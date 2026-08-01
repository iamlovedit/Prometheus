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
                  "gameName": "Visible Player",
                  "nameVisibilityType": "VISIBLE",
                  "obfuscatedPuuid": "hidden-puuid",
                  "obfuscatedSummonerId": 123,
                  "puuid": "visible-puuid",
                  "selectedSkinId": 134001,
                  "spell1Id": 4,
                  "spell2Id": 32,
                  "summonerId": 16016705290,
                  "tagLine": "CN1",
                  "team": 2,
                  "wardSkinId": 1
                }
                """;

            var member = JsonConvert.DeserializeObject<ChampionSelectTeamMemberSnapshot>(json);

            Assert.NotNull(member);
            Assert.Equal("visible-puuid", member.Puuid);
            Assert.Equal(16016705290, member.SummonerId);
            Assert.Equal("Visible Player", member.GameName);
            Assert.Equal("CN1", member.TagLine);
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

        [Fact]
        public void GameflowGameData_DeserializesRealTeamAndChampionSelectionShape()
        {
            const string json = """
                {
                  "gameId": 500838514588,
                  "playerChampionSelections": [
                    {
                      "championId": 910,
                      "puuid": "team-two-local",
                      "selectedSkinIndex": 3,
                      "spell1Id": 6,
                      "spell2Id": 4
                    },
                    {
                      "championId": 161,
                      "puuid": "team-one-enemy",
                      "selectedSkinIndex": 5,
                      "spell1Id": 32,
                      "spell2Id": 4
                    }
                  ],
                  "teamOne": [
                    {
                      "championId": 161,
                      "profileIconId": 19,
                      "puuid": "team-one-enemy",
                      "selectedPosition": "NONE",
                      "summonerId": 16330908587,
                      "summonerName": "",
                      "teamParticipantId": 1
                    }
                  ],
                  "teamTwo": [
                    {
                      "championId": 910,
                      "profileIconId": 6379,
                      "puuid": "team-two-local",
                      "selectedPosition": "NONE",
                      "summonerId": 15615783905,
                      "summonerName": "",
                      "teamParticipantId": 5
                    }
                  ]
                }
                """;

            var gameData = JsonConvert.DeserializeObject<GameflowGameData>(json);

            Assert.NotNull(gameData);
            Assert.Equal(500838514588, gameData.GameId);
            var enemy = Assert.Single(gameData.TeamOne);
            Assert.Equal("team-one-enemy", enemy.Puuid);
            Assert.Equal(16330908587, enemy.SummonerId);
            Assert.Equal(1, enemy.TeamParticipantId);
            var local = Assert.Single(gameData.TeamTwo);
            Assert.Equal("team-two-local", local.Puuid);
            Assert.Equal(15615783905, local.SummonerId);
            Assert.Equal(5, local.TeamParticipantId);

            var localSelection = Assert.Single(gameData.PlayerChampionSelections,
                selection => selection.Puuid == "team-two-local");
            Assert.Equal(910, localSelection.ChampionId);
            Assert.Equal(3, localSelection.SelectedSkinIndex);
            Assert.Equal(6, localSelection.Spell1Id);
            Assert.Equal(4, localSelection.Spell2Id);
            var enemySelection = Assert.Single(gameData.PlayerChampionSelections,
                selection => selection.Puuid == "team-one-enemy");
            Assert.Equal(32, enemySelection.Spell1Id);
            Assert.Equal(4, enemySelection.Spell2Id);
        }
    }
}
