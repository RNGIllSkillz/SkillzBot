using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.Utils;
using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.SummonerV4;
using Camille.RiotGames.SpectatorV5;
using Camille.RiotGames.LeagueV4;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.AccountV1;
using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration; 
using System.Linq;

namespace SkillzBot.API.RiotGames
{
    public class RiotApiService : IRiotApiService
    {
        private readonly BotConfigModel _config; 
        private readonly IGameStateService _gameState; 
        private RiotGamesApi _riotApi;
        private readonly ILogger<RiotApiService> _logger;
        private PlatformRoute _platformRoute;
        private Summoner _summoner;
        private string _gameName;
        private string _tagLine;
        private bool _isValidToken;
        private static string lastErrorMessage = null;
        private Account account;
        private string _normalizedSummonerName;
        private readonly RiotHttpHandler _httpHandler;

        public RiotApiService(
        BotConfigModel config,
        IGameStateService gameState,
        ILogger<RiotApiService> logger,
        RiotHttpHandler httpHandler)
        {
            _config = config;
            _gameState = gameState;
            _logger = logger;
            _httpHandler = httpHandler;
        }

        public async Task<bool> InitializeAsync()
        {
            _isValidToken = StringUtil.IsValidApiToken(_config.RiotApiToken);
            if (!_isValidToken)
            {
                _logger.LogWarning("No valid Riot API token. Functionality offline.");
                return false;
            }

            UpdateConfig();

            _riotApi = RiotGamesApi.NewInstance(
                new RiotGamesApiConfig.Builder(_config.RiotApiToken)
                {
                    MaxConcurrentRequests = 200,
                    Retries = 3,
                }.Build()
            );
            _summoner = await GetSummonerInternalAsync();

            if (_summoner == null)
            {
                _logger.LogWarning("Riot API Warning: Initial Summoner fetch failed for {GameName}#{TagLine}. Service will attempt to reconnect on next command usage.", _gameName, _tagLine);
                return true;
            }

            _logger.LogInformation("Riot API Initialized successfully for {GameName}#{TagLine}.", _gameName, _tagLine);
            return true;
        }

        private async Task<bool> EnsureServiceReadyAsync()
        {
            if (_riotApi == null)
            {
                await InitializeAsync();
            }
            if (_summoner != null) return true;
            if (!_isValidToken) return false;

            // Attempt to re-fetch if previously failed
            _summoner = await GetSummonerInternalAsync();

            if (_summoner != null)
            {
                _logger.LogInformation("Riot API recovered successfully.");
                return true;
            }

            return false;
        }

        private async Task<Summoner> GetSummonerInternalAsync()
        {
            try
            {
                var account = await _riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, _gameName, _tagLine);
                if (account == null)
                {
                    _logger.LogWarning("Riot Account lookup returned null for {GameName}#{TagLine}", _gameName, _tagLine);
                    return null;
                }
                return await _riotApi.SummonerV4().GetByPUUIDAsync(_platformRoute, account.Puuid);
            }
            catch (Exception ex)
            {
                // Log as warning during runtime retry, Error during explicit init
                _logger.LogWarning("Riot API request failed: {Message}", ex.Message);
                return null;
            }
        }

        public async Task<CurrentGameInfo> GetCurrentGameAsync()
        {
            if (!await EnsureServiceReadyAsync()) return null;

            try
            {
                var currentGame = await _riotApi.SpectatorV5().GetCurrentGameInfoByPuuidAsync(_platformRoute, _summoner.Puuid);
                lastErrorMessage = null;
                return currentGame;
            }
            catch (Exception ex)
            {
                string currentErrorMessage = ex.InnerException?.Message ?? ex.Message;
                if (!currentErrorMessage.Contains("Data not found", StringComparison.OrdinalIgnoreCase))
                {
                    if (lastErrorMessage != currentErrorMessage)
                    {
                        _logger.LogError(ex, "GetCurrentMatchTask failed");
                        lastErrorMessage = currentErrorMessage;
                    }
                }
                else
                {
                    lastErrorMessage = null;
                }
            }
            return null;
        }

        public async Task<List<string>> GetRankBySummonerAsync()
        {
            if (!await EnsureServiceReadyAsync())
            {
                _logger.LogWarning("GetRankBySummonerAsync: Service not ready.");
                return null;
            }

            try
            {
                var rank = await _httpHandler.GetLeagueEntriesByPUUIDAsync(_platformRoute, _summoner.Puuid);
                var soloQueueRank = rank?.FirstOrDefault(mType => mType.QueueType == QueueType.RANKED_SOLO_5x5);

                if (soloQueueRank != null)
                {
                    return new List<string>
                    {
                        Convert.ToString(soloQueueRank.Rank),
                        Convert.ToString(soloQueueRank.LeaguePoints),
                        Convert.ToString(soloQueueRank.Tier)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRankBySummonerAsync");
            }
            return null;
        }

        public async Task<Match> GetMatchAsync(string matchID)
        {
            if (!_isValidToken) return null; 
            try
            {
                return await _riotApi.MatchV5().GetMatchAsync(RegionalRoute.EUROPE, matchID);
            }
            catch (Exception ex)
            {
                string message = ex.InnerException?.Message ?? ex.Message;
                if (message.Contains("data not found", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                _logger.LogError(ex, "GetMatchAsync failed for match ID {MatchID}", matchID);
                throw;
            }
        }

        public async Task<LeagueEntry[]> GetLeagueEntriesBySummonerAsync(string SummonerName = null, string sRegion = null)
        {
            if (SummonerName == null && !await EnsureServiceReadyAsync()) return null;

            try
            {
                if (SummonerName == null || sRegion == null)
                    return await _httpHandler.GetLeagueEntriesByPUUIDAsync(_platformRoute, _summoner.Puuid);
                else
                {
                    // Ensure API is ready even for external lookups
                    if (_riotApi == null) await InitializeAsync();
                    if (_riotApi == null) return null;

                    var nameParts = SummonerName.Split('#');
                    if (nameParts.Length < 2)
                    {
                        _logger.LogWarning("GetLeagueEntriesBySummonerAsync called with invalid SummonerName format: {SummonerName}", SummonerName);
                        return null;
                    }
                    var tempAccount = await _riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, nameParts[0], nameParts[1]);
                    if (tempAccount == null) return null;

                    var tempPlatform = sRegion.ToLowerInvariant() switch
                    {
                        "ru" => PlatformRoute.RU,
                        "na" => PlatformRoute.NA1,
                        _ => PlatformRoute.EUW1,
                    };
                    return await _httpHandler.GetLeagueEntriesByPUUIDAsync(tempPlatform, tempAccount.Puuid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLeagueEntriesBySummonerAsync");
                return null;
            }
        }

        public Camille.RiotGames.MatchV5.Participant GetParticipantByMatch(Match match)
        {
            foreach (var participant in match.Info.Participants)
            {
                string participantFullName = StringUtil.RemoveWhitespace($"{participant.RiotIdGameName}#{participant.RiotIdTagline}");
                if (string.Equals(participantFullName, _normalizedSummonerName, StringComparison.OrdinalIgnoreCase))
                {
                    return participant;
                }
            }
            _logger.LogWarning("Could not find participant {SummonerName} in match {MatchId}", _gameState.Current.SummonerName, match.Metadata.MatchId);
            return null;
        }

        public async Task<string> UpdateSummonerByNameAsync(string gameName, string tagLine, string inRegion)
        {
            if (!_isValidToken) return "Riot API Token is not valid.";
            try
            {
                var newRegion = inRegion.ToLowerInvariant() switch
                {
                    "ru" => PlatformRoute.RU,
                    "na" => PlatformRoute.NA1,
                    _ => PlatformRoute.EUW1,
                };
                account = await _riotApi.AccountV1().GetByRiotIdAsync(RegionalRoute.EUROPE, gameName, tagLine);
                if (account == null)
                    throw new InvalidOperationException("Account not found for the given gameName and tagLine.");
                _summoner = await _riotApi.SummonerV4().GetByPUUIDAsync(newRegion, account.Puuid);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.InnerException ?? ex, "UpdateSummonerByNameAsync failed for {GameName}#{TagLine}", gameName, tagLine);
                return ex.Message;
            }
        }

        public async Task<Summoner> GetSummonerByNameAsync(string tagLine, string inRegion)
        {
            if (!_isValidToken) return null;
            try
            {
                if (account == null) return null;
                return await _riotApi.SummonerV4().GetByPUUIDAsync(_platformRoute, account.Puuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSummonerByNameAsync()");
                return null;
            }
        }

        public void UpdateConfig()
        {
            _platformRoute = _gameState.Current.SummonerRegion switch
            {
                "ru" => PlatformRoute.RU,
                "na" => PlatformRoute.NA1,
                _ => PlatformRoute.EUW1,
            };
            var name = _gameState.Current.SummonerName.Split('#');
            if (name.Length < 2)
            {
                _logger.LogError("Invalid Summoner Name format in config (Name#Tag): {SummonerName}", _gameState.Current.SummonerName);
                _isValidToken = false;
                return;
            }
            _gameName = name[0];
            _tagLine = name[1];
            _normalizedSummonerName = StringUtil.RemoveWhitespace(_gameState.Current.SummonerName);
        }
    }
}