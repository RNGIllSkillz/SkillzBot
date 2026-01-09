using Camille.Enums;
using Camille.RiotGames.LeagueV4;
using IllSkillzBot;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using SkillzBot.API.MMR;
using SkillzBot.API.RiotGames;
using SkillzBot.API.StreamElements;
using SkillzBot.API.Twitch;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Readers;
using SkillzBot.Singleton;
using SkillzBot.SubUtils;
using SkillzBot.TtvClient.TTVRewards;
using SkillzBot.Utils;
using SkillzBot.Writers;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
    internal class IllCommands
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly IllModeratorsInteractions _modInteractions;
        private readonly IllChatFilters _chatFilters;
        private readonly RewardsRedemption _rewardsRedemption;
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<IllCommands> _logger;
        private readonly IRiotApiService _riotApi;
        private readonly IllGames _illGames;
        private readonly QuartzBackgroundTaskManager _quartzManager;
        private readonly LoggingLevelSwitch _loggingSwitch;
        private readonly ITwitchService _twitchService;

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
            ITwitchService twitchService)
        {
            _ircClient = ircClient;
            _modInteractions = modInteractions;
            _chatFilters = chatFilters;
            _rewardsRedemption = rewardsRedemption;
            _databaseService = databaseService;
            _logger = logger;
            _riotApi = riotApi;
            _illGames = illGames;
            _quartzManager = quartzManager;
            _loggingSwitch = loggingSwitch;
            _twitchService = twitchService;
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
                        IllSingleton.State.AutoPred = false;
                        await _ircClient.SendMessage($"@{user.Name} Автоставки Выключены!");
                        _logger.LogInformation("{Name} Выключил ставки!", user.Name);
                        break;

                    case "on":
                        IllSingleton.State.AutoPred = true;
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
                if (!IllAccess.Mod(user)) return;
                if (!IllSingleton.State.InMatch)
                {
                    switch (command.Last())
                    {
                        case "ru":
                        case "euw":
                        case "na":
                            break;
                        default:
                            await _ircClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
                            return;
                    }

                    var result = await _riotApi.UpdateSummonerByNameAsync(command[1], command[2], command.Last());
                    if (result == null)
                    {
                        IllSingleton.Game.SummonerName = command[1] + "#" + command[2];
                        IllSingleton.Game.SummonerRegion = command.Last();
                        _riotApi.UpdateConfig();

                        var Rank = await _riotApi.GetRankBySummonerAsync();
                        if (Rank != null)
                        {
                            if (int.TryParse(Rank[1], out int buffStartLP))
                                IllSingleton.Game.StartLP = buffStartLP;
                            else
                                IllSingleton.Game.StartLP = 0;
                            IllSingleton.Game.Elo = Rank[0];
                            IllSingleton.Game.Tier = Rank[2];
                        }
                        await IllSingleton.Game.SaveAsync();
                        await BotConfigWriter.WriteAsync();
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

        public async Task<UserObject> IllFilterTrigger(UserObject user, string messageID = null)
        {
            if (user.banCount == 35)
            {
                await _twitchService.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
                user.banCount = 0;
            }
            else
            {
                switch (IllSingleton.State.ChatFilterLvl)
                {
                    case 0:
                        break;
                    case 1:
                        if (messageID != null)
                            await _twitchService.DeleteMessage(messageID);
                        break;
                    case 2:
                        if (messageID != null)
                            await _twitchService.DeleteMessage(messageID);
                        string ModsZapMsg = $"Найдена запретка на канале {IllSingleton.Config.ChannelName} от пользователя @{user.Name}. Модерам на проверку";
                        await _modInteractions.IllAllModsNotification(ModsZapMsg);
                        break;
                    case 3:
                        await _twitchService.TimeOutUser(user, 86400, STRINGS.TimeOut1wReason);
                        user.banCount++;
                        break;
                    case 4:
                        await _twitchService.TimeOutUser(user, 604800, STRINGS.TimeOut1wReason);
                        user.banCount++;
                        break;
                    case 5:
                        await _twitchService.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
                        user.banCount = 0;
                        break;
                    default:
                        break;
                }
            }
            return user;
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
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLPPromo, sender, IllSingleton.Game.SummonerName, lpData.RANK, lpData.LPoints));
            }
            else if (lpData.RANK == "Калибровка")
            {
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLPCalibration, sender, IllSingleton.Game.SummonerName, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
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
                await _ircClient.SendMessage(string.Format(STRINGS.ShowLP, sender, IllSingleton.Game.SummonerName, rankParts[0], rankParts[1], lpData.LPoints, WR, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
            }
        }

        public async Task GetMatchHistory(UserObject user)
        {
            await _ircClient.SendMessage("Команда в разработке. Верим.");
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

        public async Task StartQuizz()
        {
            // FIX: Using injected _illGames instance instead of manual 'new'
            await _illGames.Quizz(true);
        }

        public async Task GetMMR(UserObject user)
        {
            var result = await MyLOLMMRApi.GetMMR(IllSingleton.Game.SummonerName);
            if (result == null) return;
            if (result.Count == 2)
                await _ircClient.SendMessage($"@{user.Name} {result[0]}: mmr:{result[1]}");
        }

        public async Task OpGG(UserObject user)
        {
            await _ircClient.SendMessage(string.Format(STRINGS.OpGGMessage, user.Name, IllSingleton.Game.SummonerName.Replace('#', '-')));
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
            var history = await StreamElementsAPI.GetHistory();
            if (history == null || !history.History.Any()) return;
            var lastSong = history.History[0].Song;

            int userID = TempDataReader.GetUserIDByTreckID(lastSong.VideoId);
            await MediaBlackListWriter.Write(lastSong.VideoId);
            if (userID != -1)
            {
                var uUser = await _databaseService.GetUserAsync(userID);
                await _twitchService.TimeOutUser(uUser, 3600, STRINGS.TimeOutReason_Track);
                await UserBlackListWriter.Write(uUser.TwitchID.ToString());
                await _ircClient.SendMessage(string.Format(STRINGS.BanUserForTrack_chatMessage, user.Name, uUser.Name));
            }
            else
                await _ircClient.SendMessage(string.Format(STRINGS.BanUserForTrack_DonatedTrack, user.Name));
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
                IllSingleton.State.AntiBotProtectionLvl = level;
                await _ircClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, IllSingleton.State.AntiBotProtectionLvl));
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
                IllSingleton.State.ChatFilterLvl = level;
                await BotConfigWriter.WriteAsync();
                await _ircClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
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
            IllSingleton.State.Debug = !IllSingleton.State.Debug;
            if (IllSingleton.State.Debug)
                _loggingSwitch.MinimumLevel = LogEventLevel.Debug;
            else
                _loggingSwitch.MinimumLevel = LogEventLevel.Warning;

            await _ircClient.SendMessage($"Debug mode is now {IllSingleton.State.Debug}");
        }

        public async Task ToggleSilentMode(UserObject user)
        {
            IllSingleton.State.IsSilent = !IllSingleton.State.IsSilent;
            await _ircClient.SendMessage($"SilentMode mode is {IllSingleton.State.IsSilent}");
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
            var path = IllSkillzBotMain.GetDataPath().uniquePath;
            path = Path.Combine(path, IllSingleton.Config.FilePaths.UserBlacklistFileName);
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
            var path = IllSkillzBotMain.GetDataPath().sharedPath;
            path = Path.Combine(path, IllSingleton.Config.FilePaths.DicWhiteListFileName);
            await FileManipulator.AddLineToFileAsync(path, input[1]);
            _chatFilters.AddToWhiteList(input[1]);
        }

        public async Task AddSubscription(UserObject user)
        {
            await _ircClient.SendMessage(AddSub.NewPurchase().ToString());
            SubCheck.RunChecker();
        }

        public async Task CheckSubscription(UserObject user)
        {
            if (SubCheck.RunChecker())
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
    }
}