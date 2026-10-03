using Microsoft.Extensions.Logging;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    public class IllGames
    {
        private const int QuizDurationSec = 180;

        private readonly ITtvIRCClient _ircClient;
        private readonly IDatabaseService _database;
        private readonly ITwitchService _twitchService;
        private readonly IBotStateService _botState;
        private readonly IIllAccess _illAccess;
        private readonly IllModeratorsInteractions _modInteractions;
        private readonly ILogger<IllGames> _logger;

        private readonly object _quizLock = new object();
        private QuizzObject _quiz = new QuizzObject();
        private DateTime _quizStartedUtc;
        private int _quizGeneration; // bumps whenever a quiz starts or ends; the timeout task checks it

        private static readonly List<quizz_activeUser> Quizz_ActiveUsers_List = new List<quizz_activeUser>();
        private static readonly object _ActiveUsers_ListLock = new object();

        public IllGames(ITtvIRCClient ircClient, IDatabaseService database, ITwitchService twitchService, IBotStateService botState, IIllAccess illAccess, IllModeratorsInteractions modInteractions, ILogger<IllGames> logger)
        {
            _ircClient = ircClient ?? throw new ArgumentNullException(nameof(ircClient));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _twitchService = twitchService;
            _botState = botState;
            _illAccess = illAccess;
            _modInteractions = modInteractions ?? throw new ArgumentNullException(nameof(modInteractions));
            _logger = logger;
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
                        // Twitch removes moderator status on timeout; this path schedules the re-mod.
                        if (!isGod)
                            await _modInteractions.TimeOutModeratorAsync(user, 600, STRINGS.RouletteTimeOut);
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

        /// <summary>
        /// Starts a quiz from a random dbQuiz row, or reports the running one. Not forced
        /// (automatic) starts are skipped while the stream is offline.
        /// </summary>
        public async Task Quizz(bool isForced)
        {
            string runningQuestion = null;
            int runningCost = 0, secondsLeft = 0;
            lock (_quizLock)
            {
                if (_botState.Current.QuizIsRunning && !string.IsNullOrEmpty(_quiz.QuizzAnswer))
                {
                    runningQuestion = _quiz.QuizzQuestion;
                    runningCost = _quiz.QuizzCost;
                    secondsLeft = Math.Max(0, QuizDurationSec - (int)(DateTime.UtcNow - _quizStartedUtc).TotalSeconds);
                }
            }
            if (runningQuestion != null)
            {
                await _ircClient.SendMessage($"Викторина уже идет: {runningQuestion} (приз {runningCost}). Осталось {secondsLeft} с.");
                return;
            }

            if (!isForced && !_botState.Current.BroadcasterIsOnline) return;

            QuizzObject quiz;
            try
            {
                quiz = await _database.GetRandomQuizAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load a quiz question");
                await _ircClient.SendMessage("Не удалось загрузить вопрос викторины.");
                return;
            }
            if (quiz == null || string.IsNullOrWhiteSpace(quiz.QuizzQuestion) || string.IsNullOrWhiteSpace(quiz.QuizzAnswer))
            {
                await _ircClient.SendMessage("В базе нет вопросов для викторины (таблица dbQuiz).");
                return;
            }

            int generation;
            lock (_quizLock)
            {
                _quiz = quiz;
                _quizStartedUtc = DateTime.UtcNow;
                generation = ++_quizGeneration;
            }
            await _botState.UpdateStateAsync(s => s.QuizIsRunning = true);
            _logger.LogInformation("Quiz started: {Question} (prize {Prize})", quiz.QuizzQuestion, quiz.QuizzCost);
            await _ircClient.SendMessage($"{string.Format(STRINGS.QuizStart, quiz.QuizzQuestion)} Приз {quiz.QuizzCost} балл(ов), {QuizDurationSec / 60} мин на ответ.");

            _ = Task.Run(() => QuizTimeoutAsync(generation));
        }

        private async Task QuizTimeoutAsync(int generation)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(QuizDurationSec));

                string answer;
                lock (_quizLock)
                {
                    if (_quizGeneration != generation) return; // answered already, or a new quiz started
                    answer = _quiz.QuizzAnswer;
                    _quiz = new QuizzObject();
                    _quizGeneration++;
                }
                await _botState.UpdateStateAsync(s => s.QuizIsRunning = false);
                if (_botState.Current.AntiBotProtectionLvl == 2) ClearQuizzActiveUsers();
                _logger.LogInformation("Quiz timed out; answer was {Answer}", answer);
                await _ircClient.SendMessage($"{STRINGS.QuizTimeOut} Правильный ответ: {answer}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quiz timeout task failed");
            }
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

        /// <summary>Called for every chat message while a quiz is running; the first correct answer wins.</summary>
        public async Task<UserObject> UserGuessAnswer(UserObject user, string message)
        {
            string answer;
            int cost;
            lock (_quizLock)
            {
                answer = _quiz.QuizzAnswer;
                cost = _quiz.QuizzCost;
            }
            if (string.IsNullOrEmpty(answer)) return user;
            if (!message.Contains(answer, StringComparison.OrdinalIgnoreCase)) return user;
            // Shouted answers are ignored and the quiz keeps running.
            if (StringUtil.CountUpperCaseLetters(message) > 3) return user;
            if (!_botState.Current.FirstQuizOfTheDay && !CheckQuizzActiveUser(user.TwitchID.ToString())) return user;

            lock (_quizLock)
            {
                if (!string.Equals(_quiz.QuizzAnswer, answer)) return user; // lost the race to the timeout
                _quiz = new QuizzObject();
                _quizGeneration++;
            }

            await _botState.UpdateStateAsync(s =>
            {
                s.QuizIsRunning = false;
                s.FirstQuizOfTheDay = false;
            });
            if (_botState.Current.AntiBotProtectionLvl == 2) ClearQuizzActiveUsers();

            user.QuizPoints += cost;
            user.QuizTotal += cost;
            _logger.LogInformation("Quiz won by {User} (+{Prize})", user.Name, cost);
            await _ircClient.SendMessage(string.Format(STRINGS.QuizWin, answer, user.Name, cost, user.QuizPoints, user.QuizTotal)).ConfigureAwait(false);
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
