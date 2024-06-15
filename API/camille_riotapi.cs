using System;
using System.Collections.Generic;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using System.Threading.Tasks;
using System.Threading;
using SkillzBot.Utils;
using System.Globalization;
using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.SummonerV4;
using Camille.RiotGames.SpectatorV5;
using Camille.RiotGames.LeagueV4;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.AccountV1;
using System.Diagnostics;

namespace SkillzBot.API.Riot
{
    internal class RiotAPI
    {
        //private static readonly RiotApi riotApi;
        private static readonly RiotGamesApi riotApi;
        private static string lastErrorMessage = null;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        private static readonly bool IsValidToken = StringUtil.IsValidApiToken(singleton.RiotApiToken);
        private static Summoner summoner;
        private static Exception tempEx = null;
        private static PlatformRoute platformRout;
        private static string gameName;
        private static string tagLine;
        private static Account account;
        
        static RiotAPI()
        {
            if (!IsValidToken)
            {
                Console.WriteLine("No valid RiotAPI token. RiotAPI functionality is offline");
                return;
            }
            Console.Write("Initializing Camille... ");
            platformRout = singleton.SummonerRegion switch
            {
                "ru" => PlatformRoute.RU,
                //"euw" => Region.Euw,
                //"na" => Region.Na,
                _ => PlatformRoute.EUW1,
            };
            var name = singleton.SUMMONER_NAME.Split('#');
            gameName = name[0];
            tagLine = name[1];
            riotApi = RiotGamesApi.NewInstance(
                new RiotGamesApiConfig.Builder(singleton.RiotApiToken)
                {
                    MaxConcurrentRequests = 200,
                    Retries = 10,
                }.Build()
            );
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
                account = riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, gameName, tagLine).Result;
                return await riotApi.SummonerV4().GetByPUUIDAsync(platformRout, account.Puuid);                
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
        public static async Task<CurrentGameInfo> GetCurrentGameAsync()
        {
            if (!IsValidToken) return null;
            try
            {
                var currentGame = await riotApi.SpectatorV5().GetCurrentGameInfoByPuuidAsync(platformRout, summoner.Puuid).ConfigureAwait(false);
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
            LeagueEntry[] rank;
            try
            {
                rank = await riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRout, summoner.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetRankBySummonerAsync");
                return null;
            }
            foreach (var mType in rank)
            {
                if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
                {
                    return new List<string>
                    {
                        Convert.ToString(mType.Rank),
                        Convert.ToString(mType.LeaguePoints),
                        Convert.ToString(mType.Tier)
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
                return await riotApi.MatchV5().GetMatchAsync(RegionalRoute.EUROPE,matchID).ConfigureAwait(false);
                //return await riotApi.Match.GetMatchAsync(Region.Europe, matchID).ConfigureAwait(false);
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
        public static async Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string SummonerName = null, string sRegion = null)
        {
            if (!IsValidToken) return null;
            try
            {
                if (SummonerName == null || sRegion == null)
                    return await riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRout, summoner.Id).ConfigureAwait(false);
                else
                {
                    var tPlatform = sRegion switch
                    {
                        //"ru" => Region.Ru,
                        //"euw" => Region.Euw,
                        //"na" => Region.Na,
                        _ => PlatformRoute.EUW1,
                    };
                    return await riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRout, summoner.Id).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetLeagueEntriesBySummonerAsync");
                return null;
            }
        }
        /*
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
                var test = await riotApi.
                //return await riotApi.DataDragon.Champions.GetByIdAsync(ChampionId, "13.6.1", lang).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetChampByIdAsync");
                return null;
            }
        } */
        public static Camille.RiotGames.MatchV5.Participant GetParticipantByMatch(Match match)
        {
            foreach (var Participant in match.Info.Participants)
            {
                Console.WriteLine($"Participant.RiotIdGameName = {Participant.RiotIdGameName} Participant.RiotIdTagline = {Participant.RiotIdTagline}");
                if (string.Equals(StringUtil.RemoveWhitespace(Participant.RiotIdGameName+"#"+Participant.RiotIdTagline), IllSingleton.GetInstance().SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                    return Participant;
            }
            return null;
        }
        /*
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
        }*/
        public static async Task<string> UpdateSummonerByNameAsync(string gameName, string tagLine, string inRegion)
        {
            if (!IsValidToken) return null;
            try
            {
                var newRegion = inRegion switch
                {
                    //"ru" => Region.Ru,
                    //"euw" => Region.Euw,
                    //"na" => Region.Na,
                    _ => PlatformRoute.EUW1,
                };
                account = await riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, gameName, tagLine).ConfigureAwait(false);
                summoner = await riotApi.SummonerV4().GetByPUUIDAsync(platformRout, account.Puuid).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex.InnerException, "UpdateSummonerByNameAsync");
                return ex.Message;
            }
        }
        public static async Task<Summoner> GetSummonerByNameAsync(string tagLine, string inRegion)
        {
            if (!IsValidToken) return null;
            try
            {
                return await riotApi.SummonerV4().GetByPUUIDAsync(platformRout, account.Puuid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetSummonerByNameAsync()");
                return null;
            }
        }
        public static async Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string summonerId)
        {
            if (!IsValidToken) return null;
            try
            {
                return await riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRout, summonerId).ConfigureAwait(false);
                //return await riotApi.League.GetLeagueEntriesBySummonerAsync(region, summonerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetLeagueEntriesBySummonerAsync()");
                return null;
            }
        }
        public static void UpdateConfig()
        {
            platformRout = singleton.SummonerRegion switch
            {
                //"ru" => Region.Ru,
                //"euw" => Region.Euw,
                //"na" => Region.Na,
                _ => PlatformRoute.EUW1,
            };
            var name = singleton.SUMMONER_NAME.Split('#');
            gameName = name[0];
            tagLine = name[1];
        }
    }
}
