using System.Collections.Generic;
using System.Threading.Tasks;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.SummonerV4;
using Camille.RiotGames.LeagueV4;
using Camille.RiotGames.SpectatorV5;

namespace SkillzBot.Interfaces
{
    public interface IRiotApiService
    {
        Task<bool> InitializeAsync();
        Task<CurrentGameInfo> GetCurrentGameAsync();
        Task<List<string>> GetRankBySummonerAsync();
        Task<Match> GetMatchAsync(string matchID);
        Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string SummonerName = null, string sRegion = null);
        Camille.RiotGames.MatchV5.Participant GetParticipantByMatch(Match match);
        Task<string> UpdateSummonerByNameAsync(string gameName, string tagLine, string inRegion);
        Task<Summoner> GetSummonerByNameAsync(string tagLine, string inRegion);
        Task<Camille.RiotGames.MatchV5.Participant> GetLastMatchParticipantAsync();
        void UpdateConfig();
    }
}