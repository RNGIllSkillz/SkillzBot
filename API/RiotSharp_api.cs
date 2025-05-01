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

namespace SkillzBot.API.Riot_Deprecated
{
    internal class RiotAPIOld
    {
        private static readonly RiotApi riotApi;
        private static string lastErrorMessage = null;
        private static readonly bool IsValidToken = StringUtil.IsValidApiToken(IllSingleton.GetInstance().RiotApiToken);
        private static Summoner summoner;
        private static Exception tempEx = null;
        private static Region region;
        static RiotAPIOld()
        {
            if (!IsValidToken) 
            {
                Console.WriteLine("No valid RiotAPI token. RiotAPI functionality is offline");
                return; 
            }
            Console.Write("Initializing Riot API... ");
            region = IllSingleton.GetInstance().SummonerRegion switch
            {
                "ru" => Region.Ru,
                "euw" => Region.Euw,
                "na" => Region.Na,
                _ => Region.Euw,
            };
            riotApi = RiotApi.GetInstance(IllSingleton.GetInstance().RiotApiToken, 200, 500);
            for (int i = 0; i <= 5; i++)
            {
                summoner = InitAsync().GetAwaiter().GetResult();
                if (summoner != null) break;
                Thread.Sleep(2000);
            }     
            if (summoner == null)
            {
                IsValidToken = false;
                Console.WriteLine("Error at initializing RiotAPI class. RiotAPI functionality is offline.");
            }
            else
                Console.WriteLine("OK.");
        }
        private static async Task<Summoner> InitAsync()
        {           
            try
            {
                return await riotApi.Summoner.GetSummonerByNameAsync(region, IllSingleton.GetInstance().SUMMONER_NAME).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (tempEx == null || tempEx != ex)
                {
                    Log.WriteLog(ex, "RiotApi InitAsync");
                    tempEx = ex;
                }
                return null;
            }
        }
        public static async Task<CurrentGame> GetCurrentGameAsync()
        {
            if (!IsValidToken) return null;
            try
            {
                var currentGame = await riotApi.Spectator.GetCurrentGameAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
                lastErrorMessage = null;
                return currentGame;
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
            return null;
        }
        public static async Task<List<string>> GetRankBySummonerAsync()
        {
            if (!IsValidToken) return null;
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
            if (!IsValidToken) return null;
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
        public static async Task<List<LeagueEntry>> GetLeagueEntriesBySummonerAsync(string SummonerName = null, string sRegion = null)
        {
            if (!IsValidToken) return null;
            try
            {
                if (SummonerName == null || sRegion == null)
                    return await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
                else
                {
                    var tRegion = sRegion switch
                    {
                        "ru" => Region.Ru,
                        "euw" => Region.Euw,
                        "na" => Region.Na,
                        _ => Region.Euw,
                    };
                    var tSummoner = await riotApi.Summoner.GetSummonerByNameAsync(tRegion, SummonerName).ConfigureAwait(false);
                    return await riotApi.League.GetLeagueEntriesBySummonerAsync(summoner.Region, summoner.Id).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetLeagueEntriesBySummonerAsync");
                return null;
            }
        }
        public static async Task<ChampionStatic> GetChampByIdAsync(int ChampionId)
        {
            if (!IsValidToken) return null;
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
            foreach (var Participant in match.Info.Participants)
            {
                if (string.Equals(StringUtil.RemoveWhitespace(Participant.SummonerName), IllSingleton.GetInstance().SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))                
                    return Participant;                
            }
            return null;
        }
        public static async Task<List<string>> GetMatchListAsync()
        {
            if (!IsValidToken) return null;
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
        public static async Task<string> UpdateSummonerByNameAsync(string summonerName, string inRegion)
        {
            if (!IsValidToken) return null;
            try
            {
                var newRegion = inRegion switch
                {
                    "ru" => Region.Ru,
                    "euw" => Region.Euw,
                    "na" => Region.Na,
                    _ => Region.Euw,
                };

                summoner = await riotApi.Summoner.GetSummonerByNameAsync(newRegion, summonerName).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex.InnerException, "UpdateSummonerByNameAsync");
                return ex.Message;
            }
        }
        public static async Task<Summoner> GetSummonerByNameAsync(string summonerName)
        {
            if (!IsValidToken) return null;
            try
            {
                return await riotApi.Summoner.GetSummonerByNameAsync(region, summonerName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetSummonerByNameAsync()");
                return null;
            }
        }
        public static async Task<List<LeagueEntry>> GetLeagueEntriesBySummonerAsync(string summonerId)
        {
            if (!IsValidToken) return null;
            try
            {
                return await riotApi.League.GetLeagueEntriesBySummonerAsync(region, summonerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetLeagueEntriesBySummonerAsync()");
                return null;
            }
        }
        public static void UpdateRegion(string newRegion)
        {
            region = newRegion switch
            {
                "ru" => Region.Ru,
                "euw" => Region.Euw,
                "na" => Region.Na,
                _ => Region.Euw,
            };
        }
    }
}
