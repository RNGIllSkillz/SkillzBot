using Microsoft.Extensions.Logging;
using SkillzBot.API.Twitch;
using SkillzBot.Hosts;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    internal sealed class IllGames
    {
        private readonly ILogger<IllGames> _logger;
        private readonly ITtvIRCClient _ircClient;
        private readonly IDatabaseService _database;

        private static QuizzObject _Quizz = new QuizzObject();
        private static readonly List<quizz_activeUser> Quizz_ActiveUsers_List = new List<quizz_activeUser>();
        private static readonly object _ActiveUsers_ListLock = new object();

        public IllGames(ILogger<IllGames> logger, ITtvIRCClient ircClient, IDatabaseService database)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ircClient = ircClient ?? throw new ArgumentNullException(nameof(ircClient));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            IllSingleton.State.QuizIsRunning = false;
        }
        public async Task<UserObject> Rulette(UserObject user)
        {
            const int CoolDownMin = 43200;
            const int winChanse = 80;

            if (user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds() <= 0)
            {
                user.roulettCD = DateTimeOffset.Now.ToUnixTimeSeconds() + CoolDownMin;
                if (!IntUtil.GetChance(winChanse))
                {
                    if (user.roulettCon > 1)
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteLooseWs, user.Name, user.roulettCon)).ConfigureAwait(false);
                    else
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteLoose, user.Name)).ConfigureAwait(false);
                    user.roulettCon = 0;
                    if (Convert.ToBoolean(user.isMod))
                        await TtvAPI.TimeOutModerator(user, 600, STRINGS.RouletteTimeOut).ConfigureAwait(false);
                    else
                        await TtvAPI.TimeOutUser(user, 600, STRINGS.RouletteTimeOut).ConfigureAwait(false);
                }
                else
                {
                    user.roulettCon++;
                    if (user.roulettCon <= 1)
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteWin, user.Name)).ConfigureAwait(false);
                    else
                    {
                        var pos = await _database.GetUserPositionAsync(user.Name, "roulettCon").ConfigureAwait(false);
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteWinStreak, user.Name, user.roulettCon, pos[0], pos[1], IntUtil.RulProbability(user.roulettCon, winChanse))).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (Convert.ToBoolean(user.isVip) || Convert.ToBoolean(user.isMod))
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.RouletteCD, user.Name, user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds())).ConfigureAwait(false);
                }
            }
            return user;
        }
        public static string GetMagic8BallAnswer()
        {
            string[] answers = {
            "Да, определенно!",
            "Нет, ни в коем случае.",
            "Спросите позже.",
            "Это определенно.",
            "Не рассчитывайте на это.",
            "Без сомнения.",
            "Перспективы не так хороши.",
            "Конечно нет!",
            "Скорее всего.",
            "Это загадка.",
            "Точно, как песня ветра.",
            "Будущее неясно.",
            "Доверяйте своим навыкам, вызывающий.",
            "Ответ в Нексусе.",
            "По воле Короля Поро – да!",
            "Демасийцы никогда не лгут, поэтому да!",
            "Ноксианцы никогда не сдаются, но на этот раз - нет.",
            "Только йордль может подумать, что это хорошая идея, поэтому нет.",
            "Пески Шурины говорят мне... возможно.",
            "Звезды шепчут неопределенность, вызывающий.",
            "Остерегайтесь теней, ответ находится внутри.",
            "Тьма падает на землю; ответ - нет.",
            "Во Фрельйорде ответ холоднее, чем сердце Эш - нет.",
            "Хаос Зауна говорит, возможно, но, вероятно, нет.",
            "Удача йордлей говорит - да, но остерегайтесь грибов.",
            "Прогресс Пилтовера говорит, вероятно, но с осложнениями.",
            "Магия Бэндл-Сити говорит - да, с приправой каприза.",
            "Черный Туман затмевает ответ; попробуйте позже.",
            "Пираты Билджуотера говорят 'да', но остерегайтесь Кракена!",
            "Гармония Ионии предвещает положительный исход.",
            "Пустота жаждет, но жаждет другого ответа.",
            "Созвездие говорит - да, с космическим предостережением.",
            "Ответ эхом проходит через Воющую Бездну - может быть.",
            "Вершина Таргона раскрывает многообещающее предсказание, вызывающий."
        };
            int index = IntUtil.Random(0, answers.Length);
            return answers[index];
        }
        #region Quizz
        public async Task Quizz(bool isForced)
        {
            await _ircClient.SendMessage("Need to upgrade SQLReader logic at Quizz()").ConfigureAwait(false);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private static bool CheckQuizzAnswer(string message)
        {
            if (!message.Contains(_Quizz.QuizzAnswer, StringComparison.OrdinalIgnoreCase)) return false;
            IllSingleton.State.QuizIsRunning = false;
            if (IllSingleton.State.AntiBotProtectionLvl == 2)
                lock (_ActiveUsers_ListLock)
                    Quizz_ActiveUsers_List.Clear();
            return true;
        }
        public void QuizzActiveUser(string ttvID)
        {
            lock (_ActiveUsers_ListLock)
            {
                var existingUser = Quizz_ActiveUsers_List.FirstOrDefault(u => u.TwitchID == ttvID);
                if (existingUser != null)
                {
                    existingUser.MessageCount++;
                }
                else
                {
                    Quizz_ActiveUsers_List.Add(new quizz_activeUser()
                    {
                        TwitchID = ttvID,
                        MessageCount = 0
                    });
                }
            }
        }
        private static bool CheckQuizzActiveUser(string ttvID)
        {
            lock (_ActiveUsers_ListLock)
            {
                var user = Quizz_ActiveUsers_List.FirstOrDefault(u => u.TwitchID == ttvID);
                if (user == null) return false;

                if (IllSingleton.State.AntiBotProtectionLvl == 0) return true;
                if (user.MessageCount > 0) return true;

                return false;
            }
        }
        public async Task<UserObject> UserGuessAnswer(UserObject user, string message)
        {
            if (!IllSingleton.State.FirstQuizOfTheDay && !CheckQuizzActiveUser(user.TwitchID.ToString())) return user;
            if (!CheckQuizzAnswer(message)) return user;
            if (StringUtil.CountUpperCaseLetters(message) > 3) return user;
            IllSingleton.State.FirstQuizOfTheDay = false;
            user.QuizPoints += _Quizz.QuizzCost;
            user.QuizTotal += _Quizz.QuizzCost;
            await _ircClient.SendMessage(string.Format(STRINGS.QuizWin, _Quizz.QuizzAnswer, user.Name, _Quizz.QuizzCost, user.QuizPoints, user.QuizTotal)).ConfigureAwait(false);
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