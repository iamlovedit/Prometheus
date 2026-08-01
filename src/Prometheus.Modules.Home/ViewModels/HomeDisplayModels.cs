namespace Prometheus.Modules.Home.ViewModels
{
    public sealed class HomeTeamMemberViewModel
    {
        public long CellId { get; init; }

        public string ChampionIcon { get; init; }

        public string Spell1Icon { get; init; }

        public string Spell2Icon { get; init; }

        public string DisplayName { get; init; }

        public string Position { get; init; }

        public bool IsLocalPlayer { get; init; }

        public bool IsHidden { get; init; }
    }

    public sealed class HomePreferredChampionViewModel
    {
        public int Priority { get; init; }

        public int ChampionId { get; init; }

        public string Name { get; init; }

        public string IconUri { get; init; }
    }

    public sealed class HomeRecentMatchViewModel
    {
        public long GameId { get; init; }

        public string ChampionIcon { get; init; }

        public string ChampionName { get; init; }

        public string GameMode { get; init; }

        public string Kda { get; init; }

        public string PlayedAt { get; init; }

        public bool IsWin { get; init; }
    }
}
