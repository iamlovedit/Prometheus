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
}
