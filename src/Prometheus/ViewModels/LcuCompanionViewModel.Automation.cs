using Prism.Commands;
using Prometheus.Core.Logging;
using Prometheus.Core.Models;
using Prometheus.Desktop.Services;
using Serilog;
using Serilog.Events;

namespace Prometheus.ViewModels
{
    public sealed partial class LcuCompanionViewModel
    {
        private LcuCompanionAutomationCardViewModel[] CreateAutomationCards(
            LiveMatchSnapshot snapshot,
            LcuCompanionMode mode)
        {
            if (mode is LcuCompanionMode.Aram or LcuCompanionMode.HextechAram)
            {
                var currentChampionId =
                    LcuCompanionPresentation.GetLocalChampionId(snapshot);
                var targetChampionId = _automationSettings.AutoSwapAramBench
                    ? LcuCompanionPresentation.GetAramAutomationTarget(
                        snapshot,
                        _automationSettings.PreferredAramChampionIds,
                        mode)
                    : 0;
                return
                [
                    CreateCard(
                        Text("Companion.Automation.Current", "Current champion"),
                        currentChampionId,
                        currentChampionId > 0
                            ? Text("Companion.Status.Selected", "Selected")
                            : Text("Companion.Status.Waiting", "Waiting"),
                        currentChampionId > 0),
                    CreateCard(
                        Text("Companion.Automation.Aram", "Auto swap"),
                        targetChampionId,
                        GetAramStatus(snapshot, currentChampionId, targetChampionId),
                        _automationSettings.AutoSwapAramBench,
                        () => ToggleAutomationSetting(
                            "automation.aram_bench_swap.changed",
                            "Automatic ARAM champion swapping",
                            () => _automationSettings.AutoSwapAramBench,
                            value => _automationSettings.AutoSwapAramBench = value))
                ];
            }

            return
            [
                CreateChampionSelectCard(snapshot, "ban",
                    Text("Companion.Automation.Ban", "Auto Ban"),
                    _automationSettings.AutoBanChampion,
                    _automationSettings.PreferredBanChampionIds),
                CreateChampionSelectCard(snapshot, "pick",
                    Text("Companion.Automation.Pick", "Auto Pick"),
                    _automationSettings.AutoPickChampion,
                    _automationSettings.PreferredPickChampionIds,
                    () => ToggleAutomationSetting(
                        "automation.auto_pick.changed",
                        "Automatic champion picking",
                        () => _automationSettings.AutoPickChampion,
                        value => _automationSettings.AutoPickChampion = value))
            ];
        }

        private void ToggleAutomationSetting(
            string eventName,
            string settingName,
            Func<bool> getValue,
            Action<bool> persist)
        {
            var oldValue = getValue();
            var newValue = !oldValue;
            persist(newValue);

            var persisted = _automationSettings.LastPersistenceSucceeded;
            var properties = new Dictionary<string, object>
            {
                ["OldValue"] = oldValue,
                ["NewValue"] = newValue
            };
            if (!persisted)
            {
                properties["ErrorCode"] = "PersistenceFailed";
            }

            OperationLog.Write(
                persisted ? LogEventLevel.Information : LogEventLevel.Error,
                eventName,
                "Automation",
                "Manual",
                persisted ? "Succeeded" : "Failed",
                Guid.NewGuid(),
                "Companion",
                persisted
                    ? $"{settingName} was {(newValue ? "enabled" : "disabled")}."
                    : $"Unable to save the {settingName.ToLowerInvariant()} setting.",
                properties);
        }

        private LcuCompanionAutomationCardViewModel CreateChampionSelectCard(
            LiveMatchSnapshot snapshot,
            string actionType,
            string label,
            bool enabled,
            IReadOnlyList<int> preferredChampionIds,
            Action toggleAction = null)
        {
            var action = LcuCompanionPresentation.FindLocalAction(snapshot, actionType);
            var championId = enabled
                ? LcuCompanionPresentation.GetChampionSelectAutomationTarget(
                    action, preferredChampionIds)
                : 0;
            var status = !enabled
                ? Text("Companion.Status.Disabled", "Disabled")
                : championId <= 0
                    ? Text("Companion.Status.NotConfigured", "Not configured")
                    : action?.Completed == true
                        ? Text("Companion.Status.Completed", "Completed")
                        : action?.IsInProgress == true
                            ? Text("Companion.Status.Executing", "Executing")
                            : Text("Companion.Status.Waiting", "Waiting");
            return CreateCard(label, championId, status, enabled, toggleAction);
        }

        private string GetAramStatus(
            LiveMatchSnapshot snapshot,
            int currentChampionId,
            int targetChampionId)
        {
            if (!_automationSettings.AutoSwapAramBench)
            {
                return Text("Companion.Status.Disabled", "Disabled");
            }

            if ((_automationSettings.PreferredAramChampionIds?.Count ?? 0) == 0)
            {
                return Text("Companion.Status.NotConfigured", "Not configured");
            }

            if (targetChampionId <= 0)
            {
                return snapshot?.ChampionSelect?.BenchEnabled == true
                    ? Text("Companion.Status.NoCandidate", "No candidate on bench")
                    : Text("Companion.Status.BenchUnavailable", "Bench unavailable");
            }

            return targetChampionId == currentChampionId
                ? Text("Companion.Status.Completed", "Completed")
                : Text("Companion.Status.Executing", "Executing");
        }

        private static LcuCompanionAutomationCardViewModel CreateCard(
            string label,
            int championId,
            string status,
            bool enabled,
            Action toggleAction = null)
        {
            return new LcuCompanionAutomationCardViewModel
            {
                Label = label,
                ChampionId = championId,
                ChampionName = championId > 0 ? $"#{championId}" : "--",
                StatusText = status,
                IsEnabled = enabled,
                ToggleCommand = toggleAction is null
                    ? null
                    : new DelegateCommand(toggleAction)
            };
        }

        private async Task LoadChampionResourcesAsync(
            IReadOnlyCollection<int> championIds,
            long generation)
        {
            if (championIds.Count == 0)
            {
                return;
            }

            try
            {
                var names = await LoadChampionNamesWithRetryAsync(championIds)
                    .ConfigureAwait(false);
                var resources = await Task.WhenAll(championIds.Select(async championId =>
                {
                    string icon = string.Empty;
                    try
                    {
                        icon = await _gameResourceManager
                            .GetChampoinIconByIdAsync(championId)
                            .ConfigureAwait(false) ?? string.Empty;
                    }
                    catch (Exception exception)
                    {
                        Log.Debug(exception,
                            "Unable to load companion champion icon {ChampionId}",
                            championId);
                    }

                    return (ChampionId: championId,
                        Name: names.TryGetValue(championId, out var name)
                            ? name
                            : $"#{championId}",
                        Icon: icon);
                })).ConfigureAwait(false);

                Dispatch(() =>
                {
                    if (!_started || generation != _resourceGeneration)
                    {
                        return;
                    }

                    foreach (var resource in resources)
                    {
                        _championResources[resource.ChampionId] =
                            (resource.Name, resource.Icon);
                    }

                    ApplyCachedChampionResources(AutomationCards);
                });
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Unable to load companion champion resources");
            }
        }

        private async Task<IReadOnlyDictionary<int, string>>
            LoadChampionNamesWithRetryAsync(IReadOnlyCollection<int> championIds)
        {
            IReadOnlyDictionary<int, string> names =
                new Dictionary<int, string>();
            for (var attempt = 0; attempt < ChampionNameLoadAttempts; attempt++)
            {
                var missingChampionId = championIds.FirstOrDefault(championId =>
                    !names.ContainsKey(championId));
                if (attempt > 0)
                {
                    await Task.Delay(
                            ChampionNameRetryDelayMilliseconds * attempt)
                        .ConfigureAwait(false);
                }

                names = await GetChampionNamesAsync(missingChampionId)
                    .ConfigureAwait(false);
                if (championIds.All(names.ContainsKey))
                {
                    break;
                }
            }

            return names;
        }

        private void ApplyCachedChampionResources(
            IEnumerable<LcuCompanionAutomationCardViewModel> cards)
        {
            foreach (var card in cards)
            {
                if (_championResources.TryGetValue(card.ChampionId, out var resource))
                {
                    card.ChampionName = resource.Name;
                    card.ChampionIcon = resource.Icon;
                }
            }
        }

        private string GetChampionResourceRequestKey(
            IEnumerable<int> championIds)
        {
            return string.Join(",", championIds
                .Where(championId => !_championResources.ContainsKey(championId))
                .OrderBy(championId => championId));
        }

        private async Task<IReadOnlyDictionary<int, string>> LoadChampionNamesAsync()
        {
            var champions = await _gameResourceManager.GetChampionSummarysAsync()
                .ConfigureAwait(false) ?? [];
            return champions
                .Where(champion => champion is not null &&
                    champion.Id > 0 &&
                    !string.IsNullOrWhiteSpace(champion.Name))
                .GroupBy(champion => champion.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name.Trim());
        }

        private async Task<IReadOnlyDictionary<int, string>> GetChampionNamesAsync(
            int requiredChampionId = 0)
        {
            var task = Volatile.Read(ref _championNamesTask);
            if (task is null)
            {
                var createdTask = LoadChampionNamesAsync();
                task = Interlocked.CompareExchange(
                    ref _championNamesTask,
                    createdTask,
                    null) ?? createdTask;
            }

            try
            {
                var names = await task.ConfigureAwait(false);
                if (names.Count == 0 ||
                    (requiredChampionId > 0 && !names.ContainsKey(requiredChampionId)))
                {
                    _ = Interlocked.CompareExchange(ref _championNamesTask, null, task);
                }

                return names;
            }
            catch
            {
                _ = Interlocked.CompareExchange(ref _championNamesTask, null, task);
                throw;
            }
        }

        private async Task<string> ResolveChampionNameAsync(
            int championId,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < ChampionNameLoadAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(
                            ChampionNameRetryDelayMilliseconds * attempt,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                try
                {
                    var names = await GetChampionNamesAsync(championId)
                        .ConfigureAwait(false);
                    if (names.TryGetValue(championId, out var name) &&
                        IsResolvedChampionName(name, championId))
                    {
                        return name;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Log.Debug(exception,
                        "Unable to resolve champion name {ChampionId} on attempt {Attempt}",
                        championId,
                        attempt + 1);
                }
            }

            return null;
        }

        private static bool IsResolvedChampionName(string name, int championId)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                !string.Equals(name, $"#{championId}", StringComparison.Ordinal);
        }
    }
}
