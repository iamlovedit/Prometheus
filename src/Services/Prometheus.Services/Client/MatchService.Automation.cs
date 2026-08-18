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
    /// Coordinates ready-check, reconnect, ARAM bench and champion-select automation.
    /// </summary>
    public partial class MatchService
    {
        private void TriggerAutomation(PhaseContext context)
        {
            if (!IsCurrentPhase(context))
            {
                return;
            }

            if (context.Phase == GameflowPhase.ReadyCheck &&
                AutomationSettings.AutoAcceptReadyCheck)
            {
                StartAutoAccept(context);
            }

            if (context.Phase == GameflowPhase.Reconnect && AutomationSettings.AutoReconnect)
            {
                if (CanAutoReconnect(context))
                {
                    StartAutoReconnect(context);
                }
                else
                {
                    CancelAutoReconnect();
                }
            }
            else
            {
                CancelAutoReconnect();
            }

            if (context.Phase == GameflowPhase.ChampSelect &&
                AutomationSettings.AutoSwapAramBench)
            {
                StartAutoAramBenchSwap(context);
            }
            else
            {
                CancelAutoAramBenchSwap(resetState: true);
            }

            if (context.Phase == GameflowPhase.ChampSelect &&
                (AutomationSettings.AutoPickChampion ||
                 AutomationSettings.AutoBanChampion))
            {
                StartAutoChampionSelectAction(context);
            }
            else
            {
                CancelAutoChampionSelectAction(resetState: true);
            }
        }

        private void StartAutoAramBenchSwap(PhaseContext context)
        {
            var snapshot = GetCurrentSnapshot();
            var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            var stateSignature = BuildAramBenchSwapState(
                context, snapshot, preferredChampionIds);
            var targetChampionId = FindPreferredAramBenchChampion(
                snapshot, preferredChampionIds);
            var shouldRefreshUntilReady = targetChampionId <= 0 &&
                                          ShouldRefreshAramBenchUntilReady(
                                              snapshot, preferredChampionIds);

            CancellationTokenSource previousCts = null;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    !AutomationSettings.AutoSwapAramBench ||
                    string.Equals(_lastAramBenchSwapState, stateSignature,
                        StringComparison.Ordinal) ||
                    (string.Equals(_aramBenchSwapFailedState, stateSignature,
                        StringComparison.Ordinal) && _aramBenchSwapFailureRetryConsumed))
                {
                    return;
                }

                previousCts = _aramBenchSwapAutomationCts;
                _aramBenchSwapAutomationCts = null;
                _lastAramBenchSwapState = stateSignature;

                if (targetChampionId > 0)
                {
                    _aramBenchSwapAutomationCts =
                        CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var token = _aramBenchSwapAutomationCts.Token;
                    _aramBenchSwapAutomationTask = RunAramBenchSwapAsync(
                        context,
                        stateSignature,
                        targetChampionId,
                        token);
                }
                else if (shouldRefreshUntilReady)
                {
                    _aramBenchSwapAutomationCts =
                        CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var token = _aramBenchSwapAutomationCts.Token;
                    _aramBenchSwapAutomationTask = RefreshAramBenchUntilReadyAsync(
                        context,
                        stateSignature,
                        token);
                }
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
        }

        private async Task RefreshAramBenchUntilReadyAsync(
            PhaseContext context,
            string stateSignature,
            CancellationToken cancellationToken)
        {
            foreach (var delay in AramBenchReadinessRefreshDelays)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (!IsCurrentAramBenchReadinessState(context, stateSignature) ||
                    !_leagueClient.Connected || !_httpService.IsInitialized)
                {
                    return;
                }

                ChampionSelectSnapshot championSelect;
                try
                {
                    championSelect = await _gameService.GetChampionSelectSnapshotAsync(
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Debug(
                        "Unable to refresh ARAM champion-select readiness. ErrorType={ErrorType}",
                        exception.GetType().Name);
                    continue;
                }

                if (championSelect is null ||
                    !IsCurrentAramBenchReadinessState(context, stateSignature))
                {
                    continue;
                }

                NormalizeChampionSelect(championSelect);
                PublishResourceUpdate(snapshot =>
                {
                    snapshot.ChampionSelect = championSelect;
                    return snapshot;
                });

                var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                    .Where(championId => championId > 0)
                    .Distinct()
                    .ToArray() ?? [];
                var refreshedStateSignature = BuildAramBenchSwapState(
                    context, GetCurrentSnapshot(), preferredChampionIds);
                if (string.Equals(stateSignature, refreshedStateSignature,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                StartAutoAramBenchSwap(context);
                return;
            }
        }

        private async Task RunAramBenchSwapAsync(
            PhaseContext context,
            string stateSignature,
            int championId,
            CancellationToken cancellationToken)
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var attemptCount = 0;
            Exception lastError = null;

            try
            {
                for (var attempt = 0; attempt < AramBenchSwapRetryDelays.Length; attempt++)
                {
                    var delay = AramBenchSwapRetryDelays[attempt];
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    if (!IsCurrentAramBenchSwapState(
                            context, stateSignature, championId))
                    {
                        LogAramBenchSwapResult(
                            LogEventLevel.Information,
                            "Cancelled",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap was cancelled because the bench changed.");
                        return;
                    }

                    if (!_leagueClient.Connected || !_httpService.IsInitialized)
                    {
                        LogAramBenchSwapResult(
                            LogEventLevel.Warning,
                            "Rejected",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap was rejected because LCU is unavailable.");
                        return;
                    }

                    attemptCount++;
                    try
                    {
                        await _gameService.SwapAramBenchChampionAsync(
                            championId, cancellationToken).ConfigureAwait(false);
                        LogAramBenchSwapResult(
                            LogEventLevel.Information,
                            "Succeeded",
                            operationId,
                            context,
                            championId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic ARAM champion swap request was accepted.");
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                        Log.Debug(exception,
                            "Automatic ARAM bench swap attempt {AttemptCount} failed for champion {ChampionId}",
                            attemptCount, championId);
                    }
                }

                if (lastError is not null &&
                    IsCurrentAramBenchSwapState(context, stateSignature, championId))
                {
                    LogAramBenchSwapResult(
                        LogEventLevel.Error,
                        "Failed",
                        operationId,
                        context,
                        championId,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        "Automatic ARAM champion swap failed.",
                        lastError);
                    AllowAramBenchSwapRetry(stateSignature);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogAramBenchSwapResult(
                    LogEventLevel.Information,
                    "Cancelled",
                    operationId,
                    context,
                    championId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    "Automatic ARAM champion swap was cancelled.");
            }
        }

        private void AllowAramBenchSwapRetry(string stateSignature)
        {
            lock (_stateSync)
            {
                if (string.Equals(_lastAramBenchSwapState, stateSignature,
                        StringComparison.Ordinal))
                {
                    if (string.Equals(_aramBenchSwapFailedState, stateSignature,
                            StringComparison.Ordinal))
                    {
                        _aramBenchSwapFailureRetryConsumed = true;
                    }
                    else
                    {
                        _aramBenchSwapFailedState = stateSignature;
                        _aramBenchSwapFailureRetryConsumed = false;
                        _lastAramBenchSwapState = string.Empty;
                    }
                }
            }
        }

        private bool IsCurrentAramBenchSwapState(
            PhaseContext context,
            string stateSignature,
            int championId)
        {
            if (!IsCurrentPhase(context) ||
                context.Phase != GameflowPhase.ChampSelect ||
                !AutomationSettings.AutoSwapAramBench)
            {
                return false;
            }

            var snapshot = GetCurrentSnapshot();
            var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                .Where(id => id > 0)
                .Distinct()
                .ToArray() ?? [];
            return string.Equals(
                       stateSignature,
                       BuildAramBenchSwapState(context, snapshot, preferredChampionIds),
                       StringComparison.Ordinal) &&
                   FindPreferredAramBenchChampion(snapshot, preferredChampionIds) == championId;
        }

        private bool IsCurrentAramBenchReadinessState(
            PhaseContext context,
            string stateSignature)
        {
            if (!IsCurrentPhase(context) ||
                context.Phase != GameflowPhase.ChampSelect ||
                !AutomationSettings.AutoSwapAramBench)
            {
                return false;
            }

            var snapshot = GetCurrentSnapshot();
            var preferredChampionIds = AutomationSettings.PreferredAramChampionIds?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            return string.Equals(
                       stateSignature,
                       BuildAramBenchSwapState(context, snapshot, preferredChampionIds),
                       StringComparison.Ordinal) &&
                   FindPreferredAramBenchChampion(snapshot, preferredChampionIds) <= 0 &&
                   ShouldRefreshAramBenchUntilReady(snapshot, preferredChampionIds);
        }

        private static bool ShouldRefreshAramBenchUntilReady(
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            if (snapshot is null || preferredChampionIds is null ||
                preferredChampionIds.Count == 0 || !IsAramSession(snapshot))
            {
                return false;
            }

            var championSelect = snapshot.ChampionSelect;
            var currentChampionId = championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            return currentChampionId <= 0 ||
                   !preferredChampionIds.Contains(currentChampionId);
        }

        private static int FindPreferredAramBenchChampion(
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            if (championSelect is null || !championSelect.BenchEnabled ||
                preferredChampionIds is null || preferredChampionIds.Count == 0 ||
                !IsAramSession(snapshot))
            {
                return 0;
            }

            var currentChampionId = championSelect.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            if (currentChampionId <= 0 ||
                preferredChampionIds.Contains(currentChampionId))
            {
                return 0;
            }

            var benchChampionIds = championSelect.BenchChampions?.Where(
                    champion => champion is not null && champion.ChampionId > 0)
                .Select(champion => champion.ChampionId)
                .ToHashSet() ?? [];
            return preferredChampionIds.FirstOrDefault(benchChampionIds.Contains);
        }

        private static bool IsAramSession(LiveMatchSnapshot snapshot)
        {
            return GameModeResolver.IsAram(snapshot);
        }

        private static string BuildAramBenchSwapState(
            PhaseContext context,
            LiveMatchSnapshot snapshot,
            IReadOnlyList<int> preferredChampionIds)
        {
            var championSelect = snapshot?.ChampionSelect;
            var gameData = snapshot?.GameflowSession?.GameData;
            var lobbyConfig = snapshot?.Lobby?.GameConfig;
            var matchmakingQueueId = snapshot?.Matchmaking?.Queue?.Id ?? 0;
            var localChampionId = championSelect?.MyTeam?.FirstOrDefault(member =>
                    member?.CellId == championSelect.LocalPlayerCellId)?.ChampionId ?? 0;
            var builder = new StringBuilder()
                .Append(context.Instance).Append('|')
                .Append(gameData?.QueueId ?? 0).Append('|')
                .Append(gameData?.MapId ?? 0).Append('|')
                .Append(gameData?.GameMode).Append('|')
                .Append(lobbyConfig?.QueueId ?? 0).Append('|')
                .Append(lobbyConfig?.MapId ?? 0).Append('|')
                .Append(lobbyConfig?.GameMode).Append('|')
                .Append(matchmakingQueueId).Append('|')
                .Append(championSelect?.BenchEnabled ?? false).Append('|')
                .Append(localChampionId).Append('|');

            foreach (var champion in championSelect?.BenchChampions ?? [])
            {
                builder.Append(champion?.ChampionId ?? 0).Append(',');
            }

            builder.Append('|');
            foreach (var championId in preferredChampionIds ?? [])
            {
                builder.Append(championId).Append(',');
            }

            return builder.ToString();
        }

        private void CancelAutoAramBenchSwap(bool resetState)
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _aramBenchSwapAutomationCts;
                _aramBenchSwapAutomationCts = null;
                if (resetState)
                {
                    _lastAramBenchSwapState = string.Empty;
                    _aramBenchSwapFailedState = string.Empty;
                    _aramBenchSwapFailureRetryConsumed = false;
                }
            }

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        private static void LogAramBenchSwapResult(
            LogEventLevel level,
            string outcome,
            Guid operationId,
            PhaseContext context,
            int championId,
            int attemptCount,
            long durationMs,
            string displayMessage,
            Exception exception = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["TargetType"] = "Champion",
                ["TargetId"] = championId,
                ["GameflowPhase"] = context.RawPhase,
                ["PhaseInstance"] = context.Instance,
                ["AttemptCount"] = attemptCount,
                ["DurationMs"] = durationMs
            };
            if (exception is HttpRequestException httpException &&
                httpException.StatusCode.HasValue)
            {
                properties["HttpStatusCode"] = (int)httpException.StatusCode.Value;
            }

            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                "champ_select.bench.swap",
                "ChampionSelect",
                "Automation",
                outcome,
                operationId,
                "MatchService",
                displayMessage,
                properties,
                exception);
        }

        private void StartAutoChampionSelectAction(PhaseContext context)
        {
            var championSelect = GetCurrentSnapshot().ChampionSelect;
            var action = FindActiveLocalChampionSelectAction(championSelect);
            if (action is null)
            {
                CancelAutoChampionSelectAction(resetState: true);
                return;
            }

            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            var enabled = isPick
                ? AutomationSettings.AutoPickChampion
                : AutomationSettings.AutoBanChampion;
            if (!enabled)
            {
                CancelAutoChampionSelectAction(resetState: true);
                return;
            }

            var preferredChampionIds = (isPick
                    ? AutomationSettings.PreferredPickChampionIds
                    : AutomationSettings.PreferredBanChampionIds)?
                .Where(championId => championId > 0)
                .Distinct()
                .ToArray() ?? [];
            var stateSignature = BuildChampionSelectAutomationState(
                context, championSelect, action, preferredChampionIds);

            CancellationTokenSource previousCts;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    string.Equals(
                        _lastChampionSelectAutomationState,
                        stateSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                previousCts = _championSelectAutomationCts;
                _championSelectAutomationCts = null;
                _lastChampionSelectAutomationState = stateSignature;

                if (preferredChampionIds.Length > 0)
                {
                    _championSelectAutomationCts =
                        CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var token = _championSelectAutomationCts.Token;
                    _championSelectAutomationTask = RunAutoChampionSelectActionAsync(
                        context,
                        action,
                        preferredChampionIds,
                        token);
                }
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
        }

        private async Task RunAutoChampionSelectActionAsync(
            PhaseContext context,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds,
            CancellationToken cancellationToken)
        {
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            var attemptCount = 0;
            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            Exception lastError = null;
            var lastChampionId = 0;

            try
            {
                if (!IsCurrentChampionSelectAction(context, action))
                {
                    return;
                }

                if (!_leagueClient.Connected || !_httpService.IsInitialized)
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Warning,
                        "Rejected",
                        operationId,
                        context,
                        action,
                        0,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        "Automatic champion-select action was rejected because LCU is unavailable.");
                    return;
                }

                IReadOnlyList<int> availableChampionIds = isPick
                    ? await _gameService.GetPickableChampionIdsAsync(cancellationToken)
                        .ConfigureAwait(false)
                    : await _gameService.GetBannableChampionIdsAsync(cancellationToken)
                        .ConfigureAwait(false);
                var candidates = FindChampionSelectCandidates(
                    GetCurrentSnapshot().ChampionSelect,
                    action,
                    preferredChampionIds,
                    availableChampionIds)
                    .Take(ChampionSelectActionRetryDelays.Length)
                    .ToArray();
                if (candidates.Length == 0)
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Warning,
                        "Skipped",
                        operationId,
                        context,
                        action,
                        0,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        isPick
                            ? "Automatic champion picking was skipped because no preferred champion is available."
                            : "Automatic champion banning was skipped because no preferred champion is available.");
                    return;
                }

                for (var candidateIndex = 0;
                     candidateIndex < candidates.Length;
                     candidateIndex++)
                {
                    var delay = ChampionSelectActionRetryDelays[candidateIndex];
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    if (!IsCurrentChampionSelectAction(context, action))
                    {
                        LogChampionSelectAutomationResult(
                            LogEventLevel.Information,
                            "Cancelled",
                            operationId,
                            context,
                            action,
                            lastChampionId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            "Automatic champion-select action was cancelled because the active turn changed.");
                        return;
                    }

                    lastChampionId = candidates[candidateIndex];
                    attemptCount++;
                    try
                    {
                        await _gameService.CompleteChampionSelectActionAsync(
                                action, lastChampionId, cancellationToken)
                            .ConfigureAwait(false);
                        LogChampionSelectAutomationResult(
                            LogEventLevel.Information,
                            "Succeeded",
                            operationId,
                            context,
                            action,
                            lastChampionId,
                            attemptCount,
                            stopwatch.ElapsedMilliseconds,
                            isPick
                                ? "Automatic champion pick request was accepted."
                                : "Automatic champion ban request was accepted.");
                        return;
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                        Log.Debug(
                            exception,
                            "Automatic champion-select attempt {AttemptCount} failed for action {ActionId} and champion {ChampionId}",
                            attemptCount,
                            action.Id,
                            lastChampionId);
                    }
                }

                if (lastError is not null &&
                    IsCurrentChampionSelectAction(context, action))
                {
                    LogChampionSelectAutomationResult(
                        LogEventLevel.Error,
                        "Failed",
                        operationId,
                        context,
                        action,
                        lastChampionId,
                        attemptCount,
                        stopwatch.ElapsedMilliseconds,
                        isPick
                            ? "Automatic champion picking failed."
                            : "Automatic champion banning failed.",
                        lastError);
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                LogChampionSelectAutomationResult(
                    LogEventLevel.Information,
                    "Cancelled",
                    operationId,
                    context,
                    action,
                    lastChampionId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    "Automatic champion-select action was cancelled.");
            }
            catch (Exception exception)
            {
                LogChampionSelectAutomationResult(
                    LogEventLevel.Error,
                    "Failed",
                    operationId,
                    context,
                    action,
                    lastChampionId,
                    attemptCount,
                    stopwatch.ElapsedMilliseconds,
                    isPick
                        ? "Automatic champion picking failed."
                        : "Automatic champion banning failed.",
                    exception);
            }
        }

        private bool IsCurrentChampionSelectAction(
            PhaseContext context,
            ChampionSelectActionSnapshot expectedAction)
        {
            if (!IsCurrentPhase(context) ||
                context.Phase != GameflowPhase.ChampSelect)
            {
                return false;
            }

            var currentAction = FindActiveLocalChampionSelectAction(
                GetCurrentSnapshot().ChampionSelect);
            if (currentAction is null ||
                currentAction.Id != expectedAction.Id ||
                !string.Equals(
                    currentAction.Type,
                    expectedAction.Type,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(
                currentAction.Type, "pick", StringComparison.OrdinalIgnoreCase)
                ? AutomationSettings.AutoPickChampion
                : AutomationSettings.AutoBanChampion;
        }

        private static ChampionSelectActionSnapshot FindActiveLocalChampionSelectAction(
            ChampionSelectSnapshot championSelect)
        {
            if (championSelect is null)
            {
                return null;
            }

            return championSelect.Actions?
                .Where(round => round is not null)
                .SelectMany(round => round)
                .FirstOrDefault(action =>
                    action is not null &&
                    action.ActorCellId == championSelect.LocalPlayerCellId &&
                    action.IsInProgress &&
                    !action.Completed &&
                    (string.Equals(
                         action.Type, "pick", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         action.Type, "ban", StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<int> FindChampionSelectCandidates(
            ChampionSelectSnapshot championSelect,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds,
            IReadOnlyList<int> availableChampionIds)
        {
            var available = availableChampionIds?
                .Where(championId => championId > 0)
                .ToHashSet() ?? [];
            if (available.Count == 0)
            {
                return [];
            }

            var unavailable = new HashSet<int>();
            foreach (var championId in championSelect?.Bans?.MyTeamBans ?? [])
            {
                unavailable.Add(championId);
            }
            foreach (var championId in championSelect?.Bans?.TheirTeamBans ?? [])
            {
                unavailable.Add(championId);
            }

            if (string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var member in championSelect?.MyTeam ?? [])
                {
                    if (member?.ChampionPickIntent > 0)
                    {
                        unavailable.Add(member.ChampionPickIntent);
                    }
                }
            }

            return preferredChampionIds?
                .Where(championId =>
                    championId > 0 &&
                    available.Contains(championId) &&
                    !unavailable.Contains(championId))
                .Distinct() ?? [];
        }

        private static string BuildChampionSelectAutomationState(
            PhaseContext context,
            ChampionSelectSnapshot championSelect,
            ChampionSelectActionSnapshot action,
            IReadOnlyList<int> preferredChampionIds)
        {
            var builder = new StringBuilder()
                .Append(context.Instance).Append('|')
                .Append(action.Id).Append('|')
                .Append(action.Type).Append('|')
                .Append(action.ChampionId).Append('|')
                .Append(action.IsInProgress).Append('|')
                .Append(action.Completed).Append('|');

            foreach (var member in championSelect?.MyTeam ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(':')
                    .Append(member?.ChampionId ?? 0).Append(':')
                    .Append(member?.ChampionPickIntent ?? 0).Append(',');
            }

            builder.Append('|');
            foreach (var championId in championSelect?.Bans?.MyTeamBans ?? [])
            {
                builder.Append(championId).Append(',');
            }
            foreach (var championId in championSelect?.Bans?.TheirTeamBans ?? [])
            {
                builder.Append(championId).Append(',');
            }

            builder.Append('|');
            foreach (var championId in preferredChampionIds ?? [])
            {
                builder.Append(championId).Append(',');
            }

            return builder.ToString();
        }

        private void CancelAutoChampionSelectAction(bool resetState)
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _championSelectAutomationCts;
                _championSelectAutomationCts = null;
                if (resetState)
                {
                    _lastChampionSelectAutomationState = string.Empty;
                }
            }

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        private static void LogChampionSelectAutomationResult(
            LogEventLevel level,
            string outcome,
            Guid operationId,
            PhaseContext context,
            ChampionSelectActionSnapshot action,
            int championId,
            int attemptCount,
            long durationMs,
            string displayMessage,
            Exception exception = null)
        {
            var isPick = string.Equals(
                action.Type, "pick", StringComparison.OrdinalIgnoreCase);
            var properties = new Dictionary<string, object>
            {
                ["ActionId"] = action.Id,
                ["ChampionId"] = championId,
                ["TargetType"] = "Champion",
                ["TargetId"] = championId,
                ["GameflowPhase"] = context.RawPhase,
                ["PhaseInstance"] = context.Instance,
                ["AttemptCount"] = attemptCount,
                ["DurationMs"] = durationMs
            };
            if (exception is HttpRequestException httpException &&
                httpException.StatusCode.HasValue)
            {
                properties["HttpStatusCode"] = (int)httpException.StatusCode.Value;
            }
            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                isPick ? "champ_select.pick" : "champ_select.ban",
                "ChampionSelect",
                "Automation",
                outcome,
                operationId,
                "MatchService",
                displayMessage,
                properties,
                exception);
        }

        private void StartAutoAccept(PhaseContext context)
        {
            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || _lastAutoAcceptInstance == context.Instance ||
                    !AutomationSettings.AutoAcceptReadyCheck)
                {
                    return;
                }

                _lastAutoAcceptInstance = context.Instance;
                _acceptAutomationCts?.Cancel();
                _acceptAutomationCts?.Dispose();
                _acceptAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                token = _acceptAutomationCts.Token;
                _acceptAutomationTask = RunAutomationAsync(
                    "Automatic ready-check acceptance", context,
                    AcceptRetryDelays, _gameService.AcceptMatchAsync, token);
            }
        }

        private void StartAutoReconnect(PhaseContext context)
        {
            if (!CanAutoReconnect(context))
            {
                return;
            }

            CancellationToken token;
            lock (_stateSync)
            {
                if (!_started || context.Version != _phaseVersion ||
                    context.Token.IsCancellationRequested ||
                    _lastAutoReconnectInstance == context.Instance ||
                    !AutomationSettings.AutoReconnect)
                {
                    return;
                }

                _lastAutoReconnectInstance = context.Instance;
                _reconnectAutomationCts?.Cancel();
                _reconnectAutomationCts?.Dispose();
                _reconnectAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                token = _reconnectAutomationCts.Token;
                _reconnectAutomationTask = RunAutomationAsync(
                    "Automatic game reconnect", context,
                    ReconnectRetryDelays,
                    cancellationToken => ReconnectGameIfConfirmedAsync(
                        context, cancellationToken),
                    token,
                    () => CanAutoReconnect(context));
            }
        }

        private async Task ReconnectGameIfConfirmedAsync(
            PhaseContext context,
            CancellationToken cancellationToken)
        {
            if (!CanAutoReconnect(context) || !_leagueClient.Connected ||
                !_httpService.IsInitialized)
            {
                return;
            }

            var phaseTask = _gameService.GetGameflowPhaseAsync(cancellationToken);
            var sessionTask = _gameService.GetGameflowSessionSnapshotAsync(cancellationToken);
            await Task.WhenAll(phaseTask, sessionTask).ConfigureAwait(false);

            var rawPhase = await phaseTask.ConfigureAwait(false);
            var session = await sessionTask.ConfigureAwait(false);
            if (ParsePhase(rawPhase) != GameflowPhase.Reconnect ||
                ParsePhase(session?.Phase) != GameflowPhase.Reconnect ||
                session?.GameClient?.Running != true)
            {
                return;
            }

            if (!CanAutoReconnect(context) || !_leagueClient.Connected ||
                !_httpService.IsInitialized)
            {
                return;
            }

            await _gameService.ReconnectGameAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool CanAutoReconnect(PhaseContext context)
        {
            var snapshot = GetCurrentSnapshot();
            return context.Phase == GameflowPhase.Reconnect &&
                   snapshot.GameflowPhase == GameflowPhase.Reconnect &&
                   ParsePhase(snapshot.GameflowSession?.Phase) == GameflowPhase.Reconnect &&
                   snapshot.GameflowSession?.GameClient?.Running == true;
        }

        private void CancelAutoReconnect()
        {
            CancellationTokenSource cancellationTokenSource;
            lock (_stateSync)
            {
                cancellationTokenSource = _reconnectAutomationCts;
            }

            cancellationTokenSource?.Cancel();
        }

        private async Task RunAutomationAsync(string operationName, PhaseContext context,
            IReadOnlyList<TimeSpan> retryDelays, Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken, Func<bool> canExecute = null)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < retryDelays.Count; attempt++)
            {
                if (retryDelays[attempt] > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }

                if (!IsCurrentPhase(context) || canExecute?.Invoke() == false)
                {
                    return;
                }

                try
                {
                    await operation(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            if (lastError is not null && IsCurrentPhase(context))
            {
                PublishError($"{operationName} failed after {retryDelays.Count} attempts.",
                    lastError, false);
            }
        }

        private void HandleAutomationSettingsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(IGameAutomationSettings.AutoAcceptReadyCheck)
                or nameof(IGameAutomationSettings.AutoAccept)
                or nameof(IGameAutomationSettings.IsAutoAcceptEnabled))
            {
                if (!AutomationSettings.AutoAcceptReadyCheck)
                {
                    _acceptAutomationCts?.Cancel();
                }
                else
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }

            if (args.PropertyName is nameof(IGameAutomationSettings.AutoReconnect)
                or nameof(IGameAutomationSettings.IsAutoReconnectEnabled))
            {
                if (!AutomationSettings.AutoReconnect)
                {
                    _reconnectAutomationCts?.Cancel();
                }
                else
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }

            if (args.PropertyName is nameof(IGameAutomationSettings.AutoSwapAramBench)
                or nameof(IGameAutomationSettings.PreferredAramChampionIds))
            {
                CancelAutoAramBenchSwap(resetState: true);
                if (AutomationSettings.AutoSwapAramBench)
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }


            if (args.PropertyName is nameof(IGameAutomationSettings.AutoPickChampion)
                or nameof(IGameAutomationSettings.AutoBanChampion)
                or nameof(IGameAutomationSettings.PreferredPickChampionIds)
                or nameof(IGameAutomationSettings.PreferredBanChampionIds))
            {
                CancelAutoChampionSelectAction(resetState: true);
                if (AutomationSettings.AutoPickChampion ||
                    AutomationSettings.AutoBanChampion)
                {
                    TriggerAutomation(GetCurrentPhaseContext());
                }
            }
        }

    }
}
