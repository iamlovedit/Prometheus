using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Core.Tasks;
using Prometheus.Desktop.Services;
using Serilog;
using Serilog.Events;
using System.Diagnostics;

namespace Prometheus.ViewModels
{
    public sealed partial class LcuCompanionViewModel
    {
        private enum RuneApplyFailure
        {
            None,
            ClientUnavailable,
            ConfirmationFailed,
            Cancelled,
            Failed
        }

        private RuneApplyFailure _runeApplyFailure;

        private void UpdateRuneRecommendation(
            LiveMatchSnapshot snapshot,
            LcuCompanionMode mode)
        {
            var shouldShow = snapshot?.GameflowPhase == GameflowPhase.ChampSelect &&
                mode != LcuCompanionMode.HextechAram;
            IsRuneRecommendationVisible = shouldShow;
            if (!shouldShow)
            {
                ResetRuneRecommendation(hide: true);
                return;
            }

            var championId = LcuCompanionPresentation.GetLocalChampionId(snapshot);
            var lane = LcuCompanionPresentation.GetLocalAssignedPosition(snapshot);
            var isAram = mode == LcuCompanionMode.Aram;
            var requestKey = championId > 0
                ? $"{championId}:{lane}:{isAram}"
                : string.Empty;
            if (championId <= 0)
            {
                if (!string.IsNullOrEmpty(_runeRequestKey))
                {
                    ResetRuneRecommendation(hide: false);
                }

                RuneStatusText = Text(
                    "Companion.Runes.WaitingForChampion",
                    "Select a champion to view recommendations");
                RuneApplyButtonText = Text(
                    "Companion.Runes.Apply",
                    "Apply to League Client");
                return;
            }

            if (string.Equals(requestKey, _runeRequestKey, StringComparison.Ordinal))
            {
                if (IsRuneRecommendationLoading)
                {
                    RuneStatusText = Text(
                        "Companion.Runes.Loading",
                        "Loading rune recommendations");
                }
                else
                {
                    RefreshRunePresentation();
                }

                return;
            }

            CancelRuneOperations();
            _runeRequestKey = requestKey;
            ResetRunePresentation(clearStatus: false, resetApplying: false);
            _runeChampionName = $"#{championId}";
            IsRuneRecommendationLoading = true;
            RuneChampionText = $"#{championId}";
            RuneStatusText = Text(
                "Companion.Runes.Loading",
                "Loading rune recommendations");

            var generation = ++_runeGeneration;
            _runeRecommendationCts = new CancellationTokenSource();
            LoadRuneRecommendationAsync(
                    championId,
                    lane,
                    isAram,
                    requestKey,
                    generation,
                    _runeRecommendationCts.Token)
                .Observe("Loading companion rune recommendations");
        }

        private async Task LoadRuneRecommendationAsync(
            int championId,
            string lane,
            bool isAram,
            string requestKey,
            long generation,
            CancellationToken cancellationToken)
        {
            RuneRecommendationSet recommendations;
            try
            {
                recommendations = await _gameService.GetRuneRecommendationsAsync(
                        championId, lane, isAram, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to load companion rune recommendations for champion {ChampionId}",
                    championId);
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
                return;
            }

            if (recommendations is null)
            {
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var perks = await _gameResourceManager.GetPerksAsync()
                    .ConfigureAwait(false) ?? [];
                var metadata = perks
                    .Where(perk => perk is not null && perk.Id > 0)
                    .GroupBy(perk => perk.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                var allPerkIds = (recommendations.Popular?.SelectedPerkIds ?? [])
                    .Concat(recommendations.WinRate?.SelectedPerkIds ?? [])
                    .Where(perkId => perkId > 0)
                    .Distinct()
                    .ToArray();
                var resources = new Dictionary<int, (string Name, string Icon)>();
                foreach (var perkId in allPerkIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!metadata.TryGetValue(perkId, out var perk))
                    {
                        continue;
                    }

                    var icon = await _gameResourceManager.GetPerkIconByIdAsync(perkId)
                        .ConfigureAwait(false) ?? string.Empty;
                    resources[perkId] = (
                        string.IsNullOrWhiteSpace(perk.Name) ? $"#{perkId}" : perk.Name,
                        icon);
                }

                var championName = await ResolveChampionNameAsync(
                        championId,
                        cancellationToken)
                    .ConfigureAwait(false);
                var isChampionNameResolved = IsResolvedChampionName(
                    championName,
                    championId);
                Dispatch(() =>
                {
                    if (!CanCommitRuneResult(requestKey, generation))
                    {
                        return;
                    }

                    _runeRecommendations = recommendations;
                    _runeResources = resources;
                    _runeChampionName = isChampionNameResolved
                        ? championName
                        : $"#{championId}";
                    _isRuneChampionNameResolved = isChampionNameResolved;
                    IsRuneRecommendationLoading = false;
                    RefreshRunePresentation();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to resolve companion rune resources for champion {ChampionId}",
                    championId);
                Dispatch(() => CompleteRuneLoadFailure(requestKey, generation));
            }
        }

        private void CompleteRuneLoadFailure(string requestKey, long generation)
        {
            if (!CanCommitRuneResult(requestKey, generation))
            {
                return;
            }

            IsRuneRecommendationLoading = false;
            HasRuneRecommendation = false;
            IsRuneRecommendationValid = false;
            RuneStatusText = Text(
                "Companion.Runes.Unavailable",
                "Rune recommendations are unavailable");
        }

        private bool CanCommitRuneResult(string requestKey, long generation)
        {
            return _started &&
                generation == _runeGeneration &&
                string.Equals(requestKey, _runeRequestKey, StringComparison.Ordinal);
        }

        private void SelectRuneRecommendation(RuneRecommendationKind kind)
        {
            if (_runeRecommendations is null || IsRuneRecommendationLoading)
            {
                return;
            }

            if (_selectedRuneKind != kind)
            {
                _runeApplyFailure = RuneApplyFailure.None;
            }

            _selectedRuneKind = kind;
            RefreshRunePresentation();
        }

        private void RefreshRunePresentation()
        {
            if (_runeRecommendations is null)
            {
                return;
            }

            _selectedRuneRecommendation = _selectedRuneKind == RuneRecommendationKind.WinRate
                ? _runeRecommendations.WinRate ?? _runeRecommendations.Popular
                : _runeRecommendations.Popular ?? _runeRecommendations.WinRate;
            if (_selectedRuneRecommendation is null)
            {
                HasRuneRecommendation = false;
                IsRuneRecommendationValid = false;
                RuneStatusText = Text(
                    "Companion.Runes.Unavailable",
                    "Rune recommendations are unavailable");
                return;
            }

            IsPopularRuneSelected = _selectedRuneKind == RuneRecommendationKind.Popular;
            IsWinRateRuneSelected = _selectedRuneKind == RuneRecommendationKind.WinRate;
            HasRuneRecommendation = true;
            var laneText = GetRuneLaneText(_runeRecommendations.Lane);
            RuneChampionText = string.IsNullOrWhiteSpace(laneText)
                ? _runeChampionName
                : $"{_runeChampionName} · {laneText}";
            RuneStyleText = string.Format(
                Text("Companion.Runes.Style.Format", "{0} > {1}"),
                GetRuneStyleText(_selectedRuneRecommendation.PrimaryStyleId),
                GetRuneStyleText(_selectedRuneRecommendation.SubStyleId));
            RuneStatsText = FormatRuneStats(_selectedRuneRecommendation);
            RuneSourceText = string.IsNullOrWhiteSpace(_runeRecommendations.DataVersion)
                ? _runeRecommendations.Source
                : $"{_runeRecommendations.Source} · {_runeRecommendations.DataVersion}";

            var perkViewModels = _selectedRuneRecommendation.SelectedPerkIds
                .Select(perkId => _runeResources.TryGetValue(perkId, out var resource)
                    ? new LcuCompanionRunePerkViewModel
                    {
                        PerkId = perkId,
                        Name = resource.Name,
                        Icon = resource.Icon
                    }
                    : new LcuCompanionRunePerkViewModel
                    {
                        PerkId = perkId,
                        Name = $"#{perkId}"
                    })
                .ToArray();
            Replace(RunePerks, perkViewModels);
            IsRuneRecommendationValid = perkViewModels.Length == 9 &&
                perkViewModels.All(perk => _runeResources.ContainsKey(perk.PerkId));
            if (IsApplyingRune)
            {
                RuneStatusText = Text("Companion.Runes.Applying", "Applying rune page");
                RuneApplyButtonText = Text(
                    "Companion.Runes.Applying.Button", "Applying...");
                ApplyRuneCommand.RaiseCanExecuteChanged();
                return;
            }

            var isApplied = string.Equals(
                _appliedRuneSignature,
                GetRuneSignature(_selectedRuneRecommendation),
                StringComparison.Ordinal);
            RuneStatusText = !IsRuneRecommendationValid
                ? Text("Companion.Runes.Outdated", "Recommendation does not match this client version")
                : !_isRuneChampionNameResolved
                    ? Text("Companion.Runes.ChampionUnavailable", "Unable to resolve champion name")
                : GetRuneApplyFailureText() ?? (isApplied
                    ? Text("Companion.Runes.Applied", "Rune page applied")
                    : Text("Companion.Runes.Ready", "Ready to apply"));
            RuneApplyButtonText = isApplied && _runeApplyFailure == RuneApplyFailure.None
                ? Text("Companion.Runes.Applied.Button", "Applied")
                : Text("Companion.Runes.Apply", "Apply to League Client");
            ApplyRuneCommand.RaiseCanExecuteChanged();
        }

        private void ExecuteApplyRune()
        {
            ApplyRuneRecommendationAsync().Observe("Applying companion rune recommendation");
        }

        private bool CanApplyRune()
        {
            return _started &&
                _snapshot.GameflowPhase == GameflowPhase.ChampSelect &&
                _snapshot.ConnectionState == ConnectionState.Connected &&
                IsRuneRecommendationVisible &&
                IsRuneRecommendationValid &&
                _isRuneChampionNameResolved &&
                !IsRuneRecommendationLoading &&
                !IsApplyingRune &&
                _selectedRuneRecommendation is not null;
        }

        private async Task ApplyRuneRecommendationAsync()
        {
            if (!CanApplyRune())
            {
                return;
            }

            var recommendation = _selectedRuneRecommendation;
            var championId = _runeRecommendations?.ChampionId ?? 0;
            var queueId = LcuCompanionPresentation.GetQueueId(_snapshot);
            var requestKey = _runeRequestKey;
            var generation = _runeGeneration;
            var recommendationKind = _selectedRuneKind;
            var recommendationSignature = GetRuneSignature(recommendation);
            var operationId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            _runeApplyCts?.Cancel();
            _runeApplyCts?.Dispose();
            var cancellationTokenSource = new CancellationTokenSource();
            _runeApplyCts = cancellationTokenSource;
            _runeApplyFailure = RuneApplyFailure.None;
            IsApplyingRune = true;
            RuneStatusText = Text("Companion.Runes.Applying", "Applying rune page");
            RuneApplyButtonText = Text("Companion.Runes.Applying.Button", "Applying...");

            try
            {
                var result = await _gameService.ApplyRuneRecommendationAsync(
                    GetManagedRunePageName(),
                    recommendation,
                    cancellationTokenSource.Token);
                stopwatch.Stop();
                if (result.PageCreated)
                {
                    WriteRuneOperation(
                        LogEventLevel.Information,
                        "rune.page.create",
                        "Succeeded",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        Text("Companion.Runes.Log.Created", "Created managed rune page"));
                }

                if (result.Succeeded)
                {
                    if (CanCommitRuneResult(requestKey, generation))
                    {
                        _appliedRuneSignature = GetRuneSignature(recommendation);
                    }
                    WriteRuneOperation(
                        LogEventLevel.Information,
                        "rune.page.apply",
                        "Succeeded",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        Text("Companion.Runes.Log.Applied", "Applied recommended rune page"));
                }
                else
                {
                    var rejected = result.Status is RunePageApplyStatus.ClientUnavailable or
                        RunePageApplyStatus.InvalidRecommendation;
                    var resultText = rejected
                        ? Text("Companion.Runes.ClientUnavailable", "League Client is unavailable")
                        : Text("Companion.Runes.ConfirmationFailed", "Unable to confirm the active rune page");
                    SetRuneApplyFailure(
                        requestKey,
                        generation,
                        recommendationKind,
                        recommendationSignature,
                        rejected ? RuneApplyFailure.ClientUnavailable : RuneApplyFailure.ConfirmationFailed);
                    WriteRuneOperation(
                        rejected ? LogEventLevel.Warning : LogEventLevel.Error,
                        "rune.page.apply",
                        rejected ? "Rejected" : "Failed",
                        operationId,
                        championId,
                        queueId,
                        result.RunePageId,
                        stopwatch.ElapsedMilliseconds,
                        resultText,
                        result.Status.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                var resultText = Text(
                    "Companion.Runes.Cancelled", "Rune application cancelled");
                SetRuneApplyFailure(
                    requestKey,
                    generation,
                    recommendationKind,
                    recommendationSignature,
                    RuneApplyFailure.Cancelled);
                WriteRuneOperation(
                    LogEventLevel.Information,
                    "rune.page.apply",
                    "Cancelled",
                    operationId,
                    championId,
                    queueId,
                    0,
                    stopwatch.ElapsedMilliseconds,
                    resultText,
                    "Cancelled");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                var resultText = Text(
                    "Companion.Runes.ApplyFailed", "Unable to apply rune page");
                SetRuneApplyFailure(
                    requestKey,
                    generation,
                    recommendationKind,
                    recommendationSignature,
                    RuneApplyFailure.Failed);
                WriteRuneOperation(
                    LogEventLevel.Error,
                    "rune.page.apply",
                    "Failed",
                    operationId,
                    championId,
                    queueId,
                    0,
                    stopwatch.ElapsedMilliseconds,
                    resultText,
                    null,
                    exception);
            }
            finally
            {
                if (ReferenceEquals(_runeApplyCts, cancellationTokenSource))
                {
                    _runeApplyCts.Dispose();
                    _runeApplyCts = null;
                    IsApplyingRune = false;
                    var isCurrentOperation = CanCommitRuneResult(requestKey, generation);
                    if (_runeRecommendations is not null)
                    {
                        RefreshRunePresentation();
                    }
                    else if (isCurrentOperation)
                    {
                        if (string.IsNullOrWhiteSpace(RuneApplyButtonText) ||
                            RuneApplyButtonText == Text(
                                "Companion.Runes.Applying.Button", "Applying..."))
                        {
                            RuneApplyButtonText = Text(
                                "Companion.Runes.Apply", "Apply to League Client");
                        }
                    }
                }
            }
        }

        private void ResetRuneRecommendation(bool hide)
        {
            CancelRuneOperations();
            _runeGeneration++;
            _runeRequestKey = string.Empty;
            ResetRunePresentation(hide, resetApplying: true);
        }

        private void ResetRunePresentation(bool clearStatus, bool resetApplying)
        {
            _runeRecommendations = null;
            _selectedRuneRecommendation = null;
            _runeResources = new Dictionary<int, (string Name, string Icon)>();
            _runeChampionName = string.Empty;
            _isRuneChampionNameResolved = false;
            _selectedRuneKind = RuneRecommendationKind.Popular;
            _appliedRuneSignature = string.Empty;
            _runeApplyFailure = RuneApplyFailure.None;
            IsRuneRecommendationLoading = false;
            HasRuneRecommendation = false;
            IsRuneRecommendationValid = false;
            if (resetApplying)
            {
                IsApplyingRune = false;
            }
            IsPopularRuneSelected = true;
            IsWinRateRuneSelected = false;
            Replace(RunePerks, []);
            RuneChampionText = string.Empty;
            RuneStyleText = string.Empty;
            RuneStatsText = string.Empty;
            RuneSourceText = string.Empty;
            if (IsApplyingRune && !resetApplying)
            {
                RuneStatusText = Text("Companion.Runes.Applying", "Applying rune page");
                RuneApplyButtonText = Text(
                    "Companion.Runes.Applying.Button", "Applying...");
            }
            else
            {
                RuneApplyButtonText = Text("Companion.Runes.Apply", "Apply to League Client");
                if (clearStatus)
                {
                    RuneStatusText = string.Empty;
                }
            }
        }

        private void CancelRuneOperations()
        {
            _runeRecommendationCts?.Cancel();
            _runeRecommendationCts?.Dispose();
            _runeRecommendationCts = null;
            _runeApplyCts?.Cancel();
        }

        private void SetRuneApplyFailure(
            string requestKey,
            long generation,
            RuneRecommendationKind recommendationKind,
            string recommendationSignature,
            RuneApplyFailure failure)
        {
            if (CanCommitRuneResult(requestKey, generation) &&
                recommendationKind == _selectedRuneKind &&
                string.Equals(recommendationSignature,
                    GetRuneSignature(_selectedRuneRecommendation), StringComparison.Ordinal))
            {
                _runeApplyFailure = failure;
            }
        }

        private string GetRuneApplyFailureText()
        {
            return _runeApplyFailure switch
            {
                RuneApplyFailure.ClientUnavailable => Text(
                    "Companion.Runes.ClientUnavailable", "League Client is unavailable"),
                RuneApplyFailure.ConfirmationFailed => Text(
                    "Companion.Runes.ConfirmationFailed", "Unable to confirm the active rune page"),
                RuneApplyFailure.Cancelled => Text(
                    "Companion.Runes.Cancelled", "Rune application cancelled"),
                RuneApplyFailure.Failed => Text(
                    "Companion.Runes.ApplyFailed", "Unable to apply rune page"),
                _ => null
            };
        }

        private string FormatRuneStats(RuneRecommendationOption recommendation)
        {
            var pickRate = recommendation.PickRateBasisPoints / 100d;
            var winRate = recommendation.WinRateBasisPoints / 100d;
            return recommendation.SampleCount > 0
                ? string.Format(
                    Text("Companion.Runes.Stats.WithSample", "Pick {0:0.0}% · Win {1:0.0}% · {2:N0} games"),
                    pickRate,
                    winRate,
                    recommendation.SampleCount)
                : string.Format(
                    Text("Companion.Runes.Stats", "Pick {0:0.0}% · Win {1:0.0}%"),
                    pickRate,
                    winRate);
        }

        private string GetRuneLaneText(string lane)
        {
            return lane switch
            {
                "top" => Text("Companion.Runes.Lane.Top", "Top"),
                "jungle" => Text("Companion.Runes.Lane.Jungle", "Jungle"),
                "mid" => Text("Companion.Runes.Lane.Mid", "Mid"),
                "bottom" => Text("Companion.Runes.Lane.Bottom", "Bottom"),
                "support" => Text("Companion.Runes.Lane.Support", "Support"),
                "aram" => Text("Companion.Mode.Aram", "ARAM"),
                _ => string.Empty
            };
        }

        private string GetRuneStyleText(int styleId)
        {
            return styleId switch
            {
                8000 => Text("Companion.Runes.Style.Precision", "Precision"),
                8100 => Text("Companion.Runes.Style.Domination", "Domination"),
                8200 => Text("Companion.Runes.Style.Sorcery", "Sorcery"),
                8300 => Text("Companion.Runes.Style.Inspiration", "Inspiration"),
                8400 => Text("Companion.Runes.Style.Resolve", "Resolve"),
                _ => $"#{styleId}"
            };
        }

        private static string GetRuneSignature(RuneRecommendationOption recommendation)
        {
            return recommendation is null
                ? string.Empty
                : $"{recommendation.PrimaryStyleId}:{recommendation.SubStyleId}:" +
                  string.Join(',', recommendation.SelectedPerkIds);
        }

        private string GetManagedRunePageName()
        {
            if (!_isRuneChampionNameResolved)
            {
                throw new InvalidOperationException(
                    "A managed rune page cannot be named before the champion name is resolved.");
            }

            var recommendationName = _selectedRuneKind == RuneRecommendationKind.WinRate
                ? Text("Companion.Runes.PageName.WinRate", "Highest win rate runes")
                : Text("Companion.Runes.PageName.Popular", "Most popular runes");
            return $"{_runeChampionName} - {recommendationName} [Prometheus]";
        }

        private static void WriteRuneOperation(
            LogEventLevel level,
            string eventName,
            string outcome,
            Guid operationId,
            int championId,
            int queueId,
            long runePageId,
            long durationMs,
            string displayMessage,
            string errorCode = null,
            Exception exception = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["TargetType"] = "RunePage",
                ["TargetId"] = "PrometheusManaged",
                ["ChampionId"] = championId,
                ["QueueId"] = queueId,
                ["DurationMs"] = durationMs
            };
            if (runePageId > 0)
            {
                properties["RunePageId"] = runePageId;
            }

            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                properties["ErrorCode"] = errorCode;
            }

            if (exception is not null)
            {
                properties["ErrorType"] = exception.GetType().Name;
            }

            OperationLog.Write(
                level,
                eventName,
                "Rune",
                "Manual",
                outcome,
                operationId,
                "Companion",
                displayMessage,
                properties,
                exception);
        }
    }
}
