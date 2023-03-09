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

namespace SkillzBot.IllSkillzBot
{
    internal class IllGames
    {
        private static QuizzObject _Quizz = new QuizzObject();  
        private static List<quizz_activeUser> Quizz_ActiveUsers_List = new List<quizz_activeUser>();

        public IllGames()
        {
            IllSingleton.GetInstance().QuizIsRunning = false;
        }
        public static async Task<UserObject> Rulette(UserObject user)
        {
            int CoolDownMin = 43200; // время кулдауна рулетки в секундах 
            int winChanse = 80;      // % выиграша            
                                     // if (Sender.ToLower() == rootUser)
                                     // winChanse = 100;            
            double cd = CoolDownMin - (DateTimeOffset.Now.ToUnixTimeSeconds() - user.roulettCD);

            if (DateTimeOffset.Now.ToUnixTimeSeconds() - user.roulettCD >= CoolDownMin)
            {
                user.roulettCD = DateTimeOffset.Now.ToUnixTimeSeconds();
                if (!IntUtil.GetChance(winChanse))
                {
                    if (user.roulettCon > 1)
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteLooseWs, user.Name, user.roulettCon));
                    else
                        TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteLoose, user.Name));
                    user.roulettCon = 0;
                    user = await TtvAPI.TimeOutUser(user, 600, STRINGS.RouletteTimeOut).ConfigureAwait(false);
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
                    TtvIRCClient.SendMessage(string.Format(STRINGS.RouletteCD, user.Name, cd));
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
            if (IllSingleton.GetInstance().BroadcasterIsOnline || isForced)
            {
                string SQL_string = "SELECT COUNT(*) FROM dbQuiz";
                var results = await MySQL.SudoSQLReader(SQL_string).ConfigureAwait(false);
                int questionID = IntUtil.Random(1, results[0].dbID);
                _Quizz = await MySQL.GetQuiz(questionID).ConfigureAwait(false);
                await TtvAPI.Announce(string.Format(STRINGS.QuizStart, StringUtil.Shuffle(_Quizz.QuizzQuestion))).ConfigureAwait(false);
                IllSingleton.GetInstance().QuizIsRunning = true;
                double QuizRunTimer = DateTimeOffset.Now.ToUnixTimeSeconds();
                while (IllSingleton.GetInstance().QuizIsRunning)
                {
                    if (DateTimeOffset.Now.ToUnixTimeSeconds() - QuizRunTimer >= 30)
                    {
                        IllSingleton.GetInstance().QuizIsRunning = false;
                        TtvIRCClient.SendMessage(STRINGS.QuizTimeOut);
                    }
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
        }
        private static bool CheckQuizzAnswer(string message)
        {
            if (message.ToLower().Contains(_Quizz.QuizzAnswer.ToLower()))
            {
                IllSingleton.GetInstance().QuizIsRunning = false;
                if (IllSingleton.GetInstance().AntiBotProtectionLvL == 2)
                    Quizz_ActiveUsers_List.Clear();
                return true;
            }
            else
                return false;
        }
        private static void QuizzActiveUser(string ttvID)
        {
            bool found = false;
            foreach (var User in Quizz_ActiveUsers_List)
            {
                if (User.TwitchID == ttvID)
                {
                    User.MessageCount++;
                    found = true; break;
                }
            }
            if (!found)
            {
                Quizz_ActiveUsers_List.Add(new quizz_activeUser()
                {
                    TwitchID = ttvID,
                    MessageCount = 0
                });
            }
        }
        private static bool CheckQuizzActiveUser(string ttvID)
        {
            foreach (var user in Quizz_ActiveUsers_List)
            {
                if (user.TwitchID == ttvID)
                {
                    if (IllSingleton.GetInstance().AntiBotProtectionLvL == 0)
                        return true;
                    if (user.MessageCount > 0)
                        return true;
                }
            }
            return false;
        }
        public static UserObject UserGuessAnswer(UserObject user, string message)
        {
            QuizzActiveUser(user.TwitchID.ToString());
            if (IllSingleton.GetInstance().FirstQuizzOfTheDay || CheckQuizzActiveUser(user.TwitchID.ToString()))
            {
                if (CheckQuizzAnswer(message))
                {
                    IllSingleton.GetInstance().FirstQuizzOfTheDay = false;
                    user.QuizPoints += _Quizz.QuizzCost;
                    user.QuizTotal += _Quizz.QuizzCost;
                    TtvIRCClient.SendMessage(string.Format(STRINGS.QuizWin, _Quizz.QuizzAnswer, user.Name, _Quizz.QuizzCost, user.QuizPoints, user.QuizTotal));
                }
            }
            return user;
        }
        public static void ClearQuizzActiveUsers()
        {
            Quizz_ActiveUsers_List.Clear();
        }
        #endregion
    }
}
