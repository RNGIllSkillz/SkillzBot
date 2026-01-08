using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using SkillzBot.Writers;
using F23.StringSimilarity;
using SkillzBot.API.MMR;
using SkillzBot.API.StreamElements;
using SkillzBot.Readers;
using SkillzBot.TtvClient.TTVRewards;
using System.Globalization;
using SkillzBot.IllSTRINGS;
using IllSkillzBot;
using System.IO;
using SkillzBot.SubUtils;
using Camille.Enums;
using SkillzBot.API.RiotGames;
using SkillzBot.Interfaces;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Singleton;

namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
    internal class IllCommands
    {
        readonly static List<string> popMessages = new List<string>();
        private static IDatabaseService _databaseService = IllServiceProvider.Database;
        private static readonly ILogger<IllCommands> _logger = IllServiceProvider.GetLogger<IllCommands>();
        private static string _ludka = "";
        private static IRiotApiService RiotAPI = IllServiceProvider.GetService<IRiotApiService>();

        public static async Task Help(UserObject user)
        {
            await TtvIRCClient.SendMessage(string.Format(STRINGS.HelpMessage, user.Name));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task Points(UserObject user)
        { 
            var pos = await _databaseService.GetUserPositionAsync(user.Name, "Points").ConfigureAwait(false);
            var QPos = await _databaseService.GetUserPositionAsync(user.Name, "QuizPoints").ConfigureAwait(false);
            var QtPos = await _databaseService.GetUserPositionAsync(user.Name, "QuizTotal").ConfigureAwait(false);
            await TtvIRCClient.SendMessage(string.Format(STRINGS.PointsMessage, user.Name, user.Points, pos[0], pos[1], user.QuizPoints, QPos[0], QPos[1], user.QuizTotal, QtPos[0], QtPos[1]));
        }
        public static async Task Prediction(UserObject user, string[] command)
        {
            if (command.Length > 1)
            {
                switch (command[1])
                {
                    case "off":
                        IllSingleton.State.AutoPred = false;
                        await TtvIRCClient.SendMessage($"@{user.Name} Автоставки Выключены!");
                        _logger.LogInformation("{Name} Выключил ставки!", user.Name);
                        break;

                    case "on":
                        IllSingleton.State.AutoPred = true;
                        await TtvIRCClient.SendMessage($"@{user.Name} Автоставки Включены!");
                        _logger.LogInformation("{Name} Включил ставки!", user.Name);
                        break;

                    default:
                        await TtvIRCClient.SendMessage($"@{user.Name} Не правильный параметр! (on/off)");
                        break;
                }
            }
            else
                await TtvIRCClient.SendMessage($"{user.Name} Не правильная команда! (!prediction on/off)");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task LpCommand(UserObject user, string[] command)
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
                            await TtvIRCClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
                            return;
                    }
                    //var temp = StringUtil.RemoveWhitespace(StringUtil.GetCommandFromUserInput(command.Take(command.Count() - 1).ToArray()));                        
                    var result = await RiotAPI.UpdateSummonerByNameAsync(command[1], command[2], command.Last()).ConfigureAwait(false);
                    if (result == null)
                    {
                        IllSingleton.Game.SummonerName = command[1] + "#" + command[2];
                        IllSingleton.Game.SummonerRegion = command.Last();
                        RiotAPI.UpdateConfig();

                        var Rank = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                        if (Rank != null)
                        {
                            if (int.TryParse(Rank[1], out int buffStartLP))
                                IllSingleton.Game.StartLP = buffStartLP;
                            else
                                IllSingleton.Game.StartLP = 0;
                            IllSingleton.Game.Elo = Rank[0];
                            IllSingleton.Game.Tier = Rank[2];
                        }
                        SaveGameStats();
                        SaveAppConfig();
                        await ShowLPAsync(user.Name).ConfigureAwait(false);
                    }
                    else
                    {
                        await TtvIRCClient.SendMessage($"ERROR: {result}");
                    }
                }
                else
                {
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.LPInaMatch, user.Name));
                }
            }
            else
            {
                await ShowLPAsync(user.Name).ConfigureAwait(false);                
            }
        }
        public static async Task RouletteTop(UserObject user)
        {
            await TopRulete().ConfigureAwait(false);
        }

        public static async Task AddVIP(UserObject user, string[] UserInput)
        {
            if (UserInput.Length == 2)
            {
                var aUser = await _databaseService.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.AddChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.AddVIPSuccess, aUser.Name));
                }
                else
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                await TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }
        public static async Task DeleteVIP(UserObject user, string[] UserInput)
        {
            if (UserInput.Length == 2)
            {
                var aUser = await _databaseService.GetUserAsync(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.DeleteChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.DeleteVIPSuccess, aUser.Name));
                }
                else
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                await TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }

        public static async Task<TrackUser> TrackUser(UserObject user, string[] UserInput)
        {
            if (UserInput.Length > 1)
            {
                var result = await _databaseService.TrackUserAsync(UserInput[1].ToLower()).ConfigureAwait(false);
                if (result != null)
                {
                    await TtvIRCClient.SendMessage(string.Format(STRINGS.TrackUserSuccess, UserInput[1], result.Count));
                    string outDbs = "";
                    foreach (var r in result.DBName)
                    {
                        outDbs += r + ", ";
                        if (outDbs.Length == 450)
                        {
                            await TtvIRCClient.SendMessage(outDbs);
                            outDbs = "";
                        }
                    }
                    await TtvIRCClient.SendMessage(outDbs);
                }
                else
                {
                    await TtvIRCClient.SendMessage("ERROR");
                }
            }
            return null;
        }
        public static async Task<UserObject> IllFilterTrigger(UserObject user, string messageID = null)
        {
            if (user.banCount == 35)
            {
                await TtvAPI.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
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
                            await TtvAPI.DeleteMessage(messageID).ConfigureAwait(false);
                        break;
                    case 2:
                        if (messageID != null)
                            await TtvAPI.DeleteMessage(messageID).ConfigureAwait(false);
                        string ModsZapMsg = $"Найдена запретка на канале {IllSingleton.Config.ChannelName} от пользователя @{user.Name}. Модерам на проверку";
                        await IllModeratorsInteractions.IllAllModsNotification(ModsZapMsg).ConfigureAwait(false);
                        break;
                    case 3:
                        await TtvAPI.TimeOutUser(user, 86400, STRINGS.TimeOut1wReason).ConfigureAwait(false);
                        user.banCount++;
                        break;
                    case 4:
                        await TtvAPI.TimeOutUser(user, 604800, STRINGS.TimeOut1wReason).ConfigureAwait(false);
                        user.banCount++;
                        break;
                    case 5:
                        await TtvAPI.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
                        user.banCount = 0;
                        break;
                    default:
                        break;
                }
            }
            return user;
        }
        public static async Task<LP> GetLpAsync(string summoner = null, string region = null)
        {
            bool ranked = false;
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync(summoner, region).ConfigureAwait(false);
            if (rank != null)
                foreach (var mType in rank)
                {
                    if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
                    {
                        ranked = true;
                        if (mType.MiniSeries != null)
                        {
                            var promo = new List<string>();
                            foreach (var prog in mType.MiniSeries.Progress)
                            {
                                if (prog == 'L')
                                    promo.Add("❌");
                                if (prog == 'W')
                                    promo.Add("✅");
                                if (prog == 'N')
                                    promo.Add("➖");
                            }
                            string tier = StringUtil.ConvertRank(Convert.ToString(int.Parse(StringUtil.ConvertRank($"{mType.Tier} {mType.Rank}", true)) + 1), false);
                            string[] subs = tier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var promoString = string.Join(" ", promo);
                            return new LP
                            {
                                RANK = "ПРОМО В " + subs[0],
                                LPoints = promoString
                            };
                        }
                        else
                        {
                            int WR = (int)Math.Ceiling(mType.Wins * 100 / (double)(mType.Wins + mType.Losses));
                            return new LP
                            {
                                RANK = mType.Tier + " " + mType.Rank,
                                LPoints = mType.LeaguePoints.ToString()
                            };
                        }
                    }
                }
            else
            {
                return new LP
                {
                    RANK = "Riot API error",
                    LPoints = null
                };
            }
            if (!ranked)
            {
                return new LP
                {
                    RANK = "Калибровка",
                    LPoints = null
                };
            }
            return new LP
            {
                RANK = "ERROR",
                LPoints = null
            };
        }
        public static async Task ShowLPAsync(string sender)
        {
            bool ranked = false;
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
            if (rank != null)
                foreach (var mType in rank)
                {
                    if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
                    {
                        ranked = true;
                        if (mType.MiniSeries != null)
                        {
                            var promo = new List<string>();
                            foreach (var prog in mType.MiniSeries.Progress)
                            {
                                if (prog == 'L') promo.Add("❌");
                                if (prog == 'W') promo.Add("✅");
                                if (prog == 'N') promo.Add("➖");
                            }
                            string tier = StringUtil.ConvertRank(Convert.ToString(int.Parse(StringUtil.ConvertRank($"{mType.Tier} {mType.Rank}", true)) + 1), false);
                            string[] subs = tier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var promoString = string.Join(" ", promo);
                            await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPPromo, sender, IllSingleton.Game.SummonerName, subs[0], promoString));
                        }
                        else
                        {
                            int WR = (int)Math.Ceiling(mType.Wins * 100 / (double)(mType.Wins + mType.Losses));
                            await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLP, sender, IllSingleton.Game.SummonerName, mType.Tier, mType.Rank, mType.LeaguePoints, WR, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
                        }
                    }
                }
            else
                await TtvIRCClient.SendMessage("Riot API error");
            if (!ranked)
            {
                await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPCalibration, sender, IllSingleton.Game.SummonerName, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
            }
        }

        public static async Task GetMatchHistory(UserObject user)
        {
            /*
            var matchId = await RiotAPI.GetMatchListAsync().ConfigureAwait(false);
            if (matchId != null)
            {
                var match = await RiotAPI.GetMatchAsync(matchId.First()).ConfigureAwait(false);
                if (match != null)
                {
                    var Participant = RiotAPI.GetParticipantByMatch(match);
                    if (Participant != null)
                    {
                        var Champ = await RiotAPI.GetChampByIdAsync(Participant.ChampionId).ConfigureAwait(false);
                        if (Champ != null)
                        {
                            string type = match.Info.GameMode;
                            string win;
                            string role;
                            if (Participant.Winner)
                                win = STRINGS.win;
                            else
                                win = STRINGS.loose;
                            if (Participant.TeamPosition == "")
                                role = "";
                            else
                                role = string.Format(STRINGS.role, Participant.TeamPosition);
                            await TtvIRCClient.SendMessage(string.Format(STRINGS.MatchHistoryMessage, user.Name, Champ.Name, Participant.Kills, Participant.Deaths, Participant.Assists, role, type, win));
                        }
                    }
                }
            }
            */
            await TtvIRCClient.SendMessage("Команда в разработке. Верим.");
            await Task.CompletedTask.ConfigureAwait(false);
        }
       
        public static async Task TopRulete()
        {
            var result = await _databaseService.GetTopUsersAsync("rtop").ConfigureAwait(false);
            if (result != null && result.Count >= 3)
            {
                await TtvIRCClient.SendMessage(string.Format
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
        public static async Task GetTopChat(UserObject user)
        {
            var result = await _databaseService.GetTopUsersAsync("top").ConfigureAwait(false);
            if (result != null && result.Count >= 3)
            {
                await TtvIRCClient.SendMessage(string.Format
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
        public static void SaveGameStats()
        {
            GameStatsWriter.Write
                (
                $"{IllSingleton.Game.StartLP} " +
                $"{IllSingleton.Game.Elo} " +
                $"{IllSingleton.Game.EarnedLP} " +
                $"{IllSingleton.Game.NumLosses} " +
                $"{IllSingleton.Game.NumGames} " +
                $"{IllSingleton.Game.NumWins} " +
                $"{IllSingleton.Game.Tier}"
                );
        }
        public static void SaveAppConfig()
        {
            BotConfigWriter.Write();
        }
        public static async Task TypeInChat(string message)
        {
            popMessages.Add(message);
            if (popMessages.Count > 10)
                popMessages.RemoveAt(0);
            string sendMess = "";
            if (popMessages.Count == 10)
            {
                var jw = new NormalizedLevenshtein();
                foreach (var popMessage in popMessages.ToList())
                {
                    int popWeight = 0;
                    foreach (var checkpop in popMessages.ToList())
                    {
                        var sim1 = jw.Distance(popMessage, checkpop);
                        if (sim1 < 0.5)
                        {
                            popWeight++;
                            if (popWeight >= 5)
                                sendMess = checkpop;
                        }
                    }
                }
            }
            if (sendMess.Length > 2)
            {
                lock (popMessages)
                    popMessages.Clear();
                await TtvIRCClient.SendMessage(sendMess);
            }
        }
        public static async Task StartQuizz()
        {
            await IllGames.Quizz(true).ConfigureAwait(false);
        }
        public static async Task GetMMR(UserObject user)
        {
            var result = await MyLOLMMRApi.GetMMR(IllSingleton.Game.SummonerName).ConfigureAwait(false);
            if (result == null) return;
            if (result.Count == 2)
                await TtvIRCClient.SendMessage($"@{user.Name} {result[0]}: mmr:{result[1]}");
        }
        public static async Task OpGG(UserObject user)
        {
            await TtvIRCClient.SendMessage(string.Format(STRINGS.OpGGMessage, user.Name, IllSingleton.Game.SummonerName.Replace('#', '-')));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task GetTreck(UserObject user)
        {
            var result = await StreamElementsAPI.GetCurrentSong().ConfigureAwait(false);
            string output;
            if (result == null)
                output = string.Format(STRINGS.GetTrack404, user.Name);
            else
            {
                var userID = TempDataReader.GetUserIDByTreckID(result.VideoId);
                UserObject uUser = new UserObject();
                if (userID != -1)
                {
                    uUser = await _databaseService.GetUserAsync(userID).ConfigureAwait(false);
                }
                else
                    uUser.Name = "streamelements";
                output = string.Format(STRINGS.GetTrackShow, user.Name, result.Title, result.VideoId, uUser.Name);
            }
            await TtvIRCClient.SendMessage(output);
        }
        public static async Task GetTrackQueue(UserObject user)
        {
            var result = await StreamElementsAPI.GetQueue().ConfigureAwait(false);
            if (result == null)
                await TtvIRCClient.SendMessage(string.Format(STRINGS.GetTrack404, user.Name));
            else
                await TtvIRCClient.SendMessage(string.Join(", ", result.Select(v => v.Title)));
        }
        public static async Task CreateClip(UserObject user)
        {
            var response = await TtvAPI.CreateClip().ConfigureAwait(false);
            if (response != null)
            {
                var clipUrl = response.CreatedClips[0].EditUrl.Remove(response.CreatedClips[0].EditUrl.Length - 5);
                await TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipSuccess, user.Name, clipUrl));
            }
            else
            {
                await TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipERROR, user.Name, "ex"));
            }
        }
        public static async Task FlushChat(UserObject user)
        {
            await TtvAPI.DeleteAllMessages().ConfigureAwait(false);
        }
        public static async Task<UserObject> QuizzMediaReward(UserObject user, string[] UserInput)
        {
            if (user.isMod == 1) return user;
            if (user.QuizPoints > 1)
            {
                if (UserInput.Length < 2)
                {
                    await TtvIRCClient.SendMessage(STRINGS.InputERROR);
                    return user;
                }
                if (await RewardsRedemption.ZakazTrekaReward(user.Name, string.Join(" ", UserInput.Skip(1)), null, null).ConfigureAwait(false))
                {
                    user.QuizPoints -= 2;
                }
            }
            return user;
        }
        public static async Task BanUserForTrack(UserObject user)
        {
            var history = await StreamElementsAPI.GetHistory().ConfigureAwait(false);
            if (history == null) return;
            
            int userID = TempDataReader.GetUserIDByTreckID(history.History[0].Song.VideoId);
            await MediaBlackListWriter.Write(history.History[0].Song.VideoId).ConfigureAwait(false);
            if (userID != -1)
            {
                var uUser = await _databaseService.GetUserAsync(userID).ConfigureAwait(false);
                await TtvAPI.TimeOutUser(uUser, 3600, STRINGS.TimeOutReason_Track).ConfigureAwait(false);
                await UserBlackListWriter.Write(uUser.TwitchID.ToString()).ConfigureAwait(false);
                await TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_chatMessage, user.Name, uUser.Name));
            }
            else
                await TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_DonatedTrack, user.Name));
        }
        public static async Task FindUser(UserObject user, string[] input)
        {
            if (input.Length > 1)
            {
                var Name = StringUtil.GetUserNameFromInput(input[1]);
                if (Name != null)
                {
                    var idFind = await _databaseService.GetUserAsync(Name).ConfigureAwait(false);
                    if (idFind == null)
                    {
                        await TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, Name));
                    }
                    else
                    {
                        await TtvIRCClient.SendMessage($"dbID {idFind.dbID}, ttvID {idFind.TwitchID}, login {idFind.Name}, isSub {idFind.isSub}, isVip {idFind.isVip}, isMod {idFind.isMod}, IsBroadcaster {idFind.IsBroadcaster}, Uval№ {idFind.UvalCon}, messag№ {idFind.messageCon}, roulet_ws {idFind.roulettCon}, Quizz {idFind.QuizPoints}, QuizzT {idFind.QuizTotal}, IsPartner {idFind.isPartner}");
                    }
                }
                else
                    await TtvIRCClient.SendMessage(STRINGS.InputERROR);
            }
        }
        public static async Task DisableReward(UserObject user, string[] input)
        {
            if (input.Length == 1)
            {
                await TtvIRCClient.SendMessage("usage - !disablereward|rewardID(string) or !disablereward|Title(string)|text(string)");
                return;
            }
            if (input.Length == 2)
            {
                await TtvIRCClient.SendMessage($"rewardID - {input[1]}");
                var reward = await TtvAPI.GetReward(input[1]).ConfigureAwait(false);
                if (reward == null)
                    await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                else
                    await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired).ConfigureAwait(false);
            }
        }
        public static async Task CreateReward(UserObject user, string[] args)
        {
            if (args.Length == 6)
            {
                string title = args[1];
                string costStr = args[2];
                string prompt = args[3];
                string enabledStr = args[4];
                string userinputStr = args[5];

                if (int.TryParse(costStr, out int cost) &&
                    bool.TryParse(enabledStr, out bool enabled) &&
                    bool.TryParse(userinputStr, out bool userinput))
                {
                    await TtvIRCClient.SendMessage($"title - {title}, cost - {cost}, prompt - {prompt}, enabled - {enabled}, userinput - {userinput}");
                    var response = await TtvAPI.CreateReward(title, cost, prompt, enabled, userinput).ConfigureAwait(false);
                    if (response != null)
                        await TtvIRCClient.SendMessage(response);
                }
                else
                {
                    if (!int.TryParse(costStr, out _))
                        await TtvIRCClient.SendMessage("Cost must be an integer.");
                    if (!bool.TryParse(enabledStr, out _))
                        await TtvIRCClient.SendMessage("Enabled must be a boolean (true or false).");
                    if (!bool.TryParse(userinputStr, out _))
                        await TtvIRCClient.SendMessage("Userinput must be a boolean (true or false).");
                }
            }
            else
            {
                await TtvIRCClient.SendMessage("Usage: !createreward \"title\" cost \"prompt\" enabled userinput");
            }
        }
        public static async Task DeleteReward(UserObject user, string[] input)
        {
            /*
            try
            {
                string s = input;
                char[] separators = new char[] { '|' };
                string[] subs = s.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (subs.Length > 1)
                {
                    if (subs.Length == 2)
                    {
                        await TtvIRCClient.SendMessage($"ID - {subs[1]}");
                        var reward = await TtvAPI.getReward(subs[1]).ConfigureAwait(false);
                        await TtvAPI.deleteReward(subs[1]).ConfigureAwait(false);
                    }
                    if (subs.Length == 3)
                    {
                        await TtvIRCClient.SendMessage($"Title - {subs[1]}, flag - {subs[2]}");
                        var reward = await TtvAPI.getReward(subs[1], subs[2]).ConfigureAwait(false);
                        await TtvAPI.deleteReward(reward[0]).ConfigureAwait(false);
                    }
                }
                else
                    await TtvIRCClient.SendMessage("usage - !deletereward|rewardID(string) or !deletereward|Title(string)|anytext(string)");
            }
            catch (Exception ex)
            {
                await TtvIRCClient.SendMessage(ex.Message);
                Log.WriteLog(ex, "!deletereward");
            }
            */
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task UpdateReward(UserObject user, string[] args)
        {
            // Check if the total length is 7 (command name + 6 arguments)
            if (args.Length == 7)
            {
                // Extract arguments starting from index 1 (index 0 is the command name)
                string rewardID = args[1];
                string title = args[2];
                string costStr = args[3];
                string promt = args[4];
                string enabledStr = args[5];
                string userinputStr = args[6];

                // Validate and parse cost, enabled, and userinput
                if (int.TryParse(costStr, out int cost) &&
                    bool.TryParse(enabledStr, out bool enabled) &&
                    bool.TryParse(userinputStr, out bool userinput))
                {
                    // Send confirmation message before updating
                    await TtvIRCClient.SendMessage($"rewardID - {rewardID}, title - {title}, cost - {cost}, promt - {promt}, enabled - {enabled}, userinput - {userinput}");
                    // Call the API to update the reward
                    await TtvAPI.UpdateReward(rewardID, title, cost, promt, enabled, userinput).ConfigureAwait(false);
                }
                else
                {
                    // Inform the user about invalid parameter types
                    await TtvIRCClient.SendMessage("Invalid parameters. Ensure cost is an integer and enabled/userinput are booleans.");
                }
            }
            else
            {
                // Show usage with the new syntax, indicating quotes for multi-word arguments
                await TtvIRCClient.SendMessage("Usage: !updatereward rewardID \"title\" cost \"promt\" enabled userinput");
            }
        }
        public static async Task EnableReward(UserObject user, string[] args)
        {
            if (args.Length == 2)
            {
                string rewardID = args[1];
                await TtvIRCClient.SendMessage($"rewardID - {rewardID}");
                var reward = await TtvAPI.GetReward(rewardID).ConfigureAwait(false);
                if (reward == null)
                    await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                else
                    await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else if (args.Length == 3)
            {
                string title = args[1];
                string text = args[2];
                await TtvIRCClient.SendMessage($"Title - {title}");
                var reward = await TtvAPI.GetReward(title, text).ConfigureAwait(false);
                if (reward == null)
                    await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                else
                    await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
            }
            else
            {
                await TtvIRCClient.SendMessage("Usage: !enablereward rewardID or !enablereward \"title\" \"text\"");
            }
        }
        public static async Task InjectSQL(UserObject user, string[] input)
        {
            if (user.Name == IllSingleton.Config.RootUser)
            {
                //try
                //{tv
                //    string s = message;
                //    char[] separators = new char[] { '|' };
                //    string[] subs = s.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                //    if (subs.Length == 3)
                //    {
                //        if (subs[1] == "r")
                //        {
                //            var results = await MySQL.SudoSQLReader(subs[2]).ConfigureAwait(false);
                //            if (results[0].Name != null)
                //            {
                //                foreach (var result in results)
                //                {
                //                    await TtvIRCClient.SendMessage($"dbID {result.dbID} TwitchID {result.TwitchID} Name {result.Name} isSub {result.isSub} isVip {result.isVip} isMod {result.isMod}  IsBroadcaster {result.IsBroadcaster} UvalCon {result.UvalCon} messageCon {result.messageCon} roulettCon {result.roulettCon} roulettCD {result.roulettCD} UvalTimer {result.UvalTimer} banCount {result.banCount} Points {result.Points} IsOnline {result.IsOnline}");
                //                }
                //            }
                //            else
                //            {
                //                client.SendMessage(tChannel, $"{results[0].dbID}");
                //            }
                //        }
                //        if (subs[1] == "nq")
                //        {

                //            var results = await MySQL.SudoSQLNonQuery(subs[2]).ConfigureAwait(false);


                //            client.SendMessage(tChannel, $"Было изменено {results} записей");
                //        }
                //    }
                //    else
                //        client.SendMessage(tChannel, "Не верный формат команды! (!sudo|type(r/nq)|query)");
                //}
                //catch (Exception ex)
                //{
                //    client.SendMessage(tChannel, ex.Message);
                //    Log.WriteLog(ex, "null");
                //}
                await TtvIRCClient.SendMessage("Команда в разработке ага.");
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }
        public static async Task SetAntiBotLvl(UserObject user, string[] input)
        {
            if (input.Length > 1)
            {
                switch (input[1])
                {
                    case "0":
                        IllSingleton.State.AntiBotProtectionLvl = 0;
                        await TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, IllSingleton.State.AntiBotProtectionLvl));
                        break;
                    case "1":
                        IllSingleton.State.AntiBotProtectionLvl = 1;
                        await TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, IllSingleton.State.AntiBotProtectionLvl));
                        break;
                    case "2":
                        IllSingleton.State.AntiBotProtectionLvl = 2;
                        await TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, IllSingleton.State.AntiBotProtectionLvl));
                        break;
                    default:
                        await TtvIRCClient.SendMessage(STRINGS.InputERROR);
                        break;
                }
            }
            else
                await TtvIRCClient.SendMessage(STRINGS.InputERROR);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task SetChatfilterLvl(UserObject user, string[] input)
        {
            if (input.Length == 2)
            {
                switch (input[1])
                {
                    case "0":
                        IllSingleton.State.ChatFilterLvl = 0;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    case "1":
                        IllSingleton.State.ChatFilterLvl = 1;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    case "2":
                        IllSingleton.State.ChatFilterLvl = 2;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    case "3":
                        IllSingleton.State.ChatFilterLvl = 3;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    case "4":
                        IllSingleton.State.ChatFilterLvl = 4;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    case "5":
                        IllSingleton.State.ChatFilterLvl = 5;
                        SaveAppConfig();
                        await TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {IllSingleton.State.ChatFilterLvl}!");
                        break;
                    default:
                        await TtvIRCClient.SendMessage(STRINGS.InputERROR);
                        break;
                }
            }
            else
                await TtvIRCClient.SendMessage("Допустимые значения: 0, 1, 2, 3, 4. 0 - бездействие. 1 - удаление сообщения. 2 - удаления и оповещение модераторов. 3 - таймаут на сутки. 4 - таймаут на неделю. 5 - бан. <<!chatfilter 3>>");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task GetAllRewards(UserObject user)
        {
            var rewards = await TtvAPI.GetAllRewards().ConfigureAwait(false);
            if (rewards == null) return;
            int rewardsCount = rewards.Data.Length;
            string rewardsTitle = string.Join(" | ", rewards.Data.Select(r => r.Title));
            string message = string.Format(STRINGS.GetAllReward_chatMessage, rewardsCount, rewardsTitle);
            await TtvIRCClient.SendMessage(message);
        }
        public static async Task StartCronTask(UserObject user, string input)
        {
            QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
            var inputParts = input.Split(' ');
            if (inputParts.Length <= 4)
            {
                await TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR);
            }
            else
            {
                var taskName = inputParts[1];
                var triggerName = inputParts[2];
                var cronExpression = string.Join(" ", inputParts.Skip(3));
                if (!quartzBackgroundTaskManager.IsCronExpressionValid(cronExpression))
                {
                    await TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR2);
                }
                else
                {
                    await quartzBackgroundTaskManager.UpdateJobSchedule(taskName, triggerName, cronExpression);
                    await TtvIRCClient.SendMessage(STRINGS.StartCronTaskSuccess);
                }
            }
        }
        public static async Task GetAllJobs(UserObject user)
        {
            QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
            var jobs = await quartzBackgroundTaskManager.GetAllJobsNames().ConfigureAwait(false);
            await TtvIRCClient.SendMessage(jobs);
        }
        public static async Task ChangeLanguage(UserObject user, string[] input)
        {
            switch (input[1])
            {
                case "ru":
                    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");
                    await TtvIRCClient.SendMessage(STRINGS.language);
                    break;
                case "eng":
                    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
                    await TtvIRCClient.SendMessage(STRINGS.language);
                    break;
                case "fr":
                    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("fr-FR");
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("fr-FR");
                    await TtvIRCClient.SendMessage(STRINGS.language);
                    break;
                case "ja":
                    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ja-JP");
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ja-JP");
                    await TtvIRCClient.SendMessage(STRINGS.language);
                    break;
                case "ko":
                    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ko_KR");
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ko_KR");
                    await TtvIRCClient.SendMessage(STRINGS.language);
                    break;
                default:
                    break;
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task TestingMethod(UserObject user)
        {
            Console.WriteLine("test");
            //await TtvIRCClient.OnStreamUp();
            //test stuff here
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task<string> GetGPTResponce(string message, string userName = null)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return null;
            /* string responce = await ChatGPT.GetGptResponce(userName + " " + message).ConfigureAwait(false);
             if (!responce.Contains("maximum context length"))
                     if (IllChatFilters.ZapCheck(responce, "ChatGPT"))
                         return "900";
                     else
                         return responce;
             else
             {
                 ChatGPT.CreateNewChat();
                 return await GetGPTResponce(message, userName).ConfigureAwait(false);
             }*/
        }
        public static async Task ToggleDebug(UserObject user)
        {
            if (IllSingleton.State.Debug) IllSingleton.State.Debug = false;
            else IllSingleton.State.Debug = true;
            await TtvIRCClient.SendMessage($"Debug mode is {IllSingleton.State.Debug}");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task ToggleSilentMode(UserObject user)
        {
            if (IllSingleton.State.IsSilent) IllSingleton.State.IsSilent = false;
            else IllSingleton.State.IsSilent = true;
            await TtvIRCClient.SendMessage($"SilentMode mode is {IllSingleton.State.IsSilent}");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task Ttvgg(UserObject user)
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
            var results = await Task.WhenAll(taskList).ConfigureAwait(false);
            var roulettCD = user.roulettCD - currentUnixTime;
            roulettCD = roulettCD < 0 ? 0 : roulettCD;
            TimeSpan time = TimeSpan.FromSeconds(roulettCD);
            await TtvIRCClient.SendMessage($"@{user.Name}, твой винстрик в рулетке {user.roulettCon} {IntUtil.CalculateTopPercentage(results[0])}, " +
                $"всего ты отправил {user.messageCon} сообщений {IntUtil.CalculateTopPercentage(results[2])}, " +
                $"ты был в увале {user.UvalCon} раз {IntUtil.CalculateTopPercentage(results[1])}, " +
                $"у тебя есть {user.QuizPoints} баллов квиза {IntUtil.CalculateTopPercentage(results[4])}, " +
                $"за все время ты набрал {user.QuizTotal} баллов квиза {IntUtil.CalculateTopPercentage(results[5])}, " +
                $"у тебя есть {user.Points} поинтов {IntUtil.CalculateTopPercentage(results[3])}, " +
                $"кулдаун у твоей рулетки продлится еще {time:hh\\:mm\\:ss}");

        }
        public static async Task RemoveUserFromBlacklist(UserObject user, string[] input)
        {
            if (input.Length != 2)
            {
                await TtvIRCClient.SendMessage(STRINGS.InputERROR);
                return;
            }
            var UserToUnban = await _databaseService.GetUserAsync(input[1]).ConfigureAwait(false);
            if (UserToUnban.dbID == -404)
            {
                await TtvIRCClient.SendMessage(STRINGS.FindUser_ERROR404);
                return;
            }
            var path = IllSkillzBotMain.GetDataPath().uniquePath;
            path = Path.Combine(path, IllSingleton.Config.FilePaths.UserBlacklistFileName);
            if (FileManipulator.DeleteLineFromFile(path, UserToUnban.TwitchID.ToString()))
            {
                IllChatFilters.EditUserBlackList(UserToUnban.TwitchID.ToString());
                await TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} удален из черного списка");
            }
            else
                await TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} не был найден в черном списке");

        }
        public static async Task AddTowhiteList(UserObject user, string[] input)
        {
            if (input.Length != 2)
            {
                await TtvIRCClient.SendMessage(STRINGS.InputERROR).ConfigureAwait(false);
                return;
            }
            var path = IllSkillzBotMain.GetDataPath().sharedPath;
            path = Path.Combine(path, IllSingleton.Config.FilePaths.DicWhiteListFileName);
            FileManipulator.AddLineToFile(path, input[1]);
            IllChatFilters.AddToWhiteList(input[1]);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task AddSubscription(UserObject user)
        {
            await TtvIRCClient.SendMessage(AddSub.NewPurchase().ToString());
            SubCheck.RunChecker();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task CheckSubscription(UserObject user)
        {
            if (SubCheck.RunChecker())
                await TtvIRCClient.SendMessage("Valid!");
            else
                await TtvIRCClient.SendMessage("Expired!");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        public static async Task GetMods(UserObject user)
        {
            var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
            string Moderators = "";
            foreach (var mod in mods)
                Moderators += mod.UserLogin + ": " + mod.UserId + ". ";
            await TtvIRCClient.SendMessage(Moderators);
        }
        public static async Task Sheptun(UserObject user)
        {
            var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
            foreach (var mod in mods)
            {
                await TtvAPI.SendWhisper(mod.UserId, "Тестовый шептун").ConfigureAwait(false);
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        public static async Task getJobs(UserObject user)
        {
            await TtvIRCClient.SendMessage(await QuartzBackgroundTaskManager.GetRunningJobs().ConfigureAwait(false));
        }

        public static async Task Ludka (UserObject user, string[] input)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1)            
                if (input.Length > 1)
                {
                    _ludka = input[1];
                    return;
                }            
            await TtvIRCClient.SendMessage(_ludka).ConfigureAwait(false);
        }
        public static async Task Ping()
        {
            await TtvIRCClient.SendMessage("pong");
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}