using Prometheus.Core.Models;
using Prometheus.Core.Presentation;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Presentation
{
    public class LiveMatchPlayerTextFormatterTests
    {
        [Fact]
        public void FormatRank_UsesLocalizedTierAndDivision()
        {
            var rank = new Rank
            {
                Tier = Tier.EMERALD,
                Division = nameof(Division.II),
                LeaguePoints = 55
            };

            var result = LiveMatchPlayerTextFormatter.FormatRank(
                rank,
                (key, fallback) => key == "Career.Rank.Tier.Emerald"
                    ? "Emerald localized"
                    : fallback);

            Assert.Equal("Emerald localized II · 55 LP", result);
        }

        [Fact]
        public void FormatDisplayName_UsesRiotIdThenDisplayNameThenFallback()
        {
            var player = new LiveMatchPlayerSnapshot
            {
                Summoner = new SummonerAccount
                {
                    GameName = "Player",
                    TagLine = "TST"
                }
            };

            Assert.Equal("Player#TST",
                LiveMatchPlayerTextFormatter.FormatDisplayName(player, "You", "Unknown"));

            player.Summoner = null;
            player.DisplayName = "Legacy name";
            Assert.Equal("Legacy name",
                LiveMatchPlayerTextFormatter.FormatDisplayName(player, "You", "Unknown"));

            player.DisplayName = string.Empty;
            player.IsLocalPlayer = true;
            Assert.Equal("You",
                LiveMatchPlayerTextFormatter.FormatDisplayName(player, "You", "Unknown"));
        }

        [Fact]
        public void FormatDisplayName_WithLazyFallbacks_DoesNotResolveUnusedFallback()
        {
            var player = new LiveMatchPlayerSnapshot
            {
                Summoner = new SummonerAccount
                {
                    GameName = "Player",
                    TagLine = "TST"
                }
            };
            var localFallbackCalled = false;
            var unknownFallbackCalled = false;

            var result = LiveMatchPlayerTextFormatter.FormatDisplayName(
                player,
                () =>
                {
                    localFallbackCalled = true;
                    return "You";
                },
                () =>
                {
                    unknownFallbackCalled = true;
                    return "Unknown";
                });

            Assert.Equal("Player#TST", result);
            Assert.False(localFallbackCalled);
            Assert.False(unknownFallbackCalled);
        }
    }
}
