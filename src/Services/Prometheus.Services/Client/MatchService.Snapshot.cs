using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Services.Interfaces;
using Prometheus.Services.Interfaces.Client;
using Serilog;
using Serilog.Events;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Prometheus.Services.Client
{
    /// <summary>
    /// Publishes immutable snapshots and handles snapshot cloning, parsing and post-game enrichment.
    /// </summary>
    public partial class MatchService
    {
        private void PublishResourceUpdate(Func<LiveMatchSnapshot, LiveMatchSnapshot> update)
        {
            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next = update(next);
                next.Error = string.Empty;
                next.Errors = Array.Empty<string>();
                next.DataQuality = _leagueClient.Connected
                    ? DataQuality.Complete
                    : DataQuality.Stale;
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private void PublishError(string message, Exception exception, bool connectionError)
        {
            var detail = exception is null ? message : $"{message} {exception.Message}";
            PublishSnapshot(snapshot =>
            {
                var next = CopySnapshot(snapshot);
                next.Error = detail;
                next.Errors = [detail];
                next.DataQuality = HasAnyData(next) ? DataQuality.Partial : DataQuality.Error;
                if (connectionError)
                {
                    next.ConnectionState = ConnectionState.Error;
                }

                return next;
            }, new SnapshotErrorLogContext(
                message,
                exception is null ? Array.Empty<Exception>() : [exception]));
        }

        private void PublishSnapshot(Func<LiveMatchSnapshot, LiveMatchSnapshot> update,
            SnapshotErrorLogContext errorLogContext = null)
        {
            lock (_publicationSync)
            {
                LiveMatchSnapshot next;
                bool shouldLogError;
                lock (_snapshotSync)
                {
                    var current = _current;
                    next = update(current);
                    if (next is null || ReferenceEquals(next, current))
                    {
                        return;
                    }
                    next.Version = ++_snapshotVersion;
                    next.UpdatedAt = DateTimeOffset.UtcNow;
                    shouldLogError = !string.IsNullOrWhiteSpace(next.Error) &&
                                     !string.Equals(current.Error, next.Error,
                                         StringComparison.Ordinal);
                    _current = next;
                }

                if (shouldLogError)
                {
                    var safeError = errorLogContext?.SafeMessage ??
                                    LiveMatchSnapshotErrorLog.SanitizeStateError(
                                        next.Error, _leagueClient.Token);
                    LiveMatchSnapshotErrorLog.Write(
                        _logger,
                        next,
                        safeError,
                        errorLogContext?.Exceptions);
                }

                // Keep version assignment and observer notification in one
                // serialized publication lane. Consumers therefore never see
                // a newer version before an older one from another thread.
                var handlers = SnapshotChanged;
                if (handlers is null)
                {
                    return;
                }

                // Serialize the immutable publication once. Each observer still receives
                // an independent object graph, so a misbehaving consumer cannot mutate
                // service state or another observer's snapshot.
                var serializedSnapshot = SerializeForConsumer(next);

                foreach (EventHandler<LiveMatchSnapshotChangedEventArgs> handler in
                         handlers.GetInvocationList())
                {
                    try
                    {
                        var args = new LiveMatchSnapshotChangedEventArgs(
                            DeserializeForConsumer(serializedSnapshot));
                        handler(this, args);
                    }
                    catch (Exception exception)
                    {
                        // Snapshot publication is not allowed to break the
                        // websocket or retry loop because one observer failed.
                        Log.Error(exception,
                            "A live-match snapshot observer failed for version {Version}",
                            next.Version);
                    }
                }
            }
        }

        private static LiveMatchSnapshot CopySnapshot(LiveMatchSnapshot source)
        {
            return new LiveMatchSnapshot
            {
                Version = source.Version,
                ConnectionState = source.ConnectionState,
                GameflowPhase = source.GameflowPhase,
                RawPhase = source.RawPhase,
                GameflowSession = source.GameflowSession,
                Lobby = source.Lobby,
                Matchmaking = source.Matchmaking,
                ReadyCheck = source.ReadyCheck,
                ChampionSelect = source.ChampionSelect,
                PostGame = source.PostGame,
                Roster = CloneRoster(source.Roster),
                UpdatedAt = source.UpdatedAt,
                DataQuality = source.DataQuality,
                Error = source.Error,
                Errors = source.Errors ?? Array.Empty<string>()
            };
        }

        private static LiveMatchRosterSnapshot CloneRoster(
            LiveMatchRosterSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchRosterSnapshot
            {
                GameId = source.GameId,
                SourcePhase = source.SourcePhase,
                Signature = source.Signature,
                IsResolving = source.IsResolving,
                MyTeam = (source.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                    .Select(ClonePlayer)
                    .ToArray(),
                TheirTeam = (source.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                    .Select(ClonePlayer)
                    .ToArray()
            };
        }

        private static LiveMatchPlayerSnapshot ClonePlayer(
            LiveMatchPlayerSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchPlayerSnapshot
            {
                Slot = source.Slot,
                CellId = source.CellId,
                ChampionId = source.ChampionId,
                Spell1Id = source.Spell1Id,
                Spell2Id = source.Spell2Id,
                ChampionIcon = source.ChampionIcon,
                Spell1Icon = source.Spell1Icon,
                Spell2Icon = source.Spell2Icon,
                Puuid = source.Puuid,
                Position = source.Position,
                DisplayName = source.DisplayName,
                IsLocalPlayer = source.IsLocalPlayer,
                IsHidden = source.IsHidden,
                IsPlaceholder = source.IsPlaceholder,
                DataState = source.DataState,
                Summoner = source.Summoner,
                SoloRank = source.SoloRank,
                RecentWins = source.RecentWins,
                RecentLosses = source.RecentLosses,
                RecentMatchCount = source.RecentMatchCount,
                AverageKda = source.AverageKda,
                RecentResults = source.RecentResults?.ToArray() ?? Array.Empty<bool>(),
                RecentMatches = (source.RecentMatches ??
                        Array.Empty<LiveMatchRecentMatchSnapshot>())
                    .Select(CloneRecentMatch)
                    .ToArray(),
                Error = source.Error
            };
        }

        private static LiveMatchRecentMatchSnapshot CloneRecentMatch(
            LiveMatchRecentMatchSnapshot source)
        {
            if (source is null)
            {
                return null;
            }

            return new LiveMatchRecentMatchSnapshot
            {
                GameId = source.GameId,
                GameCreation = source.GameCreation,
                QueueId = source.QueueId,
                GameMode = source.GameMode,
                ChampionId = source.ChampionId,
                ChampionIcon = source.ChampionIcon,
                IsWin = source.IsWin,
                Kills = source.Kills,
                Deaths = source.Deaths,
                Assists = source.Assists
            };
        }

        private static LiveMatchSnapshot PrepareForPhase(LiveMatchSnapshot source,
            GameflowPhase phase, string rawPhase)
        {
            var next = CopySnapshot(source);
            next.GameflowPhase = phase;
            next.RawPhase = rawPhase;
            next.Error = string.Empty;
            next.Errors = Array.Empty<string>();
            next.DataQuality = source.ConnectionState == ConnectionState.Connected
                ? DataQuality.Partial
                : source.DataQuality;

            if (!IsRosterPhase(phase))
            {
                next.Roster = null;
            }

            switch (phase)
            {
                case GameflowPhase.None:
                    next.Lobby = null;
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    break;
                case GameflowPhase.Lobby:
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.Matchmaking:
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.ReadyCheck:
                    next.ChampionSelect = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.ChampSelect:
                case GameflowPhase.GameStart:
                case GameflowPhase.InProgress:
                case GameflowPhase.Reconnect:
                    next.ReadyCheck = null;
                    next.PostGame = null;
                    break;
                case GameflowPhase.WaitingForStats:
                case GameflowPhase.PreEndOfGame:
                case GameflowPhase.EndOfGame:
                    next.Lobby = null;
                    next.Matchmaking = null;
                    next.ReadyCheck = null;
                    next.ChampionSelect = null;
                    break;
            }

            return next;
        }

        private static GameflowPhase ParsePhase(string rawPhase)
        {
            var normalized = new string((rawPhase ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

            return normalized switch
            {
                "none" => GameflowPhase.None,
                "lobby" => GameflowPhase.Lobby,
                "matchmaking" => GameflowPhase.Matchmaking,
                "readycheck" => GameflowPhase.ReadyCheck,
                "champselect" => GameflowPhase.ChampSelect,
                "gamestart" => GameflowPhase.GameStart,
                "inprogress" => GameflowPhase.InProgress,
                "waitingforstats" => GameflowPhase.WaitingForStats,
                "preendofgame" => GameflowPhase.PreEndOfGame,
                "endofgame" => GameflowPhase.EndOfGame,
                "reconnect" => GameflowPhase.Reconnect,
                "terminatedinerror" => GameflowPhase.TerminatedInError,
                _ => GameflowPhase.Unknown
            };
        }

        private static bool IsPostGamePhase(GameflowPhase phase)
        {
            return phase is GameflowPhase.WaitingForStats
                or GameflowPhase.PreEndOfGame
                or GameflowPhase.EndOfGame;
        }

        private static bool HasAnyData(LiveMatchSnapshot snapshot)
        {
            return snapshot.GameflowSession is not null || snapshot.Lobby is not null ||
                   snapshot.Matchmaking is not null || snapshot.ReadyCheck is not null ||
                   snapshot.ChampionSelect is not null || snapshot.PostGame is not null ||
                   !string.IsNullOrWhiteSpace(snapshot.RawPhase);
        }

        private static bool IsDelete(OnWebsocketEventArgs args)
        {
            return string.Equals(args?.EventType, "Delete", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadPhase(object data)
        {
            if (data is null)
            {
                return string.Empty;
            }

            if (data is JValue value)
            {
                return value.Value<string>() ?? string.Empty;
            }

            if (data is JToken token)
            {
                return token.Type == JTokenType.String
                    ? token.Value<string>() ?? string.Empty
                    : token.ToString().Trim().Trim('"');
            }

            return data.ToString()?.Trim().Trim('"') ?? string.Empty;
        }

        private static T DeserializeEvent<T>(object data) where T : class
        {
            if (data is null)
            {
                return null;
            }

            try
            {
                if (data is JToken token)
                {
                    return token.ToObject<T>();
                }

                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void NormalizeChampionSelect(ChampionSelectSnapshot value)
        {
            if (value is null)
            {
                return;
            }

            value.Actions ??= [];
            for (var index = 0; index < value.Actions.Count; index++)
            {
                value.Actions[index] ??= [];
            }

            value.MyTeam ??= [];
            value.TheirTeam ??= [];
            value.BenchChampions ??= [];
            foreach (var enemy in value.TheirTeam.Where(enemy => enemy is not null))
            {
                // Champion-select opponents are intentionally published only
                // as privacy placeholders, even if a client build happens to
                // include identity fields in the payload.
                enemy.Puuid = string.Empty;
                enemy.SummonerId = 0;
                enemy.ObfuscatedPuuid = string.Empty;
                enemy.ObfuscatedSummonerId = 0;
                enemy.NameVisibilityType = "HIDDEN";
            }
        }

        private static async Task<FetchResult<T>> FetchAsync<T>(string name,
            Func<Task<T>> fetch, CancellationToken cancellationToken) where T : class
        {
            try
            {
                var value = await fetch().ConfigureAwait(false);
                return FetchResult<T>.Success(value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound
                                                         or HttpStatusCode.NoContent)
            {
                return FetchResult<T>.Unavailable();
            }
            catch (Exception exception)
            {
                return FetchResult<T>.Failure(FormatError(name, exception), exception);
            }
        }

        private async Task<FetchResult<PostGameSnapshot>> FetchPostGameAsync(
            PhaseContext context, CancellationToken cancellationToken)
        {
            // The end-of-game endpoint can lag slightly behind the phase event.
            // Keep this bounded and phase-cancellable so one early 404/null does
            // not leave the result page empty for the rest of the lifecycle.
            var delays = context.Phase == GameflowPhase.EndOfGame
                ? new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(300),
                    TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1400) }
                : new[] { TimeSpan.Zero };
            FetchResult<PostGameSnapshot> result = null;
            foreach (var delay in delays)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                if (!IsCurrentPhase(context))
                {
                    return FetchResult<PostGameSnapshot>.Unavailable();
                }

                result = await FetchAsync("post-game",
                    () => _gameService.GetPostGameSnapshotAsync(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (result.Value is not null)
                {
                    return result;
                }
            }

            return result ?? FetchResult<PostGameSnapshot>.Unavailable();
        }

        private void SchedulePostGameChampionIconEnrichment(PostGameSnapshot postGame)
        {
            CancellationToken lifetimeToken;
            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null)
                {
                    return;
                }

                lifetimeToken = _lifetimeCts.Token;
            }

            _ = EnrichPostGameChampionIconsSafelyAsync(postGame, lifetimeToken);
        }

        private async Task EnrichPostGameChampionIconsSafelyAsync(
            PostGameSnapshot postGame, CancellationToken cancellationToken)
        {
            var players = (postGame.Teams ?? [])
                .Where(team => team is not null)
                .SelectMany(team => team.Players ?? [])
                .Where(player => player is not null)
                .Concat(postGame.LocalPlayer is null
                    ? []
                    : [postGame.LocalPlayer])
                .ToArray();
            var championIds = players
                .Where(player => player.ChampionId > 0 &&
                    string.IsNullOrWhiteSpace(player.ChampionIcon))
                .Select(player => player.ChampionId)
                .Distinct()
                .ToArray();
            if (championIds.Length == 0)
            {
                return;
            }

            try
            {
                var iconTasks = championIds.ToDictionary(
                    championId => championId,
                    GetRecentChampionIconAsync);
                await Task.WhenAll(iconTasks.Values).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var icons = iconTasks.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Result ?? string.Empty);
                PublishSnapshot(snapshot =>
                {
                    if (snapshot.PostGame is null ||
                        (postGame.GameId > 0 &&
                         snapshot.PostGame.GameId != postGame.GameId))
                    {
                        return snapshot;
                    }

                    var next = CloneForConsumer(snapshot);
                    ApplyPostGameChampionIcons(next.PostGame, icons);
                    return next;
                });
            }
            catch (Exception exception)
            {
                _logger.Debug(exception,
                    "Unable to enrich post-game champion icons for game {GameId}",
                    postGame.GameId);
            }
        }

        private static void ApplyPostGameChampionIcons(PostGameSnapshot postGame,
            IReadOnlyDictionary<int, string> icons)
        {
            var players = (postGame.Teams ?? [])
                .Where(team => team is not null)
                .SelectMany(team => team.Players ?? [])
                .Where(player => player is not null)
                .Concat(postGame.LocalPlayer is null
                    ? []
                    : [postGame.LocalPlayer]);
            foreach (var player in players)
            {
                if (string.IsNullOrWhiteSpace(player.ChampionIcon) &&
                    icons.TryGetValue(player.ChampionId, out var icon))
                {
                    player.ChampionIcon = icon ?? string.Empty;
                }
            }
        }

        private static string FormatError(string name, Exception exception)
        {
            return $"{name}: {exception.Message}";
        }

        private static void AddError(ICollection<string> errors, string error)
        {
            if (!string.IsNullOrWhiteSpace(error) && !errors.Contains(error))
            {
                errors.Add(error);
            }
        }

        private static void AddRefreshFailure(ICollection<string> resources,
            ICollection<Exception> exceptions, string resource, Exception exception)
        {
            if (exception is null)
            {
                return;
            }

            resources.Add(resource);
            exceptions.Add(exception);
        }

        private static async Task AwaitAutomationTaskAsync(Task task)
        {
            if (task is null)
            {
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private sealed class WritablePropertiesContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type,
                MemberSerialization memberSerialization)
            {
                return base.CreateProperties(type, memberSerialization)
                    .Where(property => property.Writable)
                    .ToList();
            }
        }

        private readonly struct PhaseContext
        {
            public PhaseContext(long version, long instance, GameflowPhase phase,
                string rawPhase, CancellationToken token)
            {
                Version = version;
                Instance = instance;
                Phase = phase;
                RawPhase = rawPhase;
                Token = token;
            }

            public long Version { get; }

            public long Instance { get; }

            public GameflowPhase Phase { get; }

            public string RawPhase { get; }

            public CancellationToken Token { get; }
        }

        private readonly record struct PhaseTransitionResult(
            PhaseContext Context,
            bool Changed);

        private sealed record PlayerPerformanceData(
            SummonerAccount Summoner,
            Rank SoloRank,
            int Wins,
            int Losses,
            double AverageKda,
            int MatchCount,
            IReadOnlyList<bool> RecentResults,
            IReadOnlyList<LiveMatchRecentMatchSnapshot> RecentMatches);

        private readonly record struct PlayerVisuals(
            string ChampionIcon,
            string Spell1Icon,
            string Spell2Icon);

        private sealed class RosterDefinition
        {
            public RosterDefinition(long gameId, string signature,
                IReadOnlyList<LiveMatchPlayerSnapshot> myTeam,
                IReadOnlyList<LiveMatchPlayerSnapshot> theirTeam)
            {
                GameId = gameId;
                Signature = signature;
                MyTeam = myTeam;
                TheirTeam = theirTeam;
            }

            public long GameId { get; }

            public string Signature { get; }

            public IReadOnlyList<LiveMatchPlayerSnapshot> MyTeam { get; }

            public IReadOnlyList<LiveMatchPlayerSnapshot> TheirTeam { get; }
        }

        private sealed class FetchResult<T> where T : class
        {
            private FetchResult(T value, bool failed, string error, Exception exception)
            {
                Value = value;
                Failed = failed;
                Error = error;
                Exception = exception;
            }

            public T Value { get; }

            public bool Failed { get; }

            public string Error { get; }

            public Exception Exception { get; }

            public static FetchResult<T> Success(T value) => new(value, false, null, null);

            public static FetchResult<T> Unavailable() => new(null, false, null, null);

            public static FetchResult<T> Failure(string error, Exception exception) =>
                new(null, true, error, exception);
        }

        private sealed record SnapshotErrorLogContext(
            string SafeMessage,
            IReadOnlyList<Exception> Exceptions);
    }
}
