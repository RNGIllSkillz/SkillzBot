using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.API.Riot;
using System.Linq;
using SkillzBot.MYSQL;
using SkillzBot.Writers;
using F23.StringSimilarity;
using SkillzBot.API.MMR;
using SkillzBot.API.StreamElements;
using SkillzBot.Readers;
using SkillzBot.TtvClient.TTVRewards;
using System.Globalization;
using SkillzBot.IllSTRINGS;
using IllSkillzBot;
using SkillzBot.API.OpenAI;
using System.IO;
using SkillzBot.SubUtils;
using RiotSharp.Endpoints.StaticDataEndpoint.Version;
using TwitchLib.Client;

namespace SkillzBot.IllSkillzBot
{
    internal class IllCommands
    {        
        private static double helpCD = 0;
        private static double rtoppCD = 0;
        private static double getmmrCD = 0;
        private static double lpCD = 0;
        private static double matchCD = 0;
        private static double opggCD = 0;
        private static double treckCD = 0;
        private static double banCD = 0;
        private static double treckQCD = 0;
        private static double ttvggCD = 0;

        private static readonly TimeSpan ClipCooldown = TimeSpan.FromSeconds(30);
        private static DateTimeOffset LastClipTime = DateTimeOffset.MinValue;

        private static readonly IllSingleton singleton = IllSingleton.GetInstance();

        readonly static List<string> popMessages = new List<string>();
        public static void Help(UserObject user)
        {
            int secCD = 300;
            if (Convert.ToBoolean(user.isSub)) secCD = 300;
            if (Convert.ToBoolean(user.isVip)) secCD = 30;
            if (Convert.ToBoolean(user.isMod) || user.Name == singleton.rootUser) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - helpCD >= secCD)
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.HelpMessage, user.Name));
                helpCD = DateTimeOffset.Now.ToUnixTimeSeconds();
            }
        }
        public static async Task Points(UserObject user)
        {
            var pos = await MySQL.GetTopPos(user.Name, "Points").ConfigureAwait(false);
            var QPos = await MySQL.GetTopPos(user.Name, "QuizPoints").ConfigureAwait(false);
            var QtPos = await MySQL.GetTopPos(user.Name, "QuizTotal").ConfigureAwait(false);
            TtvIRCClient.SendMessage(string.Format(STRINGS.PointsMessage, user.Name, user.Points, pos[0], pos[1], user.QuizPoints, QPos[0], QPos[1], user.QuizTotal, QtPos[0], QtPos[1]));
        }
        public static void Prediction(UserObject user, string[] command)
        {
            if (!IllAccess.Low(user)) return;
            if (command.Length > 1)
            {
                switch (command[1])
                {
                    case "off":
                        singleton.autoPred = false;
                        TtvIRCClient.SendMessage($"@{user.Name} Автоставки Выключены!");
                        Log.WriteLog(null, $"{user.Name} Выключил ставки!");
                        break;

                    case "on":
                        singleton.autoPred = true;
                        TtvIRCClient.SendMessage($"@{user.Name} Автоставки Включены!");
                        Log.WriteLog(null, $"{user.Name} Включил ставки!");
                        break;

                    default:
                        TtvIRCClient.SendMessage($"@{user.Name} Не правильный параметр! (on/off)");
                        break;
                }
            }
            else
                TtvIRCClient.SendMessage($"{user.Name} Не правильная команда! (!prediction on/off)");
        }
        public static async Task LpCommand(UserObject user, string[] command)
        {
            if (command.Length > 1)
            {
                if (!IllAccess.Low(user)) return;
                if (!singleton.inAmatch)
                    {
                        switch (command.Last())
                        {
                            case "ru":
                            case "euw":
                            case "na":
                                break;
                            default:
                                TtvIRCClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
                                return;
                        }
                        var temp = StringUtil.RemoveWhitespace(StringUtil.GetCommandFromUserInput(command.Take(command.Count() - 1).ToArray()));
                        var result = await RiotAPI.UpdateSummonerByNameAsync(temp, command.Last()).ConfigureAwait(false);
                        if (result == null)
                        {
                            singleton.SUMMONER_NAME = temp;
                            singleton.SummonerRegion = command.Last();
                            RiotAPI.UpdateRegion(singleton.SummonerRegion);

                            var Rank = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                            if (Rank != null)
                            {
                                if (int.TryParse(Rank[1], out int buffStartLP))
                                    singleton.startLP = buffStartLP;
                                else
                                    singleton.startLP = 0;
                                singleton.elo = Rank[0];
                                singleton.tier = Rank[2];
                            }
                            SaveGameStats();
                            SaveAppConfig();
                            await ShowLPAsync(user.Name).ConfigureAwait(false);
                        }
                        else
                        {
                            TtvIRCClient.SendMessage($"ERROR: {result}");
                        }
                    }
                    else
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.LPInaMatch, user.Name));
                    }                
            }
            else
            {
                if (user.isVip == 1 || IllAccess.Low(user))
                    lpCD = 0;
                if (DateTimeOffset.Now.ToUnixTimeSeconds() - lpCD >= 30)
                {
                    lpCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                    await ShowLPAsync(user.Name).ConfigureAwait(false);
                }
            }
        }
        public static async Task RouletteTop(UserObject user)
        {
            int secCD = 600;
            if (user.isSub == 1) secCD = 300;
            if (user.isVip == 1) secCD = 100;
            if (IllAccess.Low(user)) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - rtoppCD >= secCD)
            {
                rtoppCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                await TopRulete().ConfigureAwait(false);
            }
        }
        
        public static async Task AddVIP(UserObject user, string[] UserInput)
        {
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await MySQL.GetUser(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.AddChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    TtvIRCClient.SendMessage(string.Format(STRINGS.AddVIPSuccess, aUser.Name));
                }
                else
                    TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }
        public static async Task DeleteVIP(UserObject user, string[] UserInput)
        {
            if (!IllAccess.Root(user)) return;
            if (UserInput.Length == 2)
            {
                var aUser = await MySQL.GetUser(UserInput[1]).ConfigureAwait(false);
                if (aUser.dbID != -404)
                {
                    await TtvAPI.DeleteChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
                    TtvIRCClient.SendMessage(string.Format(STRINGS.DeleteVIPSuccess, aUser.Name));
                }
                else
                    TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
            }
            else
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
        }
        
        public static async Task<TrackUser> TrackUser(UserObject user, string[] UserInput)
        {
            if (user.Name == singleton.rootUser)
            {
                if (UserInput.Length > 1)
                {
                    var result = await MySQL.TrackUser(UserInput[1].ToLower()).ConfigureAwait(false);
                    if (result != null)
                    {
                        TtvIRCClient.SendMessage(string.Format(STRINGS.TrackUserSuccess, UserInput[1], result.Count));
                        string outDbs = "";
                        foreach (var r in result.DBName)
                        {
                            outDbs += r + ", ";
                            if (outDbs.Length == 450)
                            {
                                TtvIRCClient.SendMessage(outDbs);
                                outDbs = "";
                            }
                        }
                        TtvIRCClient.SendMessage(outDbs);
                    }
                    else
                    {
                        TtvIRCClient.SendMessage("ERROR");
                    }
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
                switch (singleton.ChatFilterLvl)
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
                        string ModsZapMsg = $"Найдена запретка на канале {singleton.ChannelName} от пользователя @{user.Name}. Модерам на проверку";
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
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync(summoner,region).ConfigureAwait(false);
            if (rank != null)
                foreach (var mType in rank)
                {
                    if (mType.QueueType == "RANKED_SOLO_5x5")
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
                            int WR = (int)Math.Ceiling((double)(mType.Wins * 100) / (double)((mType.Wins + mType.Losses)));
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
                    if (mType.QueueType == "RANKED_SOLO_5x5")
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
                            TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPPromo, sender, mType.SummonerName, subs[0], promoString));
                        }
                        else
                        {
                            int WR = (int)Math.Ceiling((double)(mType.Wins * 100) / (double)((mType.Wins + mType.Losses)));
                            TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLP, sender, mType.SummonerName, mType.Tier, mType.Rank, mType.LeaguePoints, WR, singleton.numGames, singleton.numWins, singleton.numLoose, singleton.earnedLP));
                        }
                    }
                }
            else
                TtvIRCClient.SendMessage("Riot API error");
            if (!ranked)
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPCalibration, sender, singleton.SUMMONER_NAME, singleton.numGames, singleton.numWins, singleton.numLoose, singleton.earnedLP));
            }
        }
        public static async Task GetMatchHistory(UserObject user)
        {
            int secCD = 120;
            if (user.isSub == 1) secCD = 60;
            if (user.isVip == 1) secCD = 30;
            if (user.isMod == 1 || user.Name == singleton.rootUser) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - matchCD >= secCD)
            {
                matchCD = DateTimeOffset.Now.ToUnixTimeSeconds();
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
                                TtvIRCClient.SendMessage(string.Format(STRINGS.MatchHistoryMessage, user.Name, Champ.Name, Participant.Kills, Participant.Deaths, Participant.Assists, role, type, win));
                            }
                        }
                    }
                }
            }
        }
        public static async Task TopRulete()
        {
            var result = await MySQL.TOP("rtop").ConfigureAwait(false);
            if (result != null && result.Count >= 3)
            {
                TtvIRCClient.SendMessage(string.Format
                 (
                    STRINGS.Top3Roulette,
                    result[0].Name, result[0].roulettCon, IntUtil.RulProbability(result[0].roulettCon, 80),
                    result[1].Name, result[1].roulettCon, IntUtil.RulProbability(result[1].roulettCon, 80),
                    result[2].Name, result[2].roulettCon, IntUtil.RulProbability(result[2].roulettCon, 80)
                 ));
            }
            else
            {
                Log.WriteLog(null, "Cant get 3 users at TopRulete");
            }
        }
        public static async Task GetTopChat(UserObject user)
        {
            if (user.IsBroadcaster == 1 || user.isMod == 1 || user.Name == singleton.rootUser)
            {
                var result = await MySQL.TOP("top").ConfigureAwait(false);
                if (result != null && result.Count >= 3)
                {
                    TtvIRCClient.SendMessage(string.Format
                                (
                                 STRINGS.Top3Chat,
                                 result[0].Name, result[0].messageCon,
                                 result[1].Name, result[1].messageCon,
                                 result[2].Name, result[2].messageCon
                                ));
                }
                else
                {
                    Log.WriteLog(null, "Cant get 3 users at GetTopChat");
                }
            }
        }
        public static void SaveGameStats()
        {
            GameStatsWriter.Write
                (
                $"{singleton.startLP} " +
                $"{singleton.elo} " +
                $"{singleton.earnedLP} " +
                $"{singleton.numLoose} " +
                $"{singleton.numGames} " +
                $"{singleton.numWins} " +
                $"{singleton.tier}"
                );
        }
        public static void SaveAppConfig()
        {
            BotConfigWriter.Write();
        }
        public static void TypeInChat(string message)
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
                        var sim1 = (jw.Distance(popMessage, checkpop));
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
                TtvIRCClient.SendMessage(sendMess);
            }
        }
        public static async Task StartQuizz()
        {
            await IllGames.Quizz(true).ConfigureAwait(false);
        }
        public static async Task GetMMR(UserObject user)
        {
            int secCD = 30;
            if
            (
                user.isSub == 1 ||
                user.isMod == 1 ||
                user.IsBroadcaster == 1 ||
                user.Name == singleton.rootUser
            )
                secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - getmmrCD >= secCD)
            {
                getmmrCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                var result = await MyLOLMMRApi.GetMMR(singleton.SUMMONER_NAME).ConfigureAwait(false);
                if (result.Count == 2)
                    TtvIRCClient.SendMessage($"@{user.Name} {result[0]}: mmr:{result[1]}");
            }
        }
        public static void OpGG(UserObject user)
        {
            int secCD = 30;
            if (user.isSub == 1) secCD = 15;
            if (user.isVip == 1) secCD = 5;
            if (user.isMod == 1 || user.Name == singleton.rootUser) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - opggCD >= secCD)
            {
                opggCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                TtvIRCClient.SendMessage(string.Format(STRINGS.OpGGMessage, user.Name, singleton.SUMMONER_NAME));
            }
        }
        public static async Task GetTreck(UserObject user)
        {            
            int secCD = 10;
            if (user.isSub == 1) secCD = 10;
            if (user.isVip == 1) secCD = 5;
            if (user.isMod == 1 || user.Name == singleton.rootUser) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - treckCD >= secCD)
            {
                treckCD = DateTimeOffset.Now.ToUnixTimeSeconds();
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
                        uUser = await MySQL.GetUser(userID).ConfigureAwait(false);
                    }
                    else
                        uUser.Name = "streamelements";
                    output = string.Format(STRINGS.GetTrackShow, user.Name, result.Title, result.VideoId, uUser.Name);
                }
                TtvIRCClient.SendMessage(output);
            }
        }
        public static async Task GetTrackQueue(UserObject user)
        {
            int secCD = 60;
            if (user.isSub == 1) secCD = 30;
            if (user.isVip == 1) secCD = 15;
            if (user.isMod == 1 || user.Name == singleton.rootUser) secCD = 0;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - treckQCD >= secCD)
            {
                treckQCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                var result = await StreamElementsAPI.GetQueue().ConfigureAwait(false);
                if (result == null)
                    TtvIRCClient.SendMessage(string.Format(STRINGS.GetTrack404, user.Name));
                else
                    TtvIRCClient.SendMessage(String.Join(", ", result.Select(v => v.Title)));
            }
        }
        public static async Task CreateClip(UserObject user)
        {
            var timeSinceLastClip = DateTimeOffset.Now - LastClipTime;
            if (timeSinceLastClip < ClipCooldown)
            {
                return;
            }
            LastClipTime = DateTimeOffset.Now;
            var response = await TtvAPI.CreateClip().ConfigureAwait(false);
            if (response != null)
            {
                var clipUrl = response.CreatedClips[0].EditUrl.Remove(response.CreatedClips[0].EditUrl.Length - 5);
                TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipSuccess, user.Name, clipUrl));
            }
            else
            {
                TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipERROR, user.Name, "ex"));
            }
        }
        public static async Task FlushChat(UserObject user)
        {
            if (user.Name == singleton.rootUser)
            {
                await TtvAPI.DeleteAllMessages().ConfigureAwait(false);
            }
        }
        public static async Task<UserObject> QuizzMediaReward(UserObject user, string[] UserInput)
        {
            if (user.isMod == 1) return user;
            if (user.QuizPoints > 1)
            {
                if (UserInput.Length < 2)
                {
                    TtvIRCClient.SendMessage(STRINGS.InputERROR);
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
            if (user.isMod == 1 || user.Name == singleton.rootUser || user.IsBroadcaster == 1)
            {
                if (DateTimeOffset.Now.ToUnixTimeSeconds() - banCD >= 30)
                {
                    banCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                    var history = await StreamElementsAPI.GetHistory().ConfigureAwait(false);
                    if (history == null)
                    {
                        //ERROR getting history
                        return;
                    }
                    int userID = TempDataReader.GetUserIDByTreckID(history.History[0].Song.VideoId);                    
                    MediaBlackListWriter.Write(history.History[0].Song.VideoId);                    
                    if (userID != -1)
                    {
                        var uUser = await MySQL.GetUser(userID).ConfigureAwait(false);
                        await TtvAPI.TimeOutUser(uUser, 3600, STRINGS.TimeOutReason_Track).ConfigureAwait(false);                        
                        UserBlackListWriter.Write(uUser.TwitchID.ToString());                        
                        TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_chatMessage, user.Name, uUser.Name));
                    }
                    else
                        TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_DonatedTrack, user.Name));
                }
                else
                    TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_CoolDownmsg, user.Name, DateTimeOffset.Now.ToUnixTimeSeconds() - banCD));
            }
        }
        public static async Task FindUser(UserObject user, string[] input)
        {
            if (user.Name == singleton.rootUser)
            {
                if (input.Length > 1)
                {
                    var Name = StringUtil.GetUserNameFromInput(input[1]);
                    if (Name != null)
                    {
                        var idFind = await MySQL.GetUser(Name).ConfigureAwait(false);
                        if (idFind == null)
                        {
                            TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, Name));
                        }
                        else
                        {
                            TtvIRCClient.SendMessage($"dbID {idFind.dbID}, ttvID {idFind.TwitchID}, login {idFind.Name}, isSub {idFind.isSub}, isVip {idFind.isVip}, isMod {idFind.isMod}, IsBroadcaster {idFind.IsBroadcaster}, Uval№ {idFind.UvalCon}, messag№ {idFind.messageCon}, roulet_ws {idFind.roulettCon}, Quizz {idFind.QuizPoints}, QuizzT {idFind.QuizTotal}, IsPartner {idFind.isPartner}");
                        }
                    }
                    else
                        TtvIRCClient.SendMessage(STRINGS.InputERROR);
                }
            }
        }
        public static async Task DisableReward(UserObject user, string[] input)
        {
            if (user.Name == singleton.rootUser)
            {
                if (input.Length == 1)
                {
                    TtvIRCClient.SendMessage("usage - !disablereward|rewardID(string) or !disablereward|Title(string)|text(string)");
                    return;
                }
                if (input.Length == 2)
                {
                    TtvIRCClient.SendMessage($"rewardID - {input[1]}");
                    var reward = await TtvAPI.GetReward(input[1]).ConfigureAwait(false);
                    if (reward == null)
                        TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                    else
                        await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
                }
            }
        }
        public static async Task CreateReward(UserObject user, string input)
        {
            if (user.Name == singleton.rootUser)
            {
                char[] separators = new char[] { '|' };
                string[] subs = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (subs.Length == 6)
                {
                    TtvIRCClient.SendMessage($"title - {subs[1]},  cost - {subs[2]},  promt - {subs[3]},  enabled - {subs[4]},  userinput - {subs[5]}");                                     
                    var responce = await TtvAPI.CreateReward(subs[1], int.Parse(subs[2]), subs[3], Convert.ToBoolean(subs[4]), Convert.ToBoolean(subs[5])).ConfigureAwait(false);
                    if (responce != null)
                        TtvIRCClient.SendMessage(responce);
                }
                else
                {
                    TtvIRCClient.SendMessage("usage - !createreward|title(string)|cost(string)|promt(string)|enabled(bool)|userinput(bool)");
                }
            }
        }
        public static async Task DeleteReward(UserObject user, string[] input)
        {
            if (user.Name == singleton.rootUser)
            {
                //try
                //{
                //    string s = message;
                //    char[] separators = new char[] { '|' };
                //    string[] subs = s.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                //    if (subs.Length > 1)
                //    {
                //        if (subs.Length == 2)
                //        {
                //            client.SendMessage(tChannel, $"ID - {subs[1]}");
                //            var reward = await TtvAPI.getReward(subs[1]).ConfigureAwait(false);
                //            await TtvAPI.deleteReward(subs[1]).ConfigureAwait(false);
                //        }
                //        if (subs.Length == 3)
                //        {
                //            client.SendMessage(tChannel, $"Title - {subs[1]}, flag - {subs[2]}");
                //            var reward = await TtvAPI.getReward(subs[1], subs[2]).ConfigureAwait(false);
                //            await TtvAPI.deleteReward(reward[0]).ConfigureAwait(false);
                //        }
                //    }
                //    else
                //        client.SendMessage(tChannel, "usage - !deletereward|rewardID(string) or !deletereward|Title(string)|anytext(string)");
                //}
                //catch (Exception ex)
                //{
                //    client.SendMessage(tChannel, ex.Message);
                //    Log.WriteLog(ex, "!deletereward");
                //}
                await Task.Delay(10);
                TtvIRCClient.SendMessage("Команда не доступна.");
            }
        }
        public static async Task UpdateReward(UserObject user, string input)
        {
            if (user.Name == singleton.rootUser)
            {
                char[] separators = new char[] { '|' };
                string[] subs = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (subs.Length == 7)
                {
                    TtvIRCClient.SendMessage($"rewardID - {subs[1]}, title - {subs[2]},  cost - {subs[3]},  promt - {subs[4]},  enabled - {subs[5]},  userinput - {subs[6]}");
                    await TtvAPI.UpdateReward(subs[1], subs[2], Convert.ToInt32(subs[3]), subs[4], Convert.ToBoolean(subs[5]), Convert.ToBoolean(subs[6])).ConfigureAwait(false);
                }
                else
                {
                    TtvIRCClient.SendMessage("usage - !updatereward|rewardID(string)|title(string)|cost(string)|promt(string)|enabled(bool)|userinput(bool)");
                }
            }
        }
        public static async Task EnableReward(UserObject user, string input)
        {
            if (user.Name == singleton.rootUser)
            {
                char[] separators = new char[] { '|' };
                string[] subs = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (subs.Length > 1)
                {
                    if (subs.Length == 2)
                    {
                        TtvIRCClient.SendMessage($"rewardID - {subs[1]}");
                        var reward = await TtvAPI.GetReward(subs[1]).ConfigureAwait(false);
                        if (reward == null)
                            TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                        else
                            await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
                    }
                    if (subs.Length == 3)
                    {
                        TtvIRCClient.SendMessage($"Title - {subs[1]}");
                        var reward = await TtvAPI.GetReward(subs[1], subs[2]).ConfigureAwait(false);
                        if (reward == null)
                            TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
                        else
                            await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
                    }
                }
                else
                    TtvIRCClient.SendMessage("usage - !enablereward|rewardID(string) or !enablereward|Title(string)|text(string)");
            }
        }
        public static async Task InjectSQL(UserObject user, string[] input)
        {
            if (user.Name == singleton.rootUser)
            {
                //try
                //{
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
                //                    TtvIRCClient.SendMessage($"dbID {result.dbID} TwitchID {result.TwitchID} Name {result.Name} isSub {result.isSub} isVip {result.isVip} isMod {result.isMod}  IsBroadcaster {result.IsBroadcaster} UvalCon {result.UvalCon} messageCon {result.messageCon} roulettCon {result.roulettCon} roulettCD {result.roulettCD} UvalTimer {result.UvalTimer} banCount {result.banCount} Points {result.Points} IsOnline {result.IsOnline}");
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
                await Task.Delay(10);
                TtvIRCClient.SendMessage("Команда не доступна.");
            }
        }
        public static void SetAntiBotLvl(UserObject user, string[] input)
        {
            if (user.Name == singleton.rootUser)
            {
                if (input.Length > 1)
                {
                    switch (input[1])
                    {
                        case "0":
                            singleton.AntiBotProtectionLvL = 0;
                            TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, singleton.AntiBotProtectionLvL));
                            break;
                        case "1":
                            singleton.AntiBotProtectionLvL = 1;
                            TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, singleton.AntiBotProtectionLvL));
                            break;
                        case "2":
                            singleton.AntiBotProtectionLvL = 2;
                            TtvIRCClient.SendMessage(string.Format(STRINGS.AntiBotLvl, user.Name, singleton.AntiBotProtectionLvL));
                            break;
                        default:
                            TtvIRCClient.SendMessage(STRINGS.InputERROR);
                            break;
                    }
                }
                else
                    TtvIRCClient.SendMessage(STRINGS.InputERROR);
            }
        }
        public static void SetChatfilterLvl(UserObject user, string[] input)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1 || user.Name == singleton.rootUser)
            {
                if (input.Length == 2)
                {
                    switch (input[1])
                    {
                        case "0":
                            singleton.ChatFilterLvl = 0;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!"); 
                            break;
                        case "1":
                            singleton.ChatFilterLvl = 1;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!");
                            break;
                        case "2":
                            singleton.ChatFilterLvl = 2;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!");
                            break;
                        case "3":
                            singleton.ChatFilterLvl = 3;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!");
                            break;
                        case "4":
                            singleton.ChatFilterLvl = 4;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!");
                            break;
                        case "5":
                            singleton.ChatFilterLvl = 5;
                            SaveAppConfig();
                            TtvIRCClient.SendMessage($"Уровень модерации чата установлен в значение {singleton.ChatFilterLvl}!");
                            break;
                        default:
                            TtvIRCClient.SendMessage(STRINGS.InputERROR);
                            break;
                    }
                }
                else
                    TtvIRCClient.SendMessage("Допустимые значения: 0, 1, 2, 3, 4. 0 - бездействие. 1 - удаление сообщения. 2 - удаления и оповещение модераторов. 3 - таймаут на сутки. 4 - таймаут на неделю. 5 - бан. <<!chatfilter 3>>");
            }
        }
        public static async Task GetAllRewards(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            var rewards = await TtvAPI.GetAllRewards().ConfigureAwait(false);
            if (rewards == null) return;
            int rewardsCount = rewards.Data.Length;
            string rewardsTitle = string.Join(" | ", rewards.Data.Select(r => r.Title));
            string message = string.Format(STRINGS.GetAllReward_chatMessage, rewardsCount, rewardsTitle);
            TtvIRCClient.SendMessage(message);
        }
        public static async Task StartCronTask (UserObject user, string input)
        {
            if (user.Name == singleton.rootUser)
            {
                QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
                var inputParts = input.Split(' ');
                if (inputParts.Length <= 4)
                {
                    TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR);
                }
                else
                {
                    var taskName = inputParts[1];
                    var triggerName = inputParts[2];
                    var cronExpression = string.Join(" ", inputParts.Skip(3));
                    if (!quartzBackgroundTaskManager.IsCronExpressionValid(cronExpression))
                    {
                        TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR2);
                    }
                    else
                    {
                        await quartzBackgroundTaskManager.UpdateJobSchedule(taskName, triggerName, cronExpression);
                        TtvIRCClient.SendMessage(STRINGS.StartCronTaskSuccess);
                    }
                }
            }
        }
        public static async Task GetAllJobs(UserObject user)
        {
            if (user.Name == singleton.rootUser)
            {
                QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
                var jobs = await quartzBackgroundTaskManager.GetAllJobsNames().ConfigureAwait(false);
                TtvIRCClient.SendMessage(jobs);
            }
        }
        public static void ChangeLanguage(UserObject user, string[] input)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1 || user.Name == singleton.rootUser)
            {              
                switch (input[1])
                {
                    case "ru":
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");
                        TtvIRCClient.SendMessage(STRINGS.language);
                        break;
                    case "eng":
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
                        TtvIRCClient.SendMessage(STRINGS.language);
                        break;
                    case "fr":
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("fr-FR");
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("fr-FR");
                        TtvIRCClient.SendMessage(STRINGS.language);
                        break;
                    case "ja":
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ja-JP");
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ja-JP");
                        TtvIRCClient.SendMessage(STRINGS.language);
                        break;
                    case "ko":
                        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ko_KR");
                        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ko_KR");
                        TtvIRCClient.SendMessage(STRINGS.language);
                        break;
                    default:
                        break;
                }
            }
        }
        public static async Task TestingMethod(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            Console.WriteLine("test");
            //await TtvIRCClient.OnStreamUp();
            //test stuff here           
        }
        public static void ReconnectToPubSub (UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            IllSkillzBotMain.PubSubReconnect();
        }
        public static async Task<string> GetGPTResponce(string message, string userName = null)
        {
            string responce = await ChatGPT.GetGptResponce(userName + " " + message).ConfigureAwait(false);
            if (!responce.Contains("maximum context length"))
                    if (IllChatFilters.ZapCheck(responce, "ChatGPT"))
                        return "900";
                    else
                        return responce;
            else
            {
                ChatGPT.CreateNewChat();
                return await GetGPTResponce(message, userName).ConfigureAwait(false);
            }
        }
        public static void ToggleDebug(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            if (singleton.debug)
                singleton.debug = false;
            else
                singleton.debug = true;
            TtvIRCClient.SendMessage($"Debug mode is {singleton.debug}");
        }        
        public static void ToggleSilentMode(UserObject user)
        {
            if (user.Name != singleton.rootUser || user.isMod != 1 || user.IsBroadcaster != 1) return;
            if (singleton.IsSilent)
                singleton.IsSilent = false;
            else
                singleton.IsSilent = true;
            TtvIRCClient.SendMessage($"SilentMode mode is {singleton.IsSilent}");
        }
        public static async Task Ttvgg(UserObject user)
        {
            int secCD = 30;
            if (user.isSub == 1) secCD = 15;
            if (user.isVip == 1) secCD = 0;
            if (user.isMod == 1 || user.Name == singleton.rootUser) secCD = 0;
            long currentUnixTime = DateTimeOffset.Now.ToUnixTimeSeconds();
            if (currentUnixTime - ttvggCD >= secCD)
            {
                if (user.isMod != 1 || user.Name != singleton.rootUser)
                    ttvggCD = currentUnixTime;
                var taskList = new List<Task<int[]>>
                {
                    MySQL.GetTopPos(user.Name, "roulettCon"),
                    MySQL.GetTopPos(user.Name, "UvalCon"),
                    MySQL.GetTopPos(user.Name, "messageCon"),
                    MySQL.GetTopPos(user.Name, "Points"),
                    MySQL.GetTopPos(user.Name, "QuizPoints"),
                    MySQL.GetTopPos(user.Name, "QuizTotal")
                };
                var results = await Task.WhenAll(taskList).ConfigureAwait(false);
                var roulettCD = user.roulettCD - currentUnixTime;
                roulettCD = roulettCD < 0 ? 0 : roulettCD;
                TimeSpan time = TimeSpan.FromSeconds(roulettCD);
                TtvIRCClient.SendMessage($"@{user.Name}, твой винстрик в рулетке {user.roulettCon} {IntUtil.CalculateTopPercentage(results[0])}, " +
                    $"всего ты отправил {user.messageCon} сообщений {IntUtil.CalculateTopPercentage(results[2])}, " +
                    $"ты был в увале {user.UvalCon} раз {IntUtil.CalculateTopPercentage(results[1])}, " +
                    $"у тебя есть {user.QuizPoints} баллов квиза {IntUtil.CalculateTopPercentage(results[4])}, " +
                    $"за все время ты набрал {user.QuizTotal} баллов квиза {IntUtil.CalculateTopPercentage(results[5])}, " +
                    $"у тебя есть {user.Points} поинтов {IntUtil.CalculateTopPercentage(results[3])}, " +
                    $"кулдаун у твоей рулетки продлится еще {time:hh\\:mm\\:ss}");
            }
        }
        public static async Task RemoveUserFromBlacklist(UserObject user, string[] input)
        {
            if (!IllAccess.Low(user)) return;
            if (input.Length != 2)
            {
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
                return;
            }
            var UserToUnban = await MySQL.GetUser(input[1]).ConfigureAwait(false);
            if (UserToUnban.dbID == -404)
            {
                TtvIRCClient.SendMessage(STRINGS.FindUser_ERROR404);
                return;
            }
            var path = IllSkillzBotMain.GetDataPath().uniquePath;
            path = Path.Combine(path, singleton.UserblacklistFileName);
            if (FileManipulator.DeleteLineFromFile(path, UserToUnban.TwitchID.ToString()))
            {
                IllChatFilters.EditUserBlackList(UserToUnban.TwitchID.ToString());
                TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} удален из черного списка");
            }
            else
                TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} не был найден в черном списке");

        }
        public static void AddTowhiteList(UserObject user, string[] input)
        {
            if (!IllAccess.Root(user)) return;
            if (input.Length != 2)
            {
                TtvIRCClient.SendMessage(STRINGS.InputERROR);
                return;
            }
            var path = IllSkillzBotMain.GetDataPath().sharedPath;
            path = Path.Combine(path, singleton.DicWhiteListFileName);
            FileManipulator.AddLineToFile(path, input[1]);
            IllChatFilters.AddToWhiteList(input[1]);
        }        
        public static void AddSubscription(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            TtvIRCClient.SendMessage(AddSub.NewPurchase().ToString());
            SubCheck.RunChecker();
        }
        public static void CheckSubscription(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            if (SubCheck.RunChecker())
                TtvIRCClient.SendMessage("Valid!");
            else
                TtvIRCClient.SendMessage("Expired!");
        }        
        public static async Task GetMods(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
            string Moderators = "";
            foreach ( var mod in mods)            
                Moderators += mod.UserLogin + ": " + mod.UserId + ". ";            
            TtvIRCClient.SendMessage(Moderators);
        }
        public static async Task Sheptun(UserObject user)
        {
            if (!IllAccess.Root(user)) return;
            Console.WriteLine("шептуны");
            var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
            foreach ( var mod in mods)
            {
                await TtvAPI.SendWhisper(mod.UserId, "Тестовый шептун").ConfigureAwait(false);
                await Task.Delay(10).ConfigureAwait(false);
            }         
        }
    }
}