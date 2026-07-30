using Newtonsoft.Json;
using Prometheus.Core.Models;
using Xunit;

namespace Prometheus.Modules.ModuleName.Tests.Services
{
    public class LiveMatchModelsTests
    {
        [Fact]
        public void ChampionSelectTeamMemberSnapshot_DeserializesIdentityAndVisibilityFields()
        {
            const string json = """
                {
                  "assignedPosition": "MIDDLE",
                  "cellId": 7,
                  "championId": 134,
                  "championPickIntent": 99,
                  "nameVisibilityType": "VISIBLE",
                  "obfuscatedPuuid": "hidden-puuid",
                  "obfuscatedSummonerId": 123,
                  "puuid": "visible-puuid",
                  "selectedSkinId": 134001,
                  "spell1Id": 4,
                  "spell2Id": 32,
                  "summonerId": 16016705290,
                  "team": 2,
                  "wardSkinId": 1
                }
                """;

            var member = JsonConvert.DeserializeObject<ChampionSelectTeamMemberSnapshot>(json);

            Assert.NotNull(member);
            Assert.Equal("visible-puuid", member.Puuid);
            Assert.Equal(16016705290, member.SummonerId);
            Assert.Equal("VISIBLE", member.NameVisibilityType);
            Assert.Equal("hidden-puuid", member.ObfuscatedPuuid);
            Assert.Equal(123, member.ObfuscatedSummonerId);
            Assert.Equal(99, member.ChampionPickIntent);
            Assert.Equal(4, member.Spell1Id);
            Assert.Equal(32, member.Spell2Id);
            Assert.Equal(2, member.Team);
            Assert.Equal(1, member.WardSkinId);
        }

        [Fact]
        public void GameflowTeamMember_DeserializesIdentityAndTeamFields()
        {
            const string json = """
                {
                  "cellId": 3,
                  "championId": 22,
                  "profileIconId": 4568,
                  "puuid": "gameflow-puuid",
                  "selectedPosition": "BOTTOM",
                  "spell1Id": 4,
                  "spell2Id": 7,
                  "summonerId": 9876543210,
                  "summonerName": "Player Name",
                  "teamId": 100
                }
                """;

            var member = JsonConvert.DeserializeObject<GameflowTeamMember>(json);

            Assert.NotNull(member);
            Assert.Equal("gameflow-puuid", member.Puuid);
            Assert.Equal(9876543210, member.SummonerId);
            Assert.Equal("Player Name", member.SummonerName);
            Assert.Equal(4568, member.ProfileIconId);
            Assert.Equal("BOTTOM", member.SelectedPosition);
            Assert.Equal(4, member.Spell1Id);
            Assert.Equal(7, member.Spell2Id);
            Assert.Equal(100, member.TeamId);
        }
    }
}
