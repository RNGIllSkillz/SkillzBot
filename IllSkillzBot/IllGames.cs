using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.IllConfiguration;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    public class IllGames
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly IDatabaseService _database;
        private readonly ITwitchService _twitchService;
        private readonly IBotStateService _botState;
        private readonly IIllAccess _illAccess;

        private static QuizzObject _Quizz = new QuizzObject();
        private static readonly List<quizz_activeUser> Quizz_ActiveUsers_List = new List<quizz_activeUser>();
        private static readonly object _ActiveUsers_ListLock = new object();

        public IllGames(ITtvIRCClient ircClient, IDatabaseService database, ITwitchService twitchService, IBotStateService botState, IIllAccess illAccess)
        {
            _ircClient = ircClient ?? throw new ArgumentNullException(nameof(ircClient));
            _database = database ?? throw new ArgumentNullException(nameof(database));            
            _twitchService = twitchService;
            _botState = botState;
            _illAccess = illAccess;
            //_botState.Current.QuizIsRunning = false;
        }
        public async Task<UserObject> Rulette(UserObject user)
        {
            const int CoolDownMin = 43200;
            const int winChanse = 80;
            bool isGod = _botState.Current.GodMode && _illAccess.Root(user);
            if (isGod || user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds() <= 0)
            {
                user.roulettCD = DateTimeOffset.Now.ToUnixTimeSeconds() + CoolDownMin;
                if (!IntUtil.GetChance(winChanse))
                {
                    if (user.roulettCon > 1)
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteLooseWs, user.Name, user.roulettCon));
                    else
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteLoose, user.Name));
                    user.roulettCon = 0;
                    if (Convert.ToBoolean(user.isMod))
                    {
                        if (!isGod)
                            await _twitchService.TimeOutModerator(user, 600, STRINGS.RouletteTimeOut);
                    }
                    else
                        await _twitchService.TimeOutUser(user, 600, STRINGS.RouletteTimeOut);                    
                }
                else
                {
                    user.roulettCon++;
                    if (user.roulettCon <= 1)
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteWin, user.Name));
                    else
                    {
                        var pos = await _database.GetUserPositionAsync(user.Name, "roulettCon");
                        await _ircClient.SendMessage(string.Format(STRINGS.RouletteWinStreak, user.Name, user.roulettCon, pos[0], pos[1], IntUtil.RulProbability(user.roulettCon, winChanse)));
                    }
                }
            }
            else
            {
                if (Convert.ToBoolean(user.isVip) || Convert.ToBoolean(user.isMod))
                {
                    await _ircClient.SendMessage(string.Format(STRINGS.RouletteCD, user.Name, user.roulettCD - DateTimeOffset.Now.ToUnixTimeSeconds()));
                }
            }
            return user;
        }
        public string GetMagic8BallAnswer()
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
            await _ircClient.SendMessage("Need to upgrade SQLReader logic at Quizz()");
            await Task.CompletedTask;
        }
        private bool CheckQuizzAnswer(string message)
        {
            if (!message.Contains(_Quizz.QuizzAnswer, StringComparison.OrdinalIgnoreCase)) return false;
            //if (!message.Contains(_Quizz.QuizzAnswer, StringComparison.OrdinalIgnoreCase)) return false;
            //_botState.Current.QuizIsRunning = false;
            //if (_botState.Current.AntiBotProtectionLvl == 2)
            //    lock (_ActiveUsers_ListLock)
            //        Quizz_ActiveUsers_List.Clear();
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
        private bool CheckQuizzActiveUser(string ttvID)
        {
            lock (_ActiveUsers_ListLock)
            {
                var user = Quizz_ActiveUsers_List.FirstOrDefault(u => u.TwitchID == ttvID);
                if (user == null) return false;

                if (_botState.Current.AntiBotProtectionLvl == 0) return true;
                if (user.MessageCount > 0) return true;

                return false;
            }
        }
        public async Task<UserObject> UserGuessAnswer(UserObject user, string message)
        {
            if (!_botState.Current.FirstQuizOfTheDay && !CheckQuizzActiveUser(user.TwitchID.ToString())) return user;
            if (!message.Contains(_Quizz.QuizzAnswer, StringComparison.OrdinalIgnoreCase)) return user;

            await _botState.UpdateStateAsync(s => s.QuizIsRunning = false);

            if (_botState.Current.AntiBotProtectionLvl == 2)
                lock (_ActiveUsers_ListLock)
                    Quizz_ActiveUsers_List.Clear();

            if (StringUtil.CountUpperCaseLetters(message) > 3) return user;

            await _botState.UpdateStateAsync(s => s.FirstQuizOfTheDay = false);

            user.QuizPoints += _Quizz.QuizzCost;
            user.QuizTotal += _Quizz.QuizzCost;
            await _ircClient.SendMessage(string.Format(STRINGS.QuizWin, _Quizz.QuizzAnswer, user.Name, _Quizz.QuizzCost, user.QuizPoints, user.QuizTotal)).ConfigureAwait(false);
            return user;
        }
        public void ClearQuizzActiveUsers()
        {
            lock (_ActiveUsers_ListLock)
                Quizz_ActiveUsers_List.Clear();
        }
        #endregion
    }
}