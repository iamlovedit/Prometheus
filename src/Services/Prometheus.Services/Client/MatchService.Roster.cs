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
    /// Builds, enriches and incrementally publishes live-match rosters.
    /// </summary>
    public partial class MatchService
    {
        private void ScheduleRosterRefresh(bool forcePlayerReload = false)
        {
            var source = GetCurrentSnapshot();
            if (source.GameflowPhase == GameflowPhase.GameStart)
            {
                return;
            }

            var sourceSignature = BuildRosterSourceSignature(source);
            CancellationToken lifetimeToken;
            CancellationToken phaseToken;

            lock (_stateSync)
            {
                if (!_started || _lifetimeCts is null || _phaseCts is null)
                {
                    return;
                }

                lifetimeToken = _lifetimeCts.Token;
                phaseToken = _phaseCts.Token;
            }

            CancellationTokenSource previousCts;
            CancellationTokenSource rosterCts;
            long generation;
            lock (_rosterSync)
            {
                if (!forcePlayerReload &&
                    string.Equals(_rosterSourceSignature, sourceSignature,
                        StringComparison.Ordinal))
                {
                    return;
                }

                previousCts = _rosterCts;
                _rosterSourceSignature = sourceSignature;
                generation = ++_rosterGeneration;
                rosterCts = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeToken, phaseToken);
                _rosterCts = rosterCts;
                _rosterTask = Task.CompletedTask;
            }

            previousCts?.Cancel();
            previousCts?.Dispose();

            var task = ResolveAndEnrichRosterAsync(source, sourceSignature,
                generation, forcePlayerReload, rosterCts.Token);
            lock (_rosterSync)
            {
                if (generation == _rosterGeneration &&
                    ReferenceEquals(_rosterCts, rosterCts))
                {
                    _rosterTask = task;
                }
            }
        }

        private async Task ResolveAndEnrichRosterAsync(LiveMatchSnapshot source,
            string sourceSignature, long generation, bool forcePlayerReload,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!IsRosterPhase(source.GameflowPhase))
                {
                    PublishRoster(generation, cancellationToken, null);
                    return;
                }

                RosterDefinition definition;
                if (source.GameflowPhase == GameflowPhase.ChampSelect)
                {
                    definition = BuildChampionSelectRoster(source, sourceSignature);
                }
                else
                {
                    PublishRoster(generation, cancellationToken,
                        CreateTransitionRoster(source, sourceSignature, true));
                    definition = await BuildGameflowRosterAsync(source, sourceSignature,
                        cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (definition is null)
                {
                    PublishRoster(generation, cancellationToken,
                        CreateTransitionRoster(source, sourceSignature, false));
                    MarkRosterRetryable(generation);
                    return;
                }

                if (!forcePlayerReload)
                {
                    definition = CarryForwardPlayerData(definition, source.Roster);
                }

                var roster = new LiveMatchRosterSnapshot
                {
                    GameId = definition.GameId,
                    SourcePhase = source.GameflowPhase,
                    Signature = sourceSignature,
                    IsResolving = false,
                    MyTeam = definition.MyTeam,
                    TheirTeam = definition.TheirTeam
                };
                PublishRoster(generation, cancellationToken, roster);

                var tasks = definition.MyTeam
                    .Select((player, index) => (Player: player, Index: index, IsMyTeam: true))
                    .Concat(definition.TheirTeam.Select((player, index) =>
                        (Player: player, Index: index, IsMyTeam: false)))
                    .Where(item => NeedsPlayerLoad(item.Player))
                    .Select(item => EnrichPlayerAsync(definition.GameId, item.Player,
                        item.Index, item.IsMyTeam, generation, cancellationToken))
                    .ToArray();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                PublishRosterFailure(generation, cancellationToken,
                    "Unable to assemble the live-match roster.", exception);
                MarkRosterRetryable(generation);
            }
        }

        private async Task<RosterDefinition> BuildGameflowRosterAsync(
            LiveMatchSnapshot source, string sourceSignature,
            CancellationToken cancellationToken)
        {
            var gameData = source.GameflowSession?.GameData;
            if (!HasGameflowTeams(gameData))
            {
                return null;
            }

            var currentSummoner = _currentSummoner;
            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                currentSummoner = GetChampionSelectLocalSummoner(source.ChampionSelect);
            }

            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                currentSummoner = await _summonerService.GetCurrentSummoner(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!CanLocateCurrentSummoner(gameData, currentSummoner))
            {
                return null;
            }

            var currentInTeamOne = ContainsAccount(gameData.TeamOne, currentSummoner);
            var currentInTeamTwo = ContainsAccount(gameData.TeamTwo, currentSummoner);
            if (currentInTeamOne == currentInTeamTwo)
            {
                return null;
            }

            _currentSummoner = currentSummoner;
            var mySource = currentInTeamOne ? gameData.TeamOne : gameData.TeamTwo;
            var theirSource = currentInTeamOne ? gameData.TeamTwo : gameData.TeamOne;
            var myTeam = NormalizeRoster(mySource.Select((member, index) =>
                FromGameflowMember(member, FindPlayerSelection(gameData, member), index,
                    currentSummoner)), false);
            var theirTeam = NormalizeRoster(theirSource.Select((member, index) =>
                FromGameflowMember(member, FindPlayerSelection(gameData, member), index,
                    currentSummoner)), false);
            return new RosterDefinition(gameData.GameId, sourceSignature, myTeam, theirTeam);
        }

        private static RosterDefinition BuildChampionSelectRoster(
            LiveMatchSnapshot source, string sourceSignature)
        {
            var championSelect = source.ChampionSelect;
            var myTeam = NormalizeRoster((championSelect?.MyTeam ?? [])
                .Select((member, index) => FromChampionSelectMember(member, index,
                    championSelect?.LocalPlayerCellId ?? long.MinValue, false)), false);
            var theirTeam = NormalizeRoster((championSelect?.TheirTeam ?? [])
                .Select((member, index) => FromChampionSelectMember(member, index,
                    championSelect?.LocalPlayerCellId ?? long.MinValue, true)), true);
            var gameId = (championSelect?.GameId ?? 0) > 0
                ? championSelect.GameId
                : GetGameId(source);
            return new RosterDefinition(gameId, sourceSignature, myTeam, theirTeam);
        }

        private static RosterDefinition CarryForwardPlayerData(
            RosterDefinition definition, LiveMatchRosterSnapshot previousRoster)
        {
            if (definition is null || previousRoster is null)
            {
                return definition;
            }

            var previousPlayers = (previousRoster.MyTeam ??
                    Array.Empty<LiveMatchPlayerSnapshot>())
                .Concat(previousRoster.TheirTeam ??
                    Array.Empty<LiveMatchPlayerSnapshot>())
                .Where(player => player is not null &&
                    !string.IsNullOrWhiteSpace(player.Puuid) &&
                    player.DataState is LiveMatchPlayerDataState.Loaded or
                        LiveMatchPlayerDataState.Error)
                .GroupBy(player => NormalizePuuid(player.Puuid),
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(player =>
                            player.DataState == LiveMatchPlayerDataState.Loaded)
                        .First(),
                    StringComparer.Ordinal);

            if (previousPlayers.Count == 0)
            {
                return definition;
            }

            return new RosterDefinition(definition.GameId, definition.Signature,
                definition.MyTeam.Select(player => CarryForwardPlayerData(
                    player, previousPlayers)).ToArray(),
                definition.TheirTeam.Select(player => CarryForwardPlayerData(
                    player, previousPlayers)).ToArray());
        }

        private static LiveMatchPlayerSnapshot CarryForwardPlayerData(
            LiveMatchPlayerSnapshot player,
            IReadOnlyDictionary<string, LiveMatchPlayerSnapshot> previousPlayers)
        {
            if (player is null || string.IsNullOrWhiteSpace(player.Puuid) ||
                !previousPlayers.TryGetValue(NormalizePuuid(player.Puuid),
                    out var previous))
            {
                return player;
            }

            var next = ClonePlayer(player);
            if (next.ChampionId == previous.ChampionId)
            {
                next.ChampionIcon = previous.ChampionIcon;
            }
            if (next.Spell1Id == previous.Spell1Id)
            {
                next.Spell1Icon = previous.Spell1Icon;
            }
            if (next.Spell2Id == previous.Spell2Id)
            {
                next.Spell2Icon = previous.Spell2Icon;
            }

            next.DisplayName = FirstNotEmpty(next.DisplayName, previous.DisplayName);
            if (previous.DataState == LiveMatchPlayerDataState.Error)
            {
                next.DataState = LiveMatchPlayerDataState.Error;
                next.Error = previous.Error;
                return next;
            }

            next.Summoner = previous.Summoner;
            next.SoloRank = previous.SoloRank;
            next.RecentWins = previous.RecentWins;
            next.RecentLosses = previous.RecentLosses;
            next.RecentMatchCount = previous.RecentMatchCount;
            next.AverageKda = previous.AverageKda;
            next.RecentResults = previous.RecentResults?.ToArray() ?? Array.Empty<bool>();
            next.RecentMatches = (previous.RecentMatches ??
                    Array.Empty<LiveMatchRecentMatchSnapshot>())
                .Select(CloneRecentMatch)
                .ToArray();
            next.DataState = LiveMatchPlayerDataState.Loaded;
            next.Error = string.Empty;
            return next;
        }

        private async Task EnrichPlayerAsync(long gameId, LiveMatchPlayerSnapshot player,
            int index, bool isMyTeam, long generation,
            CancellationToken cancellationToken)
        {
            var visualsTask = LoadAndPublishPlayerVisualsAsync(player, index, isMyTeam,
                generation, cancellationToken);
            if (player.DataState != LiveMatchPlayerDataState.Loading)
            {
                await visualsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var gateEntered = false;
            try
            {
                await _playerLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;

                var puuid = await ResolvePlayerPuuidAsync(player, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(puuid))
                {
                    throw new InvalidOperationException(
                        "The public player identity is unavailable.");
                }

                var performance = await GetPlayerPerformanceAsync(puuid,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                    current => ApplyPerformance(current, performance));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Error(exception,
                    "Unable to enrich live-match player data for game {GameId}, cell {CellId}",
                    gameId, player.CellId);
                PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                    current =>
                    {
                        current.DataState = LiveMatchPlayerDataState.Error;
                        current.Error = "Unable to load player performance.";
                        return current;
                    });
            }
            finally
            {
                if (gateEntered)
                {
                    _playerLoadGate.Release();
                }
            }

            await visualsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LoadAndPublishPlayerVisualsAsync(
            LiveMatchPlayerSnapshot player, int index, bool isMyTeam,
            long generation, CancellationToken cancellationToken)
        {
            var visuals = await LoadPlayerVisualsAsync(player).ConfigureAwait(false);
            PublishPlayerUpdate(generation, cancellationToken, isMyTeam, index,
                current =>
                {
                    ApplyVisuals(current, visuals);
                    return current;
                });
        }

        private async Task<PlayerVisuals> LoadPlayerVisualsAsync(
            LiveMatchPlayerSnapshot player)
        {
            try
            {
                var championTask = player.ChampionId > 0
                    ? _gameResourceManager.GetChampoinIconByIdAsync(player.ChampionId)
                    : Task.FromResult<string>(null);
                var spell1Task = player.Spell1Id > 0
                    ? _gameResourceManager.GetSpellIconByIdAsync(player.Spell1Id)
                    : Task.FromResult<string>(null);
                var spell2Task = player.Spell2Id > 0
                    ? _gameResourceManager.GetSpellIconByIdAsync(player.Spell2Id)
                    : Task.FromResult<string>(null);
                await Task.WhenAll(championTask, spell1Task, spell2Task)
                    .ConfigureAwait(false);
                return new PlayerVisuals(await championTask.ConfigureAwait(false),
                    await spell1Task.ConfigureAwait(false),
                    await spell2Task.ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                Log.Warning(exception,
                    "Unable to load live-match icons for cell {CellId}", player.CellId);
                return default;
            }
        }

        private Task<PlayerPerformanceData> GetPlayerPerformanceAsync(string puuid,
            CancellationToken cancellationToken)
        {
            var key = NormalizePuuid(puuid);
            CancellationToken playerLoadToken;
            lock (_stateSync)
            {
                playerLoadToken = _playerLoadCts?.Token ?? new CancellationToken(true);
            }

            var lazy = _playerCache.GetOrAdd(key, _ =>
                new Lazy<Task<PlayerPerformanceData>>(
                    () => LoadPlayerPerformanceAsync(puuid, playerLoadToken),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            return AwaitCachedPlayerPerformanceAsync(key, lazy)
                .WaitAsync(cancellationToken);
        }

        private async Task<string> ResolvePlayerPuuidAsync(
            LiveMatchPlayerSnapshot player, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(player?.Puuid))
            {
                return player.Puuid;
            }

            if (!TryGetCompleteRiotId(player?.DisplayName, out var riotId))
            {
                return string.Empty;
            }

            var summoner = await _summonerService.SearchSummonerByName(
                    riotId, cancellationToken)
                .ConfigureAwait(false);
            return summoner?.Puuid ?? string.Empty;
        }

        private async Task<PlayerPerformanceData> AwaitCachedPlayerPerformanceAsync(
            string key, Lazy<Task<PlayerPerformanceData>> lazy)
        {
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                if (_playerCache.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, lazy))
                {
                    _playerCache.TryRemove(key, out _);
                }

                throw;
            }
        }

        private async Task<PlayerPerformanceData> LoadPlayerPerformanceAsync(string puuid,
            CancellationToken cancellationToken)
        {
            var summonerTask = _summonerService.SearchSummonerByPuuid(
                puuid, cancellationToken);
            var rankTask = _summonerService.GetRankStatsByPuuid(puuid, cancellationToken);
            var matchesTask = _summonerService.GetMatchHistoryAsync(
                puuid, cancellationToken);

            await Task.WhenAll(summonerTask, rankTask, matchesTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var summoner = await summonerTask.ConfigureAwait(false);
            if (summoner is null)
            {
                throw new InvalidOperationException("The summoner account is unavailable.");
            }

            var rankJson = await rankTask.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rankJson))
            {
                throw new InvalidOperationException("Ranked stats are unavailable.");
            }
            var rank = ParseSoloRank(rankJson);
            var matchResult = await matchesTask.ConfigureAwait(false);
            if (matchResult is null || !matchResult.Succeeded)
            {
                throw new InvalidOperationException(matchResult?.Error ??
                    "Recent match history is unavailable.");
            }

            var matches = (matchResult.Matches ?? Array.Empty<Match>())
                .Take(RecentMatchCount)
                .ToArray();
            var recentMatches = await BuildRecentMatchesAsync(matches, cancellationToken)
                .ConfigureAwait(false);
            var wins = recentMatches.Count(value => value.IsWin);
            var losses = recentMatches.Count - wins;
            var killsAndAssists = recentMatches.Sum(value => value.Kills + value.Assists);
            var deaths = recentMatches.Sum(value => value.Deaths);
            var averageKda = recentMatches.Count == 0
                ? 0d
                : killsAndAssists / (double)Math.Max(1, deaths);

            return new PlayerPerformanceData(summoner, rank, wins, losses,
                averageKda, recentMatches.Count,
                recentMatches.Select(value => value.IsWin).ToArray(),
                recentMatches);
        }

        private async Task<IReadOnlyList<LiveMatchRecentMatchSnapshot>>
            BuildRecentMatchesAsync(IReadOnlyList<Match> matches,
                CancellationToken cancellationToken)
        {
            var recentMatches = (matches ?? Array.Empty<Match>())
                .Select(match =>
                {
                    var participant = match?.Participants?.FirstOrDefault();
                    var stats = participant?.Stats;
                    if (stats is null)
                    {
                        return null;
                    }

                    return new LiveMatchRecentMatchSnapshot
                    {
                        GameId = match.GameId,
                        GameCreation = match.GameCreation,
                        QueueId = match.QueueId,
                        GameMode = FirstNotEmpty(match.DisplayGameMode, match.GameMode),
                        ChampionId = participant.ChampionId,
                        ChampionIcon = participant.ChampionIcon ?? string.Empty,
                        IsWin = stats.Win,
                        Kills = stats.Kills,
                        Deaths = stats.Deaths,
                        Assists = stats.Assists
                    };
                })
                .Where(match => match is not null)
                .ToArray();

            var missingChampionIds = recentMatches
                .Where(match => match.ChampionId > 0 &&
                    string.IsNullOrWhiteSpace(match.ChampionIcon))
                .Select(match => match.ChampionId)
                .Distinct()
                .ToArray();
            var championIcons = new Dictionary<int, string>();
            foreach (var championId in missingChampionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                championIcons[championId] = await GetRecentChampionIconAsync(championId)
                    .ConfigureAwait(false);
            }

            foreach (var match in recentMatches)
            {
                if (string.IsNullOrWhiteSpace(match.ChampionIcon) &&
                    championIcons.TryGetValue(match.ChampionId, out var championIcon))
                {
                    match.ChampionIcon = championIcon ?? string.Empty;
                }
            }

            return recentMatches;
        }

        private async Task<string> GetRecentChampionIconAsync(int championId)
        {
            var lazy = _recentChampionIconCache.GetOrAdd(championId, id =>
                new Lazy<Task<string>>(
                    () => _gameResourceManager.GetChampoinIconByIdAsync(id),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return await lazy.Value.ConfigureAwait(false) ?? string.Empty;
            }
            catch (Exception exception)
            {
                if (_recentChampionIconCache.TryGetValue(championId, out var current) &&
                    ReferenceEquals(current, lazy))
                {
                    _recentChampionIconCache.TryRemove(championId, out _);
                }

                Log.Debug(exception,
                    "Unable to load recent-match champion icon {ChampionId}", championId);
                return string.Empty;
            }
        }

        private void PublishRoster(long generation, CancellationToken cancellationToken,
            LiveMatchRosterSnapshot roster)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken))
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                next.Roster = CloneRoster(roster);
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private void PublishRosterFailure(long generation,
            CancellationToken cancellationToken, string error, Exception exception)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken))
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                if (next.Roster is not null)
                {
                    next.Roster.IsResolving = false;
                }
                next.Error = error;
                next.Errors = (next.Errors ?? Array.Empty<string>())
                    .Concat([error])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                next.DataQuality = DataQuality.Partial;
                return next;
            }, new SnapshotErrorLogContext(
                "Unable to assemble the live-match roster.", [exception]));
        }

        private void PublishPlayerUpdate(long generation,
            CancellationToken cancellationToken, bool isMyTeam, int index,
            Func<LiveMatchPlayerSnapshot, LiveMatchPlayerSnapshot> update)
        {
            PublishSnapshot(snapshot =>
            {
                if (!IsCurrentRosterGeneration(generation, cancellationToken) ||
                    snapshot.Roster is null)
                {
                    return snapshot;
                }

                var next = CopySnapshot(snapshot);
                var roster = next.Roster;
                var team = (isMyTeam ? roster.MyTeam : roster.TheirTeam).ToArray();
                if (index < 0 || index >= team.Length)
                {
                    return snapshot;
                }

                team[index] = update(ClonePlayer(team[index])) ?? team[index];
                if (isMyTeam)
                {
                    roster.MyTeam = team;
                }
                else
                {
                    roster.TheirTeam = team;
                }
                ApplyRosterDataQuality(next);
                return next;
            });
        }

        private bool IsCurrentRosterGeneration(long generation,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            lock (_rosterSync)
            {
                return generation == _rosterGeneration &&
                    _rosterCts is not null && !_rosterCts.IsCancellationRequested;
            }
        }

        private void MarkRosterRetryable(long generation)
        {
            lock (_rosterSync)
            {
                if (generation == _rosterGeneration)
                {
                    _rosterSourceSignature = string.Empty;
                }
            }
        }

        private Task CancelRosterEnrichment(bool clearCache, bool resetSignature)
        {
            CancellationTokenSource rosterCts;
            Task rosterTask;
            lock (_rosterSync)
            {
                rosterCts = _rosterCts;
                rosterTask = _rosterTask;
                _rosterCts = null;
                _rosterTask = Task.CompletedTask;
                _rosterGeneration++;
                if (resetSignature)
                {
                    _rosterSourceSignature = string.Empty;
                }
            }

            rosterCts?.Cancel();
            rosterCts?.Dispose();
            if (clearCache)
            {
                _playerCache.Clear();
            }
            return rosterTask ?? Task.CompletedTask;
        }

        private void ResetPlayerLoadLifetime()
        {
            CancellationTokenSource previousCts;
            lock (_stateSync)
            {
                previousCts = _playerLoadCts;
                _playerLoadCts = _started && _lifetimeCts is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token)
                    : null;
            }

            previousCts?.Cancel();
            previousCts?.Dispose();
            _playerCache.Clear();
        }

        private static LiveMatchPlayerSnapshot ApplyPerformance(
            LiveMatchPlayerSnapshot player, PlayerPerformanceData performance)
        {
            player.Puuid = FirstNotEmpty(player.Puuid, performance.Summoner?.Puuid);
            player.Summoner = performance.Summoner;
            player.SoloRank = performance.SoloRank;
            player.RecentWins = performance.Wins;
            player.RecentLosses = performance.Losses;
            player.RecentMatchCount = performance.MatchCount;
            player.AverageKda = performance.AverageKda;
            player.RecentResults = performance.RecentResults.ToArray();
            player.RecentMatches = performance.RecentMatches
                .Select(CloneRecentMatch)
                .ToArray();
            player.DisplayName = FormatSummonerName(performance.Summoner,
                player.DisplayName);
            player.DataState = LiveMatchPlayerDataState.Loaded;
            player.Error = string.Empty;
            return player;
        }

        private static void ApplyVisuals(LiveMatchPlayerSnapshot player,
            PlayerVisuals visuals)
        {
            player.ChampionIcon = visuals.ChampionIcon ?? string.Empty;
            player.Spell1Icon = visuals.Spell1Icon ?? string.Empty;
            player.Spell2Icon = visuals.Spell2Icon ?? string.Empty;
        }

        private static bool NeedsPlayerLoad(LiveMatchPlayerSnapshot player)
        {
            return player is not null &&
                (player.ChampionId > 0 || player.Spell1Id > 0 || player.Spell2Id > 0 ||
                 player.DataState == LiveMatchPlayerDataState.Loading);
        }

        private static void ApplyRosterDataQuality(LiveMatchSnapshot snapshot)
        {
            if (snapshot.ConnectionState != ConnectionState.Connected ||
                snapshot.DataQuality is DataQuality.Error or DataQuality.Stale)
            {
                return;
            }

            var roster = snapshot.Roster;
            if (roster is null)
            {
                return;
            }

            var players = roster.MyTeam.Concat(roster.TheirTeam).ToArray();
            var incomplete = roster.IsResolving || players.Length != TeamSize * 2 ||
                players.Any(player => player.DataState != LiveMatchPlayerDataState.Loaded);
            snapshot.DataQuality = incomplete || (snapshot.Errors?.Count ?? 0) > 0
                ? DataQuality.Partial
                : DataQuality.Complete;
        }

        private static LiveMatchPlayerSnapshot FromChampionSelectMember(
            ChampionSelectTeamMemberSnapshot member, int index,
            long localPlayerCellId, bool isEnemy)
        {
            member ??= new ChampionSelectTeamMemberSnapshot();
            var explicitlyHidden = string.Equals(member.NameVisibilityType, "HIDDEN",
                StringComparison.OrdinalIgnoreCase);
            var hidden = isEnemy || explicitlyHidden;
            var puuid = hidden ? string.Empty : member.Puuid ?? string.Empty;
            return new LiveMatchPlayerSnapshot
            {
                Slot = index,
                CellId = member.CellId,
                ChampionId = member.ChampionId > 0
                    ? member.ChampionId
                    : member.ChampionPickIntent,
                Spell1Id = member.Spell1Id,
                Spell2Id = member.Spell2Id,
                Puuid = puuid,
                Position = member.AssignedPosition ?? string.Empty,
                DisplayName = hidden
                    ? string.Empty
                    : FormatRiotId(member.GameName, member.TagLine),
                IsLocalPlayer = member.CellId == localPlayerCellId,
                IsHidden = hidden,
                DataState = hidden
                    ? LiveMatchPlayerDataState.Hidden
                    : string.IsNullOrWhiteSpace(puuid)
                        ? LiveMatchPlayerDataState.Unavailable
                        : LiveMatchPlayerDataState.Loading
            };
        }

        private static LiveMatchPlayerSnapshot FromGameflowMember(
            GameflowTeamMember member, GameflowPlayerSelection selection, int index,
            SummonerAccount currentSummoner)
        {
            member ??= new GameflowTeamMember();
            var puuid = FirstNotEmpty(member.Puuid, selection?.Puuid);
            var displayName = member.SummonerName ?? string.Empty;
            var hidden = string.IsNullOrWhiteSpace(puuid) &&
                string.IsNullOrWhiteSpace(displayName);
            var hasPublicRiotId = TryGetCompleteRiotId(displayName, out _);
            return new LiveMatchPlayerSnapshot
            {
                Slot = index,
                CellId = member.CellId != 0 ? member.CellId : member.TeamParticipantId,
                ChampionId = member.ChampionId > 0
                    ? member.ChampionId
                    : selection?.ChampionId ?? 0,
                Spell1Id = member.Spell1Id > 0
                    ? member.Spell1Id
                    : selection?.Spell1Id ?? 0,
                Spell2Id = member.Spell2Id > 0
                    ? member.Spell2Id
                    : selection?.Spell2Id ?? 0,
                Puuid = puuid,
                Position = FirstNotEmpty(member.SelectedPosition, member.AssignedPosition),
                DisplayName = displayName,
                IsLocalPlayer = IsSameAccount(member, currentSummoner),
                IsHidden = hidden,
                DataState = hidden
                    ? LiveMatchPlayerDataState.Hidden
                    : !string.IsNullOrWhiteSpace(puuid) || hasPublicRiotId
                        ? LiveMatchPlayerDataState.Loading
                        : LiveMatchPlayerDataState.Unavailable
            };
        }

        private static IReadOnlyList<LiveMatchPlayerSnapshot> NormalizeRoster(
            IEnumerable<LiveMatchPlayerSnapshot> members, bool hiddenPlaceholders)
        {
            var values = (members ?? [])
                .OrderBy(member => PositionOrder(member.Position))
                .ThenBy(member => member.Slot)
                .Take(TeamSize)
                .ToList();
            while (values.Count < TeamSize)
            {
                values.Add(new LiveMatchPlayerSnapshot
                {
                    Slot = values.Count,
                    CellId = long.MinValue + values.Count,
                    IsHidden = hiddenPlaceholders,
                    IsPlaceholder = true,
                    DataState = hiddenPlaceholders
                        ? LiveMatchPlayerDataState.Hidden
                        : LiveMatchPlayerDataState.Placeholder
                });
            }
            return values;
        }

        private static LiveMatchRosterSnapshot CreateTransitionRoster(
            LiveMatchSnapshot source, string sourceSignature, bool isResolving)
        {
            var gameId = GetGameId(source);
            var previous = source?.Roster;
            var canRetainPrevious = previous is not null &&
                (gameId <= 0 || previous.GameId <= 0 || previous.GameId == gameId);
            return new LiveMatchRosterSnapshot
            {
                GameId = gameId,
                SourcePhase = source?.GameflowPhase ?? GameflowPhase.Unknown,
                Signature = sourceSignature,
                IsResolving = isResolving,
                MyTeam = canRetainPrevious
                    ? (previous.MyTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                        .Select(ClonePlayer).ToArray()
                    : Array.Empty<LiveMatchPlayerSnapshot>(),
                TheirTeam = canRetainPrevious
                    ? (previous.TheirTeam ?? Array.Empty<LiveMatchPlayerSnapshot>())
                        .Select(ClonePlayer).ToArray()
                    : Array.Empty<LiveMatchPlayerSnapshot>()
            };
        }

        private static string BuildRosterSourceSignature(LiveMatchSnapshot snapshot)
        {
            var builder = new StringBuilder()
                .Append(snapshot.GameflowPhase)
                .Append(':')
                .Append(GetGameId(snapshot));
            var isGameflowPhase = snapshot.GameflowPhase is GameflowPhase.InProgress or
                GameflowPhase.Reconnect;
            if (snapshot.GameflowPhase == GameflowPhase.ChampSelect)
            {
                var championSelect = snapshot.ChampionSelect;
                builder.Append(':').Append(championSelect?.LocalPlayerCellId ?? 0);
                AppendChampionSelectSignature(builder, championSelect?.MyTeam, false);
                AppendChampionSelectSignature(builder, championSelect?.TheirTeam, true);
            }
            else if (isGameflowPhase)
            {
                AppendGameflowSignature(builder,
                    snapshot.GameflowSession?.GameData?.TeamOne);
                AppendGameflowSignature(builder,
                    snapshot.GameflowSession?.GameData?.TeamTwo);
                AppendGameflowSelectionSignature(builder,
                    snapshot.GameflowSession?.GameData?.PlayerChampionSelections);
            }
            return builder.ToString();
        }

        private static void AppendChampionSelectSignature(StringBuilder builder,
            IEnumerable<ChampionSelectTeamMemberSnapshot> team, bool hideIdentity)
        {
            builder.Append('|');
            foreach (var member in team ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(',')
                    .Append(member?.ChampionId ?? 0).Append(',')
                    .Append(member?.ChampionPickIntent ?? 0).Append(',')
                    .Append(member?.Spell1Id ?? 0).Append(',')
                    .Append(member?.Spell2Id ?? 0).Append(',')
                    .Append(member?.AssignedPosition).Append(',')
                    .Append(member?.NameVisibilityType).Append(',');
                if (!hideIdentity)
                {
                    builder.Append(member?.Puuid).Append(',')
                        .Append(member?.GameName).Append(',')
                        .Append(member?.TagLine);
                }
                builder.Append(';');
            }
        }

        private static void AppendGameflowSignature(StringBuilder builder,
            IEnumerable<GameflowTeamMember> team)
        {
            builder.Append('|');
            foreach (var member in team ?? [])
            {
                builder.Append(member?.CellId ?? 0).Append(',')
                    .Append(member?.TeamParticipantId ?? 0).Append(',')
                    .Append(member?.ChampionId ?? 0).Append(',')
                    .Append(member?.Spell1Id ?? 0).Append(',')
                    .Append(member?.Spell2Id ?? 0).Append(',')
                    .Append(member?.Puuid).Append(',')
                    .Append(member?.SummonerId ?? 0).Append(',')
                    .Append(member?.SummonerName).Append(',')
                    .Append(member?.SelectedPosition).Append(',')
                    .Append(member?.AssignedPosition).Append(';');
            }
        }

        private static void AppendGameflowSelectionSignature(StringBuilder builder,
            IEnumerable<GameflowPlayerSelection> selections)
        {
            builder.Append('|');
            foreach (var selection in selections ?? [])
            {
                builder.Append(selection?.CellId ?? 0).Append(',')
                    .Append(selection?.Puuid).Append(',')
                    .Append(selection?.ChampionId ?? 0).Append(',')
                    .Append(selection?.Spell1Id ?? 0).Append(',')
                    .Append(selection?.Spell2Id ?? 0).Append(';');
            }
        }

        private static bool HasGameflowTeams(GameflowGameData gameData)
        {
            return gameData is not null && (gameData.TeamOne?.Count ?? 0) > 0 &&
                (gameData.TeamTwo?.Count ?? 0) > 0;
        }

        private static bool CanLocateCurrentSummoner(GameflowGameData gameData,
            SummonerAccount summoner)
        {
            return gameData is not null && summoner is not null &&
                (ContainsAccount(gameData.TeamOne, summoner) ||
                 ContainsAccount(gameData.TeamTwo, summoner));
        }

        private static SummonerAccount GetChampionSelectLocalSummoner(
            ChampionSelectSnapshot championSelect)
        {
            var member = championSelect?.MyTeam?.FirstOrDefault(value =>
                value is not null && value.CellId == championSelect.LocalPlayerCellId);
            if (member is null ||
                (string.IsNullOrWhiteSpace(member.Puuid) && member.SummonerId <= 0))
            {
                return null;
            }

            return new SummonerAccount
            {
                Puuid = member.Puuid,
                SummonerId = member.SummonerId,
                GameName = member.GameName,
                TagLine = member.TagLine
            };
        }

        private static GameflowPlayerSelection FindPlayerSelection(
            GameflowGameData gameData, GameflowTeamMember member)
        {
            var puuid = NormalizePuuid(member?.Puuid);
            var selections = gameData?.PlayerChampionSelections ?? [];
            if (!string.IsNullOrWhiteSpace(puuid))
            {
                return selections.FirstOrDefault(selection =>
                    string.Equals(NormalizePuuid(selection?.Puuid), puuid,
                        StringComparison.Ordinal));
            }

            if ((member?.CellId ?? 0) != 0)
            {
                var byCellId = selections.FirstOrDefault(selection =>
                    SelectionMatchesMember(selection, member, member.CellId));
                if (byCellId is not null)
                {
                    return byCellId;
                }
            }

            if ((member?.TeamParticipantId ?? 0) != 0)
            {
                return selections.FirstOrDefault(selection =>
                    SelectionMatchesMember(
                        selection, member, member.TeamParticipantId));
            }

            return null;
        }

        private static bool SelectionMatchesMember(GameflowPlayerSelection selection,
            GameflowTeamMember member, long expectedCellId)
        {
            return selection is not null && member is not null &&
                selection.CellId == expectedCellId &&
                !string.IsNullOrWhiteSpace(selection.Puuid) &&
                (selection.ChampionId <= 0 || member.ChampionId <= 0 ||
                 selection.ChampionId == member.ChampionId);
        }

        private static bool ContainsAccount(IEnumerable<GameflowTeamMember> team,
            SummonerAccount summoner)
        {
            return (team ?? []).Any(member => IsSameAccount(member, summoner));
        }

        private static bool IsSameAccount(GameflowTeamMember member,
            SummonerAccount summoner)
        {
            if (member is null || summoner is null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(member.Puuid) &&
                    !string.IsNullOrWhiteSpace(summoner.Puuid) &&
                    string.Equals(member.Puuid, summoner.Puuid,
                        StringComparison.OrdinalIgnoreCase) ||
                member.SummonerId > 0 && summoner.SummonerId > 0 &&
                    member.SummonerId == summoner.SummonerId;
        }

        private static Rank ParseSoloRank(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JObject.Parse(json)["queueMap"]?["RANKED_SOLO_5x5"]?.ToObject<Rank>();
        }

        private static int PositionOrder(string position)
        {
            return NormalizePosition(position) switch
            {
                "TOP" => 0,
                "JUNGLE" => 1,
                "MIDDLE" => 2,
                "BOTTOM" => 3,
                "UTILITY" => 4,
                _ => 5
            };
        }

        private static string NormalizePosition(string position)
        {
            return position?.Trim().ToUpperInvariant() switch
            {
                "JUNGLE" or "JUG" => "JUNGLE",
                "MIDDLE" or "MID" => "MIDDLE",
                "BOTTOM" or "BOT" => "BOTTOM",
                "UTILITY" or "SUPPORT" or "SUP" => "UTILITY",
                "TOP" => "TOP",
                _ => string.Empty
            };
        }

    }
}
