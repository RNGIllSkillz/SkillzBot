using RiotSharp.Endpoints.SummonerEndpoint;
using RiotSharp;
using System;
using System.Collections.Generic;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using System.Threading.Tasks;
using System.Threading;
using RiotSharp.Endpoints.SpectatorEndpoint;
using RiotSharp.Misc;
using RiotSharp.Endpoints.MatchEndpoint;
using RiotSharp.Endpoints.LeagueEndpoint;
using RiotSharp.Endpoints.StaticDataEndpoint.Champion;
using SkillzBot.Utils;
using System.Globalization;
using SkillzBot.MODELS;

namespace SkillzBot.API.Riot
{
    internal class RiotAPI
    {
        private static RiotApi riotApi;
        private static string lastErrorMessage = null;
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
            CurrentGame currentGame = null;
            try
            {
                currentGame = await riotApi.Spectator.GetCurrentGameAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
                lastErrorMessage = null;
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    if (lastErrorMessage != ex.Message)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                            lastErrorMessage = ex.Message;
                        }
                    }
                }
                else
                {
                    if (lastErrorMessage != ex.Message)
                    {
                        if (!ex.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                            lastErrorMessage = ex.Message;
                        }
                    }
                }
            }
            return currentGame;
        }
        public static async Task<List<string>> GetRankBySummonerAsync()
        {
            List<LeagueEntry> rank;
            try
            {
                rank = await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetRankBySummonerAsync");
                return null;
            }
            foreach (var mType in rank)
            {
                if (mType.QueueType == "RANKED_SOLO_5x5")
                {
                    return new List<string>
                    {
                        mType.Rank,
                        Convert.ToString(mType.LeaguePoints),
                        mType.Tier
                    };
                }
            }
            return null;
        }
        public static async Task<Match> GetMatchAsync(string matchID)
        {
            try
            {
                return await riotApi.Match.GetMatchAsync(Region.Europe, matchID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    if (ex.InnerException.Message.Contains("Data not found"))
                    {
                        //expected. we are still in the game.
                        return null;
                    }
                    else
                    {
                        throw ex;
                    }
                }
                else
                {
                    if (ex.Message.Contains("Data not found"))
                    {
                        //expected. we are still in the game.
                        return null;
                    }
                    else
                    {
                        throw ex;
                    }
                }
            }
        }
        public static async Task<List<LeagueEntry>> GetLeagueEntriesBySummonerAsync()
        {
            try
            {
                return await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetLeagueEntriesBySummonerAsync");
                return null;
            }
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
            try
            {
                return await riotApi.DataDragon.Champions.GetByIdAsync(ChampionId, "13.6.1", lang).ConfigureAwait(false);
            }
            catch (Exception ex) 
            { 
                Log.WriteLog(ex, "GetChampByIdAsync"); 
                return null;
            }
        }
        public static RiotSharp.Endpoints.MatchEndpoint.Participant GetParticipantByMatch(Match match)
        {
            var Participants = match.Info.Participants.ToArray();
            foreach (var Participant in Participants)
            {
                if (string.Equals(StringUtil.RemoveWhitespace(Participant.SummonerName), IllSingleton.GetInstance().SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))                
                    return Participant;                
            }
            return null;
        }
        public static async Task<List<string>> GetMatchListAsync()
        {
            try
            {
                return await riotApi.Match.GetMatchListAsync(Region.Europe, summoner.Puuid, 0, 1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetMatchListAsync");
                return null;
            }
        }  
        public static async Task UpdateSummonerByNameAsync(string summonerName)
        {
            try
            {
                summoner = await riotApi.Summoner.GetSummonerByNameAsync(Region.Euw, summonerName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "UpdateSummonerByNameAsync");
            }
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
