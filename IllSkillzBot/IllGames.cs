using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.MYSQL;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using SkillzBot.IllSTRINGS;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace SkillzBot.IllSkillzBot
{
    internal sealed class IllGames
    {
        private static QuizzObject _Quizz = new QuizzObject();  
        private static readonly List<quizz_activeUser> Quizz_ActiveUsers_List = new List<quizz_activeUser>();
        private static readonly object _ActiveUsers_ListLock = new object();
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        static IllGames()
        {
            singleton.QuizIsRunning = false;
        }
        public static async Task<UserObject> Rulette(UserObject user)
        {
            int CoolDownMin = 43200; // время кулдауна рулетки в секундах 
            int winChanse = 80;      // % выиграша            
                                     // if (Sender.ToLower() == rootUser)
                                     // winChanse = 100;   
            if (user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds() <= 0)
            {
                user.roulettCD = DateTimeOffset.Now.ToUnixTimeSeconds() + CoolDownMin;
                if (!IntUtil.GetChance(winChanse))
                {
                    if (user.roulettCon > 1)
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteLooseWs, user.Name, user.roulettCon));
                    else
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteLoose, user.Name));
                    user.roulettCon = 0;
                    if (Convert.ToBoolean(user.isMod))
                        await TtvClient.TTVRewards.RewardsRedemption.TimeOutModerator(user, 600, STRINGS.RouletteTimeOut).ConfigureAwait(false);
                    else
                        await TtvAPI.TimeOutUser(user, 600, STRINGS.RouletteTimeOut).ConfigureAwait(false);
                }
                else
                {
                    //winChanse = 80;
                    user.roulettCon++;
                    if (user.roulettCon <= 1)
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteWin, user.Name));
                    else
                    {
                        var pos = await MySQL.GetTopPos(user.Name, "roulettCon").ConfigureAwait(false);
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteWinStreak, user.Name, user.roulettCon, pos[0], pos[1], IntUtil.RulProbability(user.roulettCon, winChanse)));
                    }
                }
            }
            else
            {
                if (Convert.ToBoolean(user.isVip) || Convert.ToBoolean(user.isMod))
                {
                    TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteCD, user.Name, user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds()));
                }
                else
                {
                    //TtvIRCClient.SendWhisper(user.Name, $"cd рулетки еще {cd}сек");
                }
            }
            return user;
        }
        #region Quizz
        public static async Task Quizz(bool isForced)
        {
            if (!singleton.BroadcasterIsOnline && !isForced) return;
            string SQL_string = "SELECT COUNT(*) FROM dbQuiz";
            var results = await MySQL.SudoSQLReader(SQL_string).ConfigureAwait(false);
            int questionID = IntUtil.Random(1, results[0].dbID);
            _Quizz = await MySQL.GetQuiz(questionID).ConfigureAwait(false);
            TtvIRCClient.SendMessage(string.Format(STRINGS.QuizStart, StringUtil.Shuffle(_Quizz.QuizzQuestion)));
            singleton.QuizIsRunning = true;
            double QuizRunTimer = DateTimeOffset.Now.ToUnixTimeSeconds();
            while (singleton.QuizIsRunning)
            {
                if (DateTimeOffset.Now.ToUnixTimeSeconds() - QuizRunTimer >= 30)
                {
                    singleton.QuizIsRunning = false;
                    TtvIRCClient.SendMessage(STRINGS.QuizTimeOut);
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
        private static bool CheckQuizzAnswer(string message)
        {
            if (!message.Contains(_Quizz.QuizzAnswer, StringComparison.OrdinalIgnoreCase)) return false;
            singleton.QuizIsRunning = false;
            if (singleton.AntiBotProtectionLvL == 2)            
                lock (_ActiveUsers_ListLock)                
                    Quizz_ActiveUsers_List.Clear(); 
            return true;
        }
        public static void QuizzActiveUser(string ttvID)
        {
            lock (_ActiveUsers_ListLock)
            {
                foreach (var User in Quizz_ActiveUsers_List)
                {
                    if (User.TwitchID != ttvID) continue;
                    User.MessageCount++;
                    return;
                }
                Quizz_ActiveUsers_List.Add(new quizz_activeUser()
                {
                    TwitchID = ttvID,
                    MessageCount = 0
                });
            }
        }
        private static bool CheckQuizzActiveUser(string ttvID)
        {
            lock (_ActiveUsers_ListLock)
            {
                foreach (var user in Quizz_ActiveUsers_List)
                {
                    if (user.TwitchID != ttvID) continue;
                    if (singleton.AntiBotProtectionLvL == 0) return true;
                    if (user.MessageCount > 0) return true;
                }
                return false;
            }
        }
        public static UserObject UserGuessAnswer(UserObject user, string message)
        {            
            if (!singleton.FirstQuizzOfTheDay && !CheckQuizzActiveUser(user.TwitchID.ToString())) return user;
            if (!CheckQuizzAnswer(message)) return user;
            if (StringUtil.CountUpperCaseLetters(message) > 3) return user;
            singleton.FirstQuizzOfTheDay = false;
            user.QuizPoints += _Quizz.QuizzCost;
            user.QuizTotal += _Quizz.QuizzCost;
            TtvIRCClient.SendMessage(string.Format(STRINGS.QuizWin, _Quizz.QuizzAnswer, user.Name, _Quizz.QuizzCost, user.QuizPoints, user.QuizTotal));
            return user;
        }
        public static void ClearQuizzActiveUsers()
        {
            lock (_ActiveUsers_ListLock)            
                Quizz_ActiveUsers_List.Clear();            
        }
        #endregion
    }
}
