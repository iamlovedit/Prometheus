using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Produces a detached, privacy-safe JSON representation of an LCU websocket event.
    /// Raw event data must never be passed to a logger before this sanitizer succeeds.
    /// </summary>
    internal static class WebsocketEventLogSanitizer
    {
        internal const string RedactedValue = "[REDACTED]";
        internal const string UnavailableValue = "[UNAVAILABLE]";

        private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.Ordinal)
        {
            "accesstoken",
            "accountids",
            "accountid",
            "address",
            "alias",
            "apikey",
            "auth",
            "authentication",
            "authorization",
            "authtoken",
            "body",
            "chat",
            "clipboard",
            "clientsecret",
            "commandline",
            "content",
            "conversationid",
            "cid",
            "cookie",
            "credential",
            "credentials",
            "deviceid",
            "directory",
            "directorypath",
            "displayname",
            "email",
            "entitlementstoken",
            "executablepath",
            "filepath",
            "folder",
            "fullpath",
            "gamename",
            "guid",
            "id",
            "idtoken",
            "installpath",
            "internalname",
            "ipaddress",
            "jid",
            "jwt",
            "machineid",
            "message",
            "messages",
            "memberid",
            "name",
            "nickname",
            "ownerid",
            "passphrase",
            "password",
            "path",
            "phone",
            "phonenumber",
            "pid",
            "playerid",
            "participantid",
            "puuid",
            "puuids",
            "query",
            "querystring",
            "refreshtoken",
            "remotingauthtoken",
            "riotid",
            "riotids",
            "riotidgamename",
            "riotidtagline",
            "search",
            "searchterm",
            "secret",
            "sessionid",
            "sender",
            "senderid",
            "recipient",
            "recipientid",
            "setcookie",
            "signature",
            "statusmessage",
            "statusmsg",
            "subject",
            "summonerid",
            "summonerids",
            "summonername",
            "tagline",
            "text",
            "token",
            "toid",
            "topid",
            "fromid",
            "frompid",
            "userid",
            "username",
            "uuid",
        };

        private static readonly HashSet<string> IdentityContainerNames = new(StringComparer.Ordinal)
        {
            "account",
            "accounts",
            "conversation",
            "conversations",
            "member",
            "members",
            "participant",
            "participants",
            "player",
            "players",
            "summoner",
            "summoners",
            "user",
            "users",
        };

        private static readonly HashSet<string> UriPropertyNames = new(StringComparer.Ordinal)
        {
            "endpoint",
            "href",
            "uri",
            "url",
        };

        internal static WebsocketEventLogSanitizationResult Sanitize(
            object data,
            params string[] knownSecrets)
        {
            try
            {
                var secrets = BuildSecretSet(knownSecrets);
                var redactedFieldCount = 0;
                var redactedPropertyIndex = 0;
                var source = data switch
                {
                    null => JValue.CreateNull(),
                    JToken token => token,
                    _ => JToken.FromObject(data),
                };

                var sanitized = SanitizeToken(
                    source,
                    null,
                    false,
                    secrets,
                    ref redactedFieldCount,
                    ref redactedPropertyIndex);

                return new WebsocketEventLogSanitizationResult(
                    sanitized.ToString(Formatting.None),
                    redactedFieldCount,
                    false,
                    null);
            }
            catch (Exception exception)
            {
                return new WebsocketEventLogSanitizationResult(
                    UnavailableValue,
                    0,
                    true,
                    exception.GetType().Name);
            }
        }

        internal static string SanitizeUri(string uri, params string[] knownSecrets)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return string.Empty;
            }

            var secrets = BuildSecretSet(knownSecrets);
            return SanitizeUriCore(uri, secrets, out _);
        }

        internal static string SanitizeScalar(string value, params string[] knownSecrets)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            var secrets = BuildSecretSet(knownSecrets);
            return ContainsKnownSecret(value, secrets) || IsCredentialText(value)
                ? RedactedValue
                : value;
        }

        private static JToken SanitizeToken(
            JToken token,
            string propertyName,
            bool identityContext,
            IReadOnlyList<string> knownSecrets,
            ref int redactedFieldCount,
            ref int redactedPropertyIndex)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                {
                    var result = new JObject();
                    var currentIdentityContext = identityContext ||
                                                 IsIdentityContainer(propertyName);
                    foreach (var property in ((JObject)token).Properties())
                    {
                        var normalizedName = NormalizePropertyName(property.Name);
                        var outputName = SanitizePropertyName(
                            property.Name,
                            knownSecrets,
                            ref redactedFieldCount,
                            ref redactedPropertyIndex);

                        if (IsSensitiveProperty(normalizedName) ||
                            (currentIdentityContext && normalizedName == "id"))
                        {
                            result.Add(outputName, RedactedValue);
                            redactedFieldCount++;
                            continue;
                        }

                        if (UriPropertyNames.Contains(normalizedName) &&
                            property.Value.Type == JTokenType.String)
                        {
                            var sanitizedUri = SanitizeUriCore(
                                property.Value.Value<string>(),
                                knownSecrets,
                                out var uriChanged);
                            result.Add(outputName, sanitizedUri);
                            if (uriChanged)
                            {
                                redactedFieldCount++;
                            }

                            continue;
                        }

                        result.Add(outputName, SanitizeToken(
                            property.Value,
                            property.Name,
                            currentIdentityContext || IsIdentityContainer(property.Name),
                            knownSecrets,
                            ref redactedFieldCount,
                            ref redactedPropertyIndex));
                    }

                    return result;
                }
                case JTokenType.Array:
                {
                    var result = new JArray();
                    var childIdentityContext = identityContext || IsIdentityContainer(propertyName);
                    foreach (var item in (JArray)token)
                    {
                        result.Add(SanitizeToken(
                            item,
                            propertyName,
                            childIdentityContext,
                            knownSecrets,
                            ref redactedFieldCount,
                            ref redactedPropertyIndex));
                    }

                    return result;
                }
                case JTokenType.String:
                    return SanitizeString(
                        token.Value<string>(),
                        propertyName,
                        identityContext,
                        knownSecrets,
                        ref redactedFieldCount,
                        ref redactedPropertyIndex);
                default:
                    return token.DeepClone();
            }
        }

        private static JToken SanitizeString(
            string value,
            string propertyName,
            bool identityContext,
            IReadOnlyList<string> knownSecrets,
            ref int redactedFieldCount,
            ref int redactedPropertyIndex)
        {
            if (value is null)
            {
                return JValue.CreateNull();
            }

            if (ContainsKnownSecret(value, knownSecrets) ||
                IsCredentialText(value) ||
                IsAbsoluteLocalPath(value) ||
                IsOpaqueIdentifier(value) ||
                (identityContext && NormalizePropertyName(propertyName) == "id"))
            {
                redactedFieldCount++;
                return new JValue(RedactedValue);
            }

            if (LooksLikeJson(value))
            {
                try
                {
                    var nested = JToken.Parse(value);
                    if (nested.Type is JTokenType.Object or JTokenType.Array)
                    {
                        var sanitizedNested = SanitizeToken(
                            nested,
                            propertyName,
                            identityContext,
                            knownSecrets,
                            ref redactedFieldCount,
                            ref redactedPropertyIndex);
                        return new JValue(sanitizedNested.ToString(Formatting.None));
                    }
                }
                catch (JsonException)
                {
                    redactedFieldCount++;
                    return new JValue(RedactedValue);
                }
            }

            return new JValue(value);
        }

        private static string SanitizePropertyName(
            string propertyName,
            IReadOnlyList<string> knownSecrets,
            ref int redactedFieldCount,
            ref int redactedPropertyIndex)
        {
            if (!ContainsKnownSecret(propertyName, knownSecrets))
            {
                return propertyName;
            }

            redactedFieldCount++;
            redactedPropertyIndex++;
            return $"redactedField{redactedPropertyIndex}";
        }

        private static string SanitizeUriCore(
            string uri,
            IReadOnlyList<string> knownSecrets,
            out bool changed)
        {
            changed = false;
            if (string.IsNullOrWhiteSpace(uri))
            {
                return string.Empty;
            }

            var sanitized = uri.Trim();
            var suffixIndex = sanitized.IndexOfAny(['?', '#']);
            if (suffixIndex >= 0)
            {
                sanitized = sanitized[..suffixIndex];
                changed = true;
            }

            if (ContainsKnownSecret(sanitized, knownSecrets) || IsCredentialText(sanitized))
            {
                changed = true;
                return RedactedValue;
            }

            var segments = sanitized.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                var previousSegment = index > 0 ? segments[index - 1] : null;
                if (ShouldRedactUriSegment(segment, previousSegment))
                {
                    segments[index] = RedactedValue;
                    changed = true;
                }
            }

            return string.Join('/', segments);
        }

        private static bool ShouldRedactUriSegment(string segment, string previousSegment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return false;
            }

            if (IsIdentityContainer(previousSegment))
            {
                return true;
            }

            if (segment.Length < 24)
            {
                return false;
            }

            return segment.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '=');
        }

        private static bool IsSensitiveProperty(string normalizedName)
        {
            if (SensitivePropertyNames.Contains(normalizedName))
            {
                return true;
            }

            return normalizedName.EndsWith("token", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("password", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("secret", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("puuid", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("puuids", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("summonerid", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("summonerids", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("accountid", StringComparison.Ordinal) ||
                   normalizedName.EndsWith("accountids", StringComparison.Ordinal);
        }

        private static bool IsIdentityContainer(string propertyName)
        {
            return IdentityContainerNames.Contains(NormalizePropertyName(propertyName));
        }

        private static string NormalizePropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return string.Empty;
            }

            return new string(propertyName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static IReadOnlyList<string> BuildSecretSet(IEnumerable<string> knownSecrets)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (knownSecrets is null)
            {
                return result.ToArray();
            }

            foreach (var secret in knownSecrets)
            {
                if (string.IsNullOrWhiteSpace(secret) || secret.Length < 8)
                {
                    continue;
                }

                result.Add(secret);
                result.Add(Uri.EscapeDataString(secret));
                result.Add(Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{secret}")));
            }

            return result.ToArray();
        }

        private static bool ContainsKnownSecret(string value, IReadOnlyList<string> knownSecrets)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var secret in knownSecrets)
            {
                if (value.Contains(secret, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCredentialText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.Contains("--remoting-auth-token", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("authorization:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length > 7;
            }

            if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var credential = value[6..].Trim();
            return credential.Length > 0 && credential.All(character =>
                char.IsLetterOrDigit(character) || character is '+' or '/' or '=');
        }

        private static bool IsOpaqueIdentifier(string value)
        {
            var trimmed = value.Trim();
            if (Guid.TryParse(trimmed, out _))
            {
                return true;
            }

            return trimmed.Length >= 40 && trimmed.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '=');
        }

        private static bool IsAbsoluteLocalPath(string value)
        {
            try
            {
                return Path.IsPathFullyQualified(value) ||
                       (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool LooksLikeJson(string value)
        {
            var trimmed = value.AsSpan().Trim();
            return trimmed.Length > 0 && trimmed[0] is '{' or '[';
        }
    }

    internal sealed class WebsocketEventLogSanitizationResult
    {
        internal WebsocketEventLogSanitizationResult(
            string data,
            int redactedFieldCount,
            bool failed,
            string errorType)
        {
            Data = data;
            RedactedFieldCount = redactedFieldCount;
            Failed = failed;
            ErrorType = errorType;
        }

        internal string Data { get; }

        internal int RedactedFieldCount { get; }

        internal bool Failed { get; }

        internal string ErrorType { get; }
    }
}
