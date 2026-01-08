using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using SkillzBot.Utils;
using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.SummonerV4;
using Camille.RiotGames.SpectatorV5;
using Camille.RiotGames.LeagueV4;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.AccountV1;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Singleton;

namespace SkillzBot.API.RiotGames
{
    public class RiotApiService : IRiotApiService
    {
        private RiotGamesApi _riotApi;
        private readonly ILogger<RiotApiService> _logger;
        private PlatformRoute _platformRoute;
        private Summoner _summoner;
        private string _gameName;
        private string _tagLine;
        private bool _isValidToken;
        private static string lastErrorMessage = null;
        private Account account;

        public RiotApiService(ILogger<RiotApiService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> InitializeAsync()
        {
            _isValidToken = StringUtil.IsValidApiToken(IllSingleton.Config.RiotApiToken);
            if (!_isValidToken)
            {
                _logger.LogWarning("No valid _riotApi token. Functionality offline.");
                return false;
            }

            _platformRoute = IllSingleton.Game.SummonerRegion switch
            {
                "ru" => PlatformRoute.RU,
                _ => PlatformRoute.EUW1,
            };

            var name = IllSingleton.Game.SummonerName.Split('#');
            if (name.Length < 2)
            {
                _logger.LogError("Invalid Summoner Name format (Name#Tag)");
                return false;
            }

            _gameName = name[0];
            _tagLine = name[1];

            _riotApi = RiotGamesApi.NewInstance(
                new RiotGamesApiConfig.Builder(IllSingleton.Config.RiotApiToken)
                {
                    MaxConcurrentRequests = 200,
                    Retries = 3, // Reduced from 10 to prevent long hangs
                }.Build()
            );

            // Async initialization
            _summoner = await GetSummonerInternalAsync();

            if (_summoner == null)
            {
                _isValidToken = false;
                _logger.LogError("Failed to fetch initial Summoner data.");
                return false;
            }

            _logger.LogInformation("Riot API Initialized successfully.");
            return true;
        }

        private async Task<Summoner> GetSummonerInternalAsync()
        {
            try
            {
                var account = await _riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, _gameName, _tagLine);
                if (account == null) return null;
                return await _riotApi.SummonerV4().GetByPUUIDAsync(_platformRoute, account.Puuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "_riotApi Init Failed");
                return null;
            }
        }
        public async Task<CurrentGameInfo> GetCurrentGameAsync()
        {
            if (!_isValidToken) return null;
            try
            {
                var currentGame = await _riotApi.SpectatorV5().GetCurrentGameInfoByPuuidAsync(_platformRoute, _summoner.Puuid).ConfigureAwait(false);
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
                            _logger.LogError(ex, "GetCurrentMatchTask_1");
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
                            _logger.LogError(ex, "GetCurrentMatchTask_2");
                            lastErrorMessage = ex.Message;
                        }
                    }
                }
            }
            return null;
        }
        
        public async Task<List<string>> GetRankBySummonerAsync()
        {
            if (!_isValidToken) return null;
            LeagueEntry[] rank;
            try
            {
                rank = await HttpHandler.GetLeagueEntriesByPUUIDAsync(_platformRoute, _summoner.Puuid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRankBySummonerAsync");
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
        /* public static async Task<List<string>> GetRankBySummonerAsync()
         {
             if (!_isValidToken) return null;
             LeagueEntry[] rank; //= null
             try
             {
                 //rank = await _riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRout, summoner.Id).ConfigureAwait(false);
                 rank = await GetLeagueEntriesByPUUIDAsync("euw1", summoner.Puuid, singleton._riotApiToken).ConfigureAwait(false);
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "GetRankBySummonerAsync");
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
         }*/
        public async Task<Match> GetMatchAsync(string matchID)
        {
            if (!_isValidToken) return null;
            try
            {
                return await _riotApi.MatchV5().GetMatchAsync(RegionalRoute.EUROPE, matchID).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string message = ex.InnerException?.Message ?? ex.Message;
                if (message.Contains("data not found", StringComparison.OrdinalIgnoreCase))
                {
                    // Expected during live game – match data not yet available
                    return null;
                }
                throw;
            }
        }
        /*
        public static async Task<Match> GetMatchAsync(string matchID)
        {
            if (!_isValidToken) return null;
            try
            {
                return await _riotApi.MatchV5().GetMatchAsync(RegionalRoute.EUROPE,matchID).ConfigureAwait(false);
                //return await _riotApi.Match.GetMatchAsync(Region.Europe, matchID).ConfigureAwait(false);
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
        }*/
        public async Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string SummonerName = null, string sRegion = null)
        {
            if (!_isValidToken) return null;
            try
            {
                if (SummonerName == null || sRegion == null)                
                    return await HttpHandler.GetLeagueEntriesByPUUIDAsync(_platformRoute, _summoner.Puuid).ConfigureAwait(false); 
                else
                {
                    var tPlatform = sRegion switch
                    {
                        //"ru" => Region.Ru,
                        //"euw" => Region.Euw,
                        //"na" => Region.Na,
                        _ => PlatformRoute.EUW1,
                    };
                    return await HttpHandler.GetLeagueEntriesByPUUIDAsync(_platformRoute, _summoner.Puuid).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLeagueEntriesBySummonerAsync");
                return null;
            }
        }
        /*
        public static async Task<ChampionStatic> GetChampByIdAsync(int ChampionId)
        {
            if (!_isValidToken) return null;
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
                var test = await _riotApi.
                //return await _riotApi.DataDragon.Champions.GetByIdAsync(ChampionId, "13.6.1", lang).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChampByIdAsync");
                return null;
            }
        } */
        public Camille.RiotGames.MatchV5.Participant GetParticipantByMatch(Match match)
        {
            foreach (var Participant in match.Info.Participants)
            {
                Console.WriteLine($"Participant.RiotIdGameName = {Participant.RiotIdGameName} Participant.RiotIdTagline = {Participant.RiotIdTagline}");
                if (string.Equals(StringUtil.RemoveWhitespace(Participant.RiotIdGameName + "#" + Participant.RiotIdTagline), IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
                    return Participant;
            }
            return null;
        }
        /*
        public static async Task<List<string>> GetMatchListAsync()
        {
            if (!_isValidToken) return null;
            try
            {
                return await _riotApi.Match.GetMatchListAsync(Region.Europe, summoner.Puuid, 0, 1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMatchListAsync");
                return null;
            }
        }*/
        public async Task<string> UpdateSummonerByNameAsync(string gameName, string tagLine, string inRegion)
        {
            if (!_isValidToken) return null;
            try
            {
                var newRegion = inRegion switch
                {
                    //"ru" => Region.Ru,
                    //"euw" => Region.Euw,
                    //"na" => Region.Na,
                    _ => PlatformRoute.EUW1,
                };
                account = await _riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, gameName, tagLine).ConfigureAwait(false);
                if (account == null)
                    throw new InvalidOperationException("Account not found for the given gameName and tagLine.");
                _summoner = await _riotApi.SummonerV4().GetByPUUIDAsync(newRegion, account.Puuid).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.InnerException ?? ex, "UpdateSummonerByNameAsync");
                return ex.Message;
            }
        }
        public async Task<Summoner> GetSummonerByNameAsync(string tagLine, string inRegion)
        {
            if (!_isValidToken) return null;
            try
            {
                return await _riotApi.SummonerV4().GetByPUUIDAsync(_platformRoute, account.Puuid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSummonerByNameAsync()");
                return null;
            }
        }
        public async Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string summonerId)
        {
            if (!_isValidToken) return null;
            try
            {
                return await _riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(_platformRoute, summonerId).ConfigureAwait(false);
                //return await _riotApi.League.GetLeagueEntriesBySummonerAsync(region, summonerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLeagueEntriesBySummonerAsync()");
                return null;
            }
        }
        public void UpdateConfig()
        {
            _platformRoute = IllSingleton.Game.SummonerRegion switch
            {
                //"ru" => Region.Ru,
                //"euw" => Region.Euw,
                //"na" => Region.Na,
                _ => PlatformRoute.EUW1,
            };
            var name = IllSingleton.Game.SummonerName.Split('#');
            _gameName = name[0];
            _tagLine = name[1];
        }
    }
}

