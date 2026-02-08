using Camille.Enums;
using Camille.RiotGames.LeagueV4;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Infrastructure;
using SkillzBot.Services.Writers;
using SkillzBot.IllConfiguration;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.Utils;
using SkillzBot.Writers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
    public class IllCommands
    {
        private readonly ITtvIRCClient _ircClient;
        //private readonly IllModeratorsInteractions _modInteractions;
        private readonly IllChatFilters _chatFilters;
        private readonly RewardsRedemption _rewardsRedemption;
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<IllCommands> _logger;
        private readonly IRiotApiService _riotApi;
        private readonly IllGames _illGames;
        private readonly QuartzBackgroundTaskManager _quartzManager;
        private readonly LoggingLevelSwitch _loggingSwitch;
        private readonly ITwitchService _twitchService;
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly IPathProvider _pathProvider;
        private readonly IStreamElementsService _streamElementsService;
        private readonly IIllAccess _illAccess;
        private readonly ConfigWriterService _configWriter;
        private readonly MediaQueueService _mediaQueueService;
        private readonly BlacklistService _blacklistService;
        private readonly SubscriptionService _subscriptionService;
        private readonly IMmrService _mmrService;

        private string _ludka = "";

        public IllCommands(
            ITtvIRCClient ircClient,
            IllModeratorsInteractions modInteractions,
            IllChatFilters chatFilters,
            RewardsRedemption rewardsRedemption,
            IDatabaseService databaseService,
            ILogger<IllCommands> logger,
            IRiotApiService riotApi,
            IllGames illGames,
            QuartzBackgroundTaskManager quartzManager,
            LoggingLevelSwitch loggingSwitch,
            ITwitchService twitchService,
            BotConfigModel config,
            IGameStateService gameState,
            IBotStateService botState,
            IPathProvider pathProvider,
            IStreamElementsService streamElementsService,
            IIllAccess illAccess, 
            ConfigWriterService configWriter,
            MediaQueueService mediaQueueService,
            BlacklistService blacklistService,
            SubscriptionService subscriptionService,
            IMmrService mmrService)
        {
            _ircClient = ircClient;
            //_modInteractions = modInteractions;
            _chatFilters = chatFilters;
            _rewardsRedemption = rewardsRedemption;
            _databaseService = databaseService;
            _logger = logger;
            _riotApi = riotApi;
            _illGames = illGames;
            _quartzManager = quartzManager;
            _loggingSwitch = loggingSwitch;
            _twitchService = twitchService;
            _config = config;
            _gameState = gameState;
            _botState = botState;
            _pathProvider = pathProvider;
            _streamElementsService = streamElementsService;
            _illAccess = illAccess;
            _subscriptionService = subscriptionService;
            _mediaQueueService = mediaQueueService;
            _configWriter = configWriter;
            _blacklistService = blacklistService;
            _mmrService = mmrService;
        }

        public async Task Help(UserObject user)
        {
            await _ircClient.SendMessage(string.Format(STRINGS.HelpMessage, user.Name));
        }

        public async Task Points(UserObject user)
        {
            var pos = await _databaseService.GetUserPositionAsync(user.Name, "Points");
            var QPos = await _databaseService.GetUserPositionAsync(user.Name, "QuizPoints");
            var QtPos = await _databaseService.GetUserPositionAsync(user.Name, "QuizTotal");
            await _ircClient.SendMessage(string.Format(STRINGS.PointsMessage, user.Name, user.Points, pos[0], pos[1], user.QuizPoints, QPos[0], QPos[1], user.QuizTotal, QtPos[0], QtPos[1]));
        }

        public async Task Prediction(UserObject user, string[] command)
        {
            if (command.Length > 1)
            {
                switch (command[1])
                {
                    case "off":
                        await _botState.UpdateStateAsync(s => s.AutoPred = false);
                        await _ircClient.SendMessage($"@{user.Name} Автоставки Выключены!");
                        _logger.LogInformation("{Name} Выключил ставки!", user.Name);
                        break;

                    case "on":
                        await _botState.UpdateStateAsync(s => s.AutoPred = true);
                        await _ircClient.SendMessage($"@{user.Name} Автоставки Включены!");
                        _logger.LogInformation("{Name} Включил ставки!", user.Name);
                        break;

                    default:
                        await _ircClient.SendMessage($"@{user.Name} Не правильный параметр! (on/off)");
                        break;
                }
            }
            else
                await _ircClient.SendMessage($"{user.Name} Не правильная команда! (!prediction on/off)");
        }

        public async Task LpCommand(UserObject user, string[] command)
        {
            if (command.Length > 2)
            {
                if (!_illAccess.Mod(user)) return;
                if (!_botState.Current.InMatch)
                {
                    string region = command.Last();
                    if (region != "ru" && region != "euw" && region != "na")
                    {
                        await _ircClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
                        return;
                    }

                    var result = await _riotApi.UpdateSummonerByNameAsync(command[1], command[2], region);
                    if (result == null)
                    {
                        // Update Game State via Service
                        await _gameState.UpdateStateAsync(state =>
                        {
                            state.SummonerName = command[1] + "#" + command[2];
                            state.SummonerRegion = region;
                        });

                        _riotApi.UpdateConfig(); // Ensure API refreshes its internal state

                        var Rank = await _riotApi.GetRankBySummonerAsync();

                        // Update Rank Data
                        await _gameState.UpdateStateAsync(state =>
                        {
                            if (Rank != null)
                            {
                                if (int.TryParse(Rank[1], out int buffStartLP))
                                    state.StartLP = buffStartLP;
                                else
                                    state.StartLP = 0;
                                state.Elo = Rank[0];
                                state.Tier = Rank[2];
                            }
                        });
                        await _configWriter.WriteAsync();
                        await ShowLPAsync(user.Name);
                    }
                    else
                    {
                        await _ircClient.SendMessage($"ERROR: {result}");
                    }
                }
                else
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.LPInaMatch, user.Name));
                }
            }
            else
            {
                await ShowLPAsync(user.Name);
            }
        }

        public async Task RouletteTop(UserObject user)
        {
            await TopRulete();
        }

        public async Task AddVIP(UserObject user, string[] UserInput)
        {
            if (UserInput.Length == 2)
            {
                var aUser = await _databaseService.GetUserAsync(UserInput[1]);
                if (aUser.dbID != -404)
                {
                    await _twitchService.AddChannelVIP(aUser.TwitchID.ToString());
                    await _ircClient.SendMessage(string.Format(STRINGS.AddVIPSuccess, aUser.Name));
                }
                else
                    await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                await _ircClient.SendMessage(STRINGS.InputERROR);
        }

        public async Task DeleteVIP(UserObject user, string[] UserInput)
        {
            if (UserInput.Length == 2)
            {
                var aUser = await _databaseService.GetUserAsync(UserInput[1]);
                if (aUser.dbID != -404)
                {
                    await _twitchService.DeleteChannelVIP(aUser.TwitchID.ToString());
                    await _ircClient.SendMessage(string.Format(STRINGS.DeleteVIPSuccess, aUser.Name));
                }
                else
                    await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                await _ircClient.SendMessage(STRINGS.InputERROR);
        }

        public async Task<TrackUser> TrackUser(UserObject user, string[] UserInput)
        {
            if (UserInput.Length > 1)
            {
                var result = await _databaseService.TrackUserAsync(UserInput[1].ToLower());
                if (result != null)
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.TrackUserSuccess, UserInput[1], result.Count));
                    string outDbs = "";
                    foreach (var r in result.DBName)
                    {
                        outDbs += r + ", ";
                        if (outDbs.Length > 450)
                        {
                            await _ircClient.SendMessage(outDbs);
                            outDbs = "";
                        }
                    }
                    await _ircClient.SendMessage(outDbs);
                }
                else
                {
                    await _ircClient.SendMessage("ERROR");
                }
            }
            return null;
        }
        public async Task GetBotState(UserObject user)
        {
            var s = _botState.Current;
            string message = $"⚙️ BOT STATE: " +
                             $"Debug: {s.Debug} | " +
                             $"Silent: {s.IsSilent} | " +
                             $"SubActive: {s.IsSubActive} | " +
                             $"FilterLvl: {s.ChatFilterLvl} | " +
                             $"AntiBot: {s.AntiBotProtectionLvl} | " +
                             $"AutoPred: {s.AutoPred} | " +
                             $"InMatch: {s.InMatch} | " +
                             $"StreamOnline: {s.BroadcasterIsOnline} | " +
                             $"QuizRunning: {s.QuizIsRunning}";

            await _ircClient.SendMessage(message);
        }
        public async Task<LP> GetLpAsync(string summonerName = null, string region = null)
        {
            bool isForCurrentUser = string.IsNullOrEmpty(summonerName);
            LeagueEntry[] rank;

            if (isForCurrentUser)
            {
                rank = await _riotApi.GetLeagueEntriesBySummonerAsync();
            }
            else
            {
                rank = await _riotApi.GetLeagueEntriesBySummonerAsync(summonerName, region);
            }

            if (rank != null)
            {
                var soloQueueRank = rank.FirstOrDefault(mType => mType.QueueType == QueueType.RANKED_SOLO_5x5);
                if (soloQueueRank != null)
                {
                    if (soloQueueRank.MiniSeries != null)
                    {
                        var promo = soloQueueRank.MiniSeries.Progress.Select(prog => prog switch
                        {
                            'L' => "❌",
                            'W' => "✅",
                            'N' => "➖",
                            _ => ""
                        }).ToList();

                        string tier = StringUtil.ConvertRank(Convert.ToString(int.Parse(StringUtil.ConvertRank($"{soloQueueRank.Tier} {soloQueueRank.Rank}", true)) + 1), false);
                        string[] subs = tier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        return new LP { RANK = "ПРОМО В " + subs[0], LPoints = string.Join(" ", promo) };
                    }
                    else
                    {
                        return new LP { RANK = $"{soloQueueRank.Tier} {soloQueueRank.Rank}", LPoints = soloQueueRank.LeaguePoints.ToString() };
                    }
                }
                return new LP { RANK = "Калибровка", LPoints = null };
            }
            return new LP { RANK = "Riot API error", LPoints = null };
        }

        public async Task ShowLPAsync(string sender)
        {
            var lpData = await GetLpAsync();
            if (lpData.RANK == "Riot API error" || lpData.RANK == null)
            {
                await _ircClient.SendMessage("Riot API error");
                return;
            }

            if (lpData.RANK.StartsWith("ПРОМО"))
            {
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLPPromo, sender, _gameState.Current.SummonerName, lpData.RANK, lpData.LPoints));
            }
            else if (lpData.RANK == "Калибровка")
            {
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLPCalibration, sender, _gameState.Current.SummonerName, _gameState.Current.NumGames, _gameState.Current.NumWins, _gameState.Current.NumLosses, _gameState.Current.EarnedLP));
            }
            else
            {
                var rankParts = lpData.RANK.Split(' ');
                var rank = await _riotApi.GetRankBySummonerAsync(); // Note: This might be redundant if we just want Win/Loss, optimized slightly below
                int wins = 0, losses = 0;

                var leagueEntries = await _riotApi.GetLeagueEntriesBySummonerAsync();
                if (leagueEntries != null)
                {
                    var soloQ = leagueEntries.FirstOrDefault(q => q.QueueType == QueueType.RANKED_SOLO_5x5);
                    if (soloQ != null) { wins = soloQ.Wins; losses = soloQ.Losses; }
                }

                int WR = (wins + losses > 0) ? (int)Math.Ceiling(wins * 100.0 / (wins + losses)) : 0;
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLP, sender, _gameState.Current.SummonerName, rankParts[0], rankParts[1], lpData.LPoints, WR, _gameState.Current.NumGames, _gameState.Current.NumWins, _gameState.Current.NumLosses, _gameState.Current.EarnedLP));
            }
        }

        public async Task GetMatchHistory(UserObject user)
        {
            var matchData = await _riotApi.GetLastMatchParticipantAsync().ConfigureAwait(false);

            if (matchData != null)
            {
                string result = matchData.Win ? "Победа" : "Поражение";
                // Formatting: @User Последняя игра: Победа (5/0/10) на Garen
                await _ircClient.SendMessage($"@{user.Name} Последняя игра: {result} ({matchData.Kills}/{matchData.Deaths}/{matchData.Assists}) на {matchData.ChampionName}");
            }
            else
            {
                await _ircClient.SendMessage($"@{user.Name} Не удалось получить информацию о последнем матче.");
            }
        }

        public async Task TopRulete()
        {
            var result = await _databaseService.GetTopUsersAsync("rtop");
            if (result != null && result.Count >= 3)
            {
                await _ircClient.SendMessage(string.Format
                 (
                    STRINGS.Top3Roulette,
                    result[0].Name, result[0].roulettCon, IntUtil.RulProbability(result[0].roulettCon, 80),
                    result[1].Name, result[1].roulettCon, IntUtil.RulProbability(result[1].roulettCon, 80),
                    result[2].Name, result[2].roulettCon, IntUtil.RulProbability(result[2].roulettCon, 80)
                 ));
            }
            else
            {
                _logger.LogError("Cant get 3 users at TopRulete");
            }
        }

        public async Task GetTopChat(UserObject user)
        {
            var result = await _databaseService.GetTopUsersAsync("top");
            if (result != null && result.Count >= 3)
            {
                await _ircClient.SendMessage(string.Format
                            (
                             STRINGS.Top3Chat,
                             result[0].Name, result[0].messageCon,
                             result[1].Name, result[1].messageCon,
                             result[2].Name, result[2].messageCon
                            ));
            }
            else
            {
                _logger.LogError("Cant get 3 users at GetTopChat");
            }
        }
        public async Task ToggleGodMode(UserObject user)
        {
            await _botState.UpdateStateAsync(s => s.GodMode = !s.GodMode);
            string status = _botState.Current.GodMode ? "ENABLED" : "DISABLED";
            await _ircClient.SendMessage($"GodMode is now {status}.");
            _logger.LogWarning("GodMode toggled to {Status} by {User}", status, user.Name);
        }
        public async Task StartQuizz()
        {
            await _illGames.Quizz(true);
        }

        public async Task GetMMR(UserObject user)
        {
            var result = await _mmrService.GetMMR(_gameState.Current.SummonerName);
            if (result == null) return;
            if (result.Count == 2)
                await _ircClient.SendMessage($"@{user.Name} {result[0]}: mmr:{result[1]}");
        }

        public async Task OpGG(UserObject user)
        {
            await _ircClient.SendMessage(string.Format(STRINGS.OpGGMessage, user.Name, _gameState.Current.SummonerName.Replace('#', '-')));
        }

        public async Task CreateClip(UserObject user)
        {
            var response = await _twitchService.CreateClip();
            if (response != null && response.CreatedClips.Any())
            {
                var clipUrl = response.CreatedClips[0].EditUrl.Replace("/edit", "");
                await _ircClient.SendMessage(string.Format(STRINGS.CreateClipSuccess, user.Name, clipUrl));
            }
            else
            {
                await _ircClient.SendMessage(string.Format(STRINGS.CreateClipERROR, user.Name, "API Error"));
            }
        }
        public async Task TogglePDebug(UserObject user)
        {
            await _botState.UpdateStateAsync(s => s.PerformanceDebugMode = !s.PerformanceDebugMode);
            string status = _botState.Current.PerformanceDebugMode ? "ON" : "OFF";
            await _ircClient.SendMessage($"⚡ Performance Debug is now: {status}");
        }
        public async Task FlushChat(UserObject user)
        {
            await _twitchService.DeleteAllMessages();
        }

        public async Task<UserObject> QuizzMediaReward(UserObject user, string[] UserInput)
        {
            if (user.isMod == 1) return user;
            if (user.QuizPoints > 1)
            {
                if (UserInput.Length < 2)
                {
                    await _ircClient.SendMessage(STRINGS.InputERROR);
                    return user;
                }
                if (await _rewardsRedemption.ZakazTrekaReward(user.Name, string.Join(" ", UserInput.Skip(1)), null, null))
                {
                    user.QuizPoints -= 2;
                }
            }
            return user;
        }

        public async Task BanUserForTrack(UserObject user)
        {
            var history = await _streamElementsService.GetHistory();
            if (history == null || !history.History.Any()) return;
            var lastSong = history.History[0].Song;
            int userID = await _mediaQueueService.GetUserIdByTrackIdAsync(lastSong.VideoId);
            await _blacklistService.AddToMediaBlacklistAsync(lastSong.VideoId);
            if (userID != -1)
            {
                var uUser = await _databaseService.GetUserAsync(userID);
                await _twitchService.TimeOutUser(uUser, 3600, STRINGS.TimeOutReason_Track);
                await _blacklistService.AddToUserBlacklistAsync(uUser.TwitchID.ToString());
                await _ircClient.SendMessage(string.Format(STRINGS.BanUserForTrack_chatMessage, user.Name, uUser.Name));
            }
            else
            {
                await _ircClient.SendMessage(string.Format(STRINGS.BanUserForTrack_DonatedTrack, user.Name));
            }
        }

        public async Task FindUser(UserObject user, string[] input)
        {
            if (input.Length > 1)
            {
                var Name = StringUtil.GetUserNameFromInput(input[1]);
                if (Name != null)
                {
                    var idFind = await _databaseService.GetUserAsync(Name);
                    if (idFind == null || idFind.dbID == -404)
                    {
                        await _ircClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, Name));
                    }
                    else
                    {
                        await _ircClient.SendMessage($"dbID {idFind.dbID}, ttvID {idFind.TwitchID}, login {idFind.Name}, isSub {idFind.isSub}, isVip {idFind.isVip}, isMod {idFind.isMod}, IsBroadcaster {idFind.IsBroadcaster}, Uval№ {idFind.UvalCon}, messag№ {idFind.messageCon}, roulet_ws {idFind.roulettCon}, Quizz {idFind.QuizPoints}, QuizzT {idFind.QuizTotal}, IsPartner {idFind.isPartner}");
                    }
                }
                else
                    await _ircClient.SendMessage(STRINGS.InputERROR);
            }
        }

        public async Task DisableReward(UserObject user, string[] input)
        {
            if (input.Length != 2)
            {
                await _ircClient.SendMessage("usage - !disablereward <rewardID>");
                return;
            }

            await _ircClient.SendMessage($"Disabling rewardID - {input[1]}");
            var reward = await _twitchService.GetReward(input[1]);
            if (reward == null)
                await _ircClient.SendMessage("Error 404 - Награда не найденa");
            else
                await _twitchService.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired);
        }

        public async Task CreateReward(UserObject user, string[] args)
        {
            if (args.Length == 6)
            {
                string title = args[1];
                if (int.TryParse(args[2], out int cost) &&
                    bool.TryParse(args[4], out bool enabled) &&
                    bool.TryParse(args[5], out bool userinput))
                {
                    string prompt = args[3];
                    await _ircClient.SendMessage($"title - {title}, cost - {cost}, prompt - {prompt}, enabled - {enabled}, userinput - {userinput}");
                    var response = await _twitchService.CreateReward(title, cost, prompt, enabled, userinput);
                    if (response != null)
                        await _ircClient.SendMessage($"Reward created with ID: {response}");
                }
                else
                {
                    await _ircClient.SendMessage("Invalid parameters. Cost must be an integer, enabled/userinput must be boolean (true/false).");
                }
            }
            else
            {
                await _ircClient.SendMessage("Usage: !createreward \"title\" cost \"prompt\" enabled userinput");
            }
        }

        public async Task DeleteReward(UserObject user, string[] input)
        {
            await _ircClient.SendMessage("!deletereward is not implemented yet.");
        }

        public async Task UpdateReward(UserObject user, string[] args)
        {
            if (args.Length == 7)
            {
                string rewardID = args[1];
                string title = args[2];
                string prompt = args[4];

                if (int.TryParse(args[3], out int cost) &&
                    bool.TryParse(args[5], out bool enabled) &&
                    bool.TryParse(args[6], out bool userinput))
                {
                    await _ircClient.SendMessage($"rewardID - {rewardID}, title - {title}, cost - {cost}, prompt - {prompt}, enabled - {enabled}, userinput - {userinput}");
                    await _twitchService.UpdateReward(rewardID, title, cost, prompt, enabled, userinput);
                }
                else
                {
                    await _ircClient.SendMessage("Invalid parameters. Ensure cost is an integer and enabled/userinput are booleans.");
                }
            }
            else
            {
                await _ircClient.SendMessage("Usage: !updatereward rewardID \"title\" cost \"prompt\" enabled userinput");
            }
        }

        public async Task EnableReward(UserObject user, string[] args)
        {
            if (args.Length == 2)
            {
                string rewardID = args[1];
                await _ircClient.SendMessage($"rewardID - {rewardID}");
                var reward = await _twitchService.GetReward(rewardID);
                if (reward == null)
                    await _ircClient.SendMessage("Error 404 - Награда не найденa");
                else
                    await _twitchService.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired);
            }
            else if (args.Length == 3)
            {
                string title = args[1];
                string text = args[2];
                await _ircClient.SendMessage($"Title - {title}");
                var reward = await _twitchService.GetReward(title, text);
                if (reward == null)
                    await _ircClient.SendMessage("Error 404 - Награда не найденa");
                else
                    await _twitchService.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired);
            }
            else
            {
                await _ircClient.SendMessage("Usage: !enablereward <rewardID> or !enablereward \"title\" \"text\"");
            }
        }

        public async Task SetAntiBotLvl(UserObject user, string[] input)
        {
            if (input.Length > 1 && int.TryParse(input[1], out int level) && level >= 0 && level <= 2)
            {
                await _botState.UpdateStateAsync(s => s.AntiBotProtectionLvl = level);
                await _ircClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, _botState.Current.AntiBotProtectionLvl));
            }
            else
            {
                await _ircClient.SendMessage($"{STRINGS.InputERROR}. Valid levels are 0, 1, 2.");
            }
        }

        public async Task SetChatfilterLvl(UserObject user, string[] input)
        {
            if (input.Length == 2 && int.TryParse(input[1], out int level) && level >= 0 && level <= 5)
            {
                await _botState.UpdateStateAsync(s => s.ChatFilterLvl = level);
                await _configWriter.WriteAsync(); // Note: ChatFilterLvl is duplicated in config and botstate?
                await _ircClient.SendMessage($"Уровень модерации чата установлен в значение {level}!");
            }
            else
            {
                await _ircClient.SendMessage("Допустимые значения: 0, 1, 2, 3, 4, 5. 0 - бездействие. 1 - удаление сообщения. 2 - удаления и оповещение модераторов. 3 - таймаут на сутки. 4 - таймаут на неделю. 5 - бан. <<!chatfilter 3>>");
            }
        }

        public async Task GetAllRewards(UserObject user)
        {
            var rewards = await _twitchService.GetAllRewards();
            if (rewards == null) return;
            int rewardsCount = rewards.Data.Length;
            string rewardsTitle = string.Join(" | ", rewards.Data.Select(r => r.Title));
            string message = string.Format(STRINGS.GetAllReward_chatMessage, rewardsCount, rewardsTitle);
            await _ircClient.SendMessage(message);
        }

        public async Task getJobs(UserObject user)
        {
            await _ircClient.SendMessage(await _quartzManager.GetRunningJobs());
        }

        public async Task ChangeLanguage(UserObject user, string[] input)
        {
            if (input.Length < 2) return;
            string langCode = input[1].ToLower();
            var cultureInfo = langCode switch
            {
                "ru" => new CultureInfo("ru-RU"),
                "eng" => new CultureInfo("en-US"),
                "fr" => new CultureInfo("fr-FR"),
                "ja" => new CultureInfo("ja-JP"),
                "ko" => new CultureInfo("ko-KR"),
                _ => null
            };

            if (cultureInfo != null)
            {
                CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
                CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
                await _ircClient.SendMessage(STRINGS.language);
            }
        }

        public async Task TestingMethod(UserObject user)
        {
            Console.WriteLine("test");
            await Task.CompletedTask;
        }

        public async Task ToggleDebug(UserObject user)
        {
            await _botState.UpdateStateAsync(s => s.Debug = !s.Debug);

            if (_botState.Current.Debug)
                _loggingSwitch.MinimumLevel = LogEventLevel.Debug;
            else
                _loggingSwitch.MinimumLevel = LogEventLevel.Warning;

            await _ircClient.SendMessage($"Debug mode is now {_botState.Current.Debug}");
        }

        public async Task ToggleSilentMode(UserObject user)
        {
            await _botState.UpdateStateAsync(s => s.IsSilent = !s.IsSilent);
            await _ircClient.SendMessage($"SilentMode mode is {_botState.Current.IsSilent}");
        }

        public async Task Ttvgg(UserObject user)
        {
            long currentUnixTime = DateTimeOffset.Now.ToUnixTimeSeconds();
            var taskList = new List<Task<int[]>>
            {
                _databaseService.GetUserPositionAsync(user.Name, "roulettCon"),
                _databaseService.GetUserPositionAsync(user.Name, "UvalCon"),
                _databaseService.GetUserPositionAsync(user.Name, "messageCon"),
                _databaseService.GetUserPositionAsync(user.Name, "Points"),
                _databaseService.GetUserPositionAsync(user.Name, "QuizPoints"),
                _databaseService.GetUserPositionAsync(user.Name, "QuizTotal")
            };
            var results = await Task.WhenAll(taskList);
            var roulettCD = user.roulettCD - currentUnixTime;
            roulettCD = roulettCD < 0 ? 0 : roulettCD;
            TimeSpan time = TimeSpan.FromSeconds(roulettCD);
            await _ircClient.SendMessage($"@{user.Name}, твой винстрик в рулетке {user.roulettCon} {IntUtil.CalculateTopPercentage(results[0])}, " +
                $"всего ты отправил {user.messageCon} сообщений {IntUtil.CalculateTopPercentage(results[2])}, " +
                $"ты был в увале {user.UvalCon} раз {IntUtil.CalculateTopPercentage(results[1])}, " +
                $"у тебя есть {user.QuizPoints} баллов квиза {IntUtil.CalculateTopPercentage(results[4])}, " +
                $"за все время ты набрал {user.QuizTotal} баллов квиза {IntUtil.CalculateTopPercentage(results[5])}, " +
                $"у тебя есть {user.Points} поинтов {IntUtil.CalculateTopPercentage(results[3])}, " +
                $"кулдаун у твоей рулетки продлится еще {time:hh\\:mm\\:ss}");
        }

        public async Task RemoveUserFromBlacklist(UserObject user, string[] input)
        {
            if (input.Length != 2)
            {
                await _ircClient.SendMessage(STRINGS.InputERROR);
                return;
            }
            var UserToUnban = await _databaseService.GetUserAsync(input[1]);
            if (UserToUnban.dbID == -404)
            {
                await _ircClient.SendMessage(STRINGS.FindUser_ERROR404);
                return;
            }
            var path = _pathProvider.GetFullPath(_config.FilePaths.UserBlacklistFileName);
            if (await FileManipulator.DeleteLineFromFileAsync(path, UserToUnban.TwitchID.ToString()))
            {
                _chatFilters.EditUserBlackList(UserToUnban.TwitchID.ToString());
                await _ircClient.SendMessage($"Пользователь {UserToUnban.Name} удален из черного списка");
            }
            else
                await _ircClient.SendMessage($"Пользователь {UserToUnban.Name} не был найден в черном списке");
        }

        public async Task AddTowhiteList(UserObject user, string[] input)
        {
            if (input.Length != 2)
            {
                await _ircClient.SendMessage(STRINGS.InputERROR);
                return;
            }
            var path = _pathProvider.GetFullPath(_config.FilePaths.DicWhiteListFileName, isShared: true); 
            await FileManipulator.AddLineToFileAsync(path, input[1]);
            _chatFilters.AddToWhiteList(input[1]);
        }

        public async Task AddSubscription(UserObject user)
        {
            var t = await _subscriptionService.AddSubscriptionAsync();
            await _ircClient.SendMessage(t.ToString());
            await _subscriptionService.CheckSubscriptionAsync(); //run the check right away to apply new subscription
        }

        public async Task CheckSubscription(UserObject user)
        {
            if (await _subscriptionService.CheckSubscriptionAsync())
                await _ircClient.SendMessage("Valid!");
            else
                await _ircClient.SendMessage("Expired!");
        }

        public async Task GetMods(UserObject user)
        {
            var mods = await _twitchService.GetAllMods();
            if (mods == null || !mods.Any())
            {
                await _ircClient.SendMessage("Could not retrieve moderators.");
                return;
            }
            string moderators = string.Join(", ", mods.Select(m => m.UserLogin));
            await _ircClient.SendMessage($"Mods: {moderators}");
        }

        public async Task Sheptun(UserObject user)
        {
            var mods = await _twitchService.GetAllMods();
            foreach (var mod in mods)
            {
                await _twitchService.SendWhisper(mod.UserId, "Тестовый шептун");
                await Task.Delay(100);
            }
        }

        public async Task Ludka(UserObject user, string[] input)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1)
                if (input.Length > 1)
                {
                    _ludka = StringUtil.GetCommandFromUserInput(input);
                    return;
                }
            await _ircClient.SendMessage(_ludka);
        }

        public async Task Ping()
        {
            await _ircClient.SendMessage("pong");
        }

        public async Task ReloadFilters(UserObject user)
        {
            _chatFilters.ReloadFilters();
            await _ircClient.SendMessage("Chat filters have been reloaded.");
        }

        public async Task GetServiceStatus(UserObject user)
        {
            // 1. System Stats
            using var process = Process.GetCurrentProcess();
            var uptime = DateTime.Now - process.StartTime;
            double ramUsage = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            int threadCount = process.Threads.Count;

            var dbStats = await _databaseService.GetStatsAsync();

            // 2. Connection Stats
            string ircStatus = _ircClient.IsConnected ? "Connected" : "Disconnected";

            // 4. Game Logic Stats
            string matchStatus = _botState.Current.InMatch ? "In Match" : "Idle";
            string predStatus = _botState.Current.AutoPred ? "On" : "Off";

            //ToDo 5. Message queue status

            string output =
                $"[SYS] UpTime: {uptime:dd\\:hh\\:mm} |RAM: {ramUsage:F0}MB |Threads: {threadCount} || " +
                $"[DB Sess] Msgs: {dbStats.SessionMessagesSaved} | New users: {dbStats.SessionNewUsers} | Qry: {dbStats.SessionQueries} || " +
                $"[DB Tot] {dbStats.TotalMessages} msgs | {dbStats.TotalUsers} users";

            await _ircClient.SendMessage(output);
        }
    }
}