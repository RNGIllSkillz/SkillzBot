using RiotSharp.Endpoints.SummonerEndpoint;
using RiotSharp;
using System;
using System.Collections.Generic;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using System.Threading.Tasks;
using System.Threading;
using SkillzBot.Readers;
using RiotSharp.Endpoints.SpectatorEndpoint;
using RiotSharp.Misc;
using RiotSharp.Endpoints.MatchEndpoint;
using RiotSharp.Endpoints.LeagueEndpoint;
using RiotSharp.Endpoints.StaticDataEndpoint.Champion;
using SkillzBot.Utils;
using System.Globalization;
using SkillzBot.JSON.MediaHistory;
using Google.Protobuf.WellKnownTypes;

namespace SkillzBot.API.Riot
{
    internal class RiotAPI
    {
        private static RiotApi riotApi;
        static Summoner summoner;
        static RiotAPI()
        {
            while (true)
            {
                summoner = InitAsync().GetAwaiter().GetResult();
                if (summoner != null) break;
                Thread.Sleep(2000);
            }         
        }
        private static async Task<Summoner> InitAsync()
        {
            riotApi = RiotApi.GetInstance(IllSingleton.GetInstance().RiotApiToken, 200, 500);
            try
            {
                return await riotApi.Summoner.GetSummonerByNameAsync(Region.Euw, IllSingleton.GetInstance().SUMMONER_NAME).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "RiotApi InitAsync");
                return null;
            }
        }


        public static async Task<CurrentGame> GetCurrentGameAsync()
        {
            return await riotApi.Spectator.GetCurrentGameAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
        }
        public static async Task<List<string>> GetRankBySummonerAsync()
        {
            List<string> output = new List<string>();
            var rank = await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
            foreach (var mType in rank)
            {
                if (mType.QueueType == "RANKED_SOLO_5x5")
                {
                    output.Add(mType.Rank);
                    output.Add(Convert.ToString(mType.LeaguePoints));
                    output.Add(mType.Tier);
                    return output;
                }
            }
            return null;
        }
        public static async Task<Match> GetMatchAsync(string matchID)
        { 
            return await riotApi.Match.GetMatchAsync(Region.Europe, matchID).ConfigureAwait(false);
        }
        public static async Task<List<LeagueEntry>> GetLeagueEntriesBySummonerAsync()
        {
            return await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
        }
        public static async Task<ChampionStatic> GetChampByIdAsync(int ChampionId)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            var lang = culture.TwoLetterISOLanguageName.ToLower() switch
            {
                "ru" => Language.ru_RU,
                "en" => Language.en_US,
                "fr" => Language.fr_FR,
                "jp" => Language.ja_JP,
                "ko" => Language.ko_KR,
                _ => Language.en_US,
            };
            return await riotApi.DataDragon.Champions.GetByIdAsync(ChampionId, "12.13.1", lang).ConfigureAwait(false);
        }
        public static RiotSharp.Endpoints.MatchEndpoint.Participant GetParticipantByMatch(Match match)
        {
            try
            {
                var Participants = match.Info.Participants.ToArray();
                foreach (var Participant in Participants)
                {
                    if (StringUtil.RemoveWhitespace(Participant.SummonerName.ToLower()) == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
                    {
                        return Participant;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "null");
                return null;
            }
        }
        public static async Task<List<string>> GetMatchListAsync()
        {
           return await riotApi.Match.GetMatchListAsync(Region.Europe, summoner.Puuid, 0, 1).ConfigureAwait(false);
        }  
        public static async Task UpdateSummonerByNameAsync(string summonerName)
        {
            summoner = await riotApi.Summoner.GetSummonerByNameAsync(Region.Euw, summonerName).ConfigureAwait(false);
        }
        public static async Task<Summoner> GetSummonerByNameAsync(string summonerName)
        {
            return await riotApi.Summoner.GetSummonerByNameAsync(Region.Euw, summonerName).ConfigureAwait(false);
        }
        public static async Task<List<LeagueEntry>> GetLeagueEntriesBySummonerAsync(string summonerId)
        {
            return await riotApi.League.GetLeagueEntriesBySummonerAsync(Region.Euw, summonerId).ConfigureAwait(false);
        }
    }
}
