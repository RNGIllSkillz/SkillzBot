using Camille.Enums;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.SpectatorV5;
using Participant = Camille.RiotGames.MatchV5.Participant;
using Microsoft.Extensions.Logging;
using SkillzBot.API.RiotGames;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot.Predictions;
using SkillzBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    /// <summary>
    /// Auto-predictions for the streamer's League games. The default is win/lose; after a
    /// game, at most once per <see cref="PollInterval"/>, chat picks the type of the next
    /// prediction through a Twitch poll (win/lose plus three weighted random alternatives).
    /// </summary>
    public class IllPredictions
    {
        private const int MaxGameLengthSec = 5400;
        private const int PredictionWindowSec = 180;
        private const int NewGameMaxLengthSec = 30;
        private const int RemakeThresholdSec = 300;
        private const int PollDurationSec = 120;
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(4);

        private readonly ILogger<IllPredictions> _logger;
        private readonly IRiotApiService _riotApi;
        private readonly ITtvIRCClient _ircClient;
        private readonly ITwitchService _twitchService;
        private readonly IBotStateService _botState;
        private readonly IGameStateService _gameState;
        private readonly ChampionNames _championNames;
        private readonly BotConfigModel _config;

        private string _currentMatchId;
        private string _platformId;
        private int _pollRunning;

        private sealed class ActivePrediction
        {
            public PredictionKind Kind;
            /// <summary>Outcome title used for each champion id, so resolution matches what Twitch shows.</summary>
            public Dictionary<int, string> OutcomeByChampion;
        }

        public IllPredictions(
            ILogger<IllPredictions> logger,
            IRiotApiService riotApi,
            ITtvIRCClient ircClient,
            ITwitchService twitchService,
            IBotStateService botState,
            IGameStateService gameState,
            ChampionNames championNames,
            BotConfigModel config)
        {
            _logger = logger;
            _riotApi = riotApi;
            _ircClient = ircClient;
            _twitchService = twitchService;
            _botState = botState;
            _gameState = gameState;
            _championNames = championNames;
            _config = config;
        }

        private string StreamerLabel => PredictionCatalog.Truncate(_config.ChannelName, PredictionCatalog.MaxOutcomeLength);

        #region Game detection

        public async Task GetCurrentMatchTask()
        {
            if (_botState.Current.Debug) _logger.LogDebug("Running GetCurrentMatchTask()");
            if (!_botState.Current.IsSubActive || _botState.Current.InMatch || !_botState.Current.AutoPred) return;

            _platformId = _gameState.Current.SummonerRegion switch
            {
                "ru" => "RU_",
                "euw" => "EUW1_",
                "na" => "NA1_",
                _ => "EUW1_",
            };

            var currentGame = await _riotApi.GetCurrentGameAsync();
            if (currentGame == null) return;

            string matchId = _platformId + currentGame.GameId;
            if (_currentMatchId == matchId || currentGame.GameLength > NewGameMaxLengthSec) return;
            _currentMatchId = matchId;
            _logger.LogInformation("New game detected: {MatchId} ({Mode}, {Players} players).", matchId, currentGame.GameMode, currentGame.Participants?.Length ?? 0);

            var predictions = await _twitchService.GetCurrentPredPublic();
            if (predictions == null || predictions.Data.Length == 0) return;
            var lastStatus = predictions.Data.First().Status;
            if (lastStatus != TwitchLib.Api.Core.Enums.PredictionStatus.RESOLVED && lastStatus != TwitchLib.Api.Core.Enums.PredictionStatus.CANCELED)
            {
                _logger.LogWarning("Previous prediction is still {Status}; skipping auto-prediction for {MatchId}.", lastStatus, matchId);
                return;
            }

            var rank = await _riotApi.GetLeagueEntriesBySummonerAsync();
            if (rank == null) return;

            var kind = await ConsumeNextKindAsync();
            await SetInMatchAsync(true);
            try
            {
                await RunPredictionAsync(kind, currentGame, matchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running prediction task for {MatchId}", matchId);
            }
            finally
            {
                await SetInMatchAsync(false);
            }
        }

        private Task SetInMatchAsync(bool value) => _botState.UpdateStateAsync(s => s.InMatch = value);

        /// <summary>Takes the chat-chosen type for this game (one-shot) or falls back to win/lose.</summary>
        private async Task<PredictionKind> ConsumeNextKindAsync()
        {
            string key = _botState.Current.NextPredictionKey;
            var kind = PredictionCatalog.Find(key) ?? PredictionCatalog.WinLose;
            if (!string.IsNullOrEmpty(key))
                await _botState.UpdateStateAsync(s => s.NextPredictionKey = null);
            return kind;
        }

        #endregion

        #region Prediction lifecycle

        private async Task RunPredictionAsync(PredictionKind kind, CurrentGameInfo game, string matchId)
        {
            var active = await StartPredictionAsync(kind, game);

            var match = await WaitForMatchEndAsync(matchId);
            if (match == null) return;

            var participant = _riotApi.GetParticipantByMatch(match);
            if (participant == null)
            {
                await _botState.UpdateStateAsync(s => s.AutoPred = false);
                _logger.LogCritical("Participant could not be found in match {MatchId}. Auto-predictions disabled.", matchId);
                await _ircClient.SendMessage("Критическая ошибка: не удалось найти призывателя в матче. Автоставки выключены.");
                return;
            }

            if (match.Info.GameDuration <= RemakeThresholdSec)
            {
                await _ircClient.SendMessage("Матч отменен. Ставка будет отменена.");
                await _twitchService.CencelePrediction();
                return;
            }

            bool won = participant.Win;
            if (_botState.Current.Debug) _logger.LogDebug("Матч завершен {Win}", won);

            await _gameState.UpdateStateAsync(s =>
            {
                s.NumGames++;
                if (won) s.NumWins++;
                else s.NumLosses++;
            });
            await UpdateDailyStats(won);

            await ResolveAsync(active, match, participant, won);
            await MaybeStartPollAsync();
        }

        /// <summary>Creates the Twitch prediction for the requested type, or win/lose when the game does not fit it.</summary>
        private async Task<ActivePrediction> StartPredictionAsync(PredictionKind kind, CurrentGameInfo game)
        {
            if (kind.Scope == PredictionScope.WinLose)
                return await StartWinLoseAsync();

            string fallbackReason = null;
            try
            {
                switch (kind.Scope)
                {
                    case PredictionScope.Lane:
                        if (game.GameMode != GameMode.CLASSIC)
                        {
                            fallbackReason = "в этом режиме нет лайнов";
                            break;
                        }
                        await _twitchService.Start_2_Prediction(kind.Title, StreamerLabel, PredictionCatalog.OpponentLabel, PredictionWindowSec);
                        _logger.LogInformation("Prediction started: {Key} ({Title}).", kind.Key, kind.Title);
                        return new ActivePrediction { Kind = kind };

                    case PredictionScope.Team:
                    case PredictionScope.All:
                        {
                            var puuid = _riotApi.CurrentPuuid;
                            var streamer = game.Participants?.FirstOrDefault(p => string.Equals(p.Puuid, puuid, StringComparison.OrdinalIgnoreCase));
                            if (streamer == null) { fallbackReason = "стример не найден в игре"; break; }

                            var group = kind.Scope == PredictionScope.Team
                                ? game.Participants.Where(p => p.TeamId == streamer.TeamId).ToList()
                                : game.Participants.ToList();
                            int expected = kind.Scope == PredictionScope.Team ? 5 : 10;
                            if (group.Count != expected) { fallbackReason = $"в игре {group.Count} игроков вместо {expected}"; break; }

                            var outcomes = new Dictionary<int, string>();
                            var titles = new List<string>();
                            foreach (var p in group)
                            {
                                var name = PredictionCatalog.Truncate(await _championNames.GetNameAsync(p.ChampionId), PredictionCatalog.MaxOutcomeLength);
                                outcomes[(int)p.ChampionId] = name;
                                titles.Add(name);
                            }
                            if (titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != titles.Count) { fallbackReason = "чемпионы повторяются"; break; }

                            if (expected == 5)
                                await _twitchService.Start_5_Prediction(titles, kind.Title, PredictionWindowSec);
                            else
                                await _twitchService.Start_10_Prediction(titles, kind.Title, PredictionWindowSec);
                            _logger.LogInformation("Prediction started: {Key} ({Title}) with outcomes {Outcomes}.", kind.Key, kind.Title, string.Join(", ", titles));
                            return new ActivePrediction { Kind = kind, OutcomeByChampion = outcomes };
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start prediction {Key}; falling back to win/lose.", kind.Key);
                fallbackReason = "ошибка при создании ставки";
            }

            _logger.LogWarning("Prediction {Key} not applicable ({Reason}); falling back to win/lose.", kind.Key, fallbackReason);
            await _ircClient.SendMessage($"Ставка «{kind.PollLabel}» недоступна ({fallbackReason}), запускаю вин/луз.");
            return await StartWinLoseAsync();
        }

        private async Task<ActivePrediction> StartWinLoseAsync()
        {
            await _twitchService.Start_2_Prediction(PredictionCatalog.WinLose.Title, "вин", "луз", PredictionWindowSec);
            if (_botState.Current.Debug) _logger.LogDebug("Ставка запущена");
            return new ActivePrediction { Kind = PredictionCatalog.WinLose };
        }

        /// <summary>Polls the Match API until the game shows up as finished. Null when tracking gave up.</summary>
        private async Task<Match> WaitForMatchEndAsync(string matchId)
        {
            int consecutiveErrors = 0;
            long deadline = DateTimeOffset.Now.ToUnixTimeSeconds() + MaxGameLengthSec;

            while (_botState.Current.InMatch)
            {
                if (DateTimeOffset.Now.ToUnixTimeSeconds() > deadline)
                {
                    await _ircClient.SendMessage($"Кажется я забаговал. Матч длится 1.5 часа. Прекращаю отслеживать матч с ID:{matchId}");
                    _logger.LogWarning("Match tracking timed out after 1.5 hours for Match ID: {MatchId}", matchId);
                    return null;
                }

                Match match;
                try
                {
                    match = await _riotApi.GetMatchAsync(matchId);
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogError(ex, "GetMatchAsync failed ({Count} in a row) for {MatchId}", consecutiveErrors, matchId);
                    if (consecutiveErrors > 5)
                    {
                        _logger.LogError("Giving up on match {MatchId} after repeated Riot API errors.", matchId);
                        return null;
                    }
                    await Task.Delay(2000);
                    continue;
                }

                if (consecutiveErrors != 0)
                {
                    _logger.LogInformation("Recovered from {ErrorCount} consecutive API errors", consecutiveErrors);
                    consecutiveErrors = 0;
                }

                if (match == null)
                {
                    await Task.Delay(4000);
                    continue;
                }
                return match;
            }
            return null;
        }

        private static ParticipantStats ToStats(Participant p) => new ParticipantStats(
            p.Puuid, (int)p.ChampionId, (int)p.TeamId, p.TeamPosition,
            p.Kills, p.Deaths, p.Assists,
            p.TotalMinionsKilled + p.NeutralMinionsKilled, p.GoldEarned, p.TotalDamageDealtToChampions);

        private async Task ResolveAsync(ActivePrediction active, Match match, Participant streamer, bool won)
        {
            var kind = active.Kind;
            if (kind.Scope == PredictionScope.WinLose)
            {
                await _twitchService.End_WinLoose_Prediction(won, 0);
                return;
            }

            var stats = match.Info.Participants.Select(ToStats).ToList();
            switch (kind.Scope)
            {
                case PredictionScope.Lane:
                    {
                        var result = PredictionCatalog.ResolveLane(stats, streamer.Puuid, kind.Metric);
                        switch (result.Outcome)
                        {
                            case LaneOutcome.StreamerWins:
                            case LaneOutcome.OpponentWins:
                                bool streamerWins = result.Outcome == LaneOutcome.StreamerWins;
                                await _twitchService.End_WinLoose_Prediction(streamerWins, 0);
                                var opponentName = await _championNames.GetNameAsync((Champion)result.OpponentChampionId);
                                await _ircClient.SendMessage(
                                    $"Итог ставки «{kind.PollLabel}»: {StreamerLabel} {PredictionCatalog.FormatValue(result.StreamerValue, kind.Metric)} vs {opponentName} {PredictionCatalog.FormatValue(result.OpponentValue, kind.Metric)}. " +
                                    (streamerWins ? "Стример доминировал на лайне PogChamp" : "git gud"));
                                break;
                            case LaneOutcome.Tie:
                                await _ircClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                                await _twitchService.CencelePrediction();
                                break;
                            default:
                                await _ircClient.SendMessage("Не удалось определить оппонента на лайне. Ставка отменена PoroSad");
                                await _twitchService.CencelePrediction();
                                break;
                        }
                        break;
                    }

                case PredictionScope.Team:
                case PredictionScope.All:
                    {
                        var group = kind.Scope == PredictionScope.Team
                            ? stats.Where(s => s.TeamId == (int)streamer.TeamId)
                            : stats;
                        var result = PredictionCatalog.ResolveGroup(group, kind.Metric);
                        if (result.IsTie)
                        {
                            await _ircClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                            await _twitchService.CencelePrediction();
                            break;
                        }
                        if (active.OutcomeByChampion == null || !active.OutcomeByChampion.TryGetValue(result.WinnerChampionId, out var outcomeTitle))
                        {
                            _logger.LogError("Winner champion {ChampionId} has no outcome in the active prediction.", result.WinnerChampionId);
                            await _ircClient.SendMessage("Не удалось сопоставить победителя с исходом ставки. Ставка отменена PoroSad");
                            await _twitchService.CencelePrediction();
                            break;
                        }
                        var endResult = await _twitchService.End_Multy_Prediction(outcomeTitle);
                        if (endResult == "OK")
                            await _ircClient.SendMessage($"Итог ставки «{kind.PollLabel}»: {outcomeTitle} ({PredictionCatalog.FormatValue(result.WinnerValue, kind.Metric)}) PogChamp");
                        else
                            await _ircClient.SendMessage($"Не удалось закрыть ставку: {endResult}");
                        break;
                    }
            }
        }

        #endregion

        #region Chat poll for the next prediction type

        private async Task MaybeStartPollAsync()
        {
            var s = _botState.Current;
            if (!s.PredictionPollEnabled || !s.AutoPred || !s.IsSubActive || !s.BroadcasterIsOnline) return;
            if (DateTime.UtcNow - s.LastPredictionPollUtc < PollInterval) return;
            await StartPollAsync();
        }

        /// <summary>Starts the "which prediction next" poll. Returns a short status for chat commands.</summary>
        public async Task<string> StartPollAsync()
        {
            if (Interlocked.CompareExchange(ref _pollRunning, 1, 0) != 0) return "Опрос уже идет.";

            bool handedOff = false;
            try
            {
                var options = PredictionCatalog.PickPollOptions(3, Random.Shared);
                var choices = new List<string> { PredictionCatalog.WinLose.PollLabel };
                choices.AddRange(options.Select(o => o.PollLabel));

                await _botState.UpdateStateAsync(st => st.LastPredictionPollUtc = DateTime.UtcNow);

                var pollId = await _twitchService.CreatePollAsync(PredictionCatalog.PollTitle, choices, PollDurationSec);
                if (pollId == null)
                {
                    _logger.LogError("Prediction poll could not be created. The token needs channel:manage:polls and the channel must be affiliate/partner.");
                    return "Не удалось создать опрос (см. лог).";
                }

                _logger.LogInformation("Prediction poll {PollId} started with options: {Options}", pollId, string.Join(" | ", choices));
                await _ircClient.SendMessage($"Опрос: {PredictionCatalog.PollTitle} Варианты: {string.Join(" | ", choices)}. Голосуем {PollDurationSec / 60} мин PopNemo");

                handedOff = true;
                _ = Task.Run(() => FinishPollAsync(pollId, options));
                return "Опрос запущен.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartPollAsync failed");
                return "Ошибка при запуске опроса.";
            }
            finally
            {
                if (!handedOff) Interlocked.Exchange(ref _pollRunning, 0);
            }
        }

        private async Task FinishPollAsync(string pollId, List<PredictionKind> options)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollDurationSec + 5));

                PollResult result = null;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    result = await _twitchService.GetPollAsync(pollId);
                    if (result == null || !result.IsActive) break;
                    await Task.Delay(5000);
                }

                if (result == null || !result.IsFinished)
                {
                    _logger.LogWarning("Poll {PollId} did not finish normally (status {Status}).", pollId, result?.Status ?? "unknown");
                    await _ircClient.SendMessage("Опрос не завершился штатно, остается вин/луз.");
                    return;
                }

                // Choices come back in creation order, so a tie keeps the earlier option (win/lose first).
                var winner = result.Choices.OrderByDescending(c => c.Votes).FirstOrDefault();
                _logger.LogInformation("Poll {PollId} finished: {Votes}", pollId, string.Join(", ", result.Choices.Select(c => $"{c.Title}={c.Votes}")));

                if (winner == null || winner.Votes == 0)
                {
                    await _botState.UpdateStateAsync(s => s.NextPredictionKey = null);
                    await _ircClient.SendMessage("Никто не проголосовал, остается вин/луз.");
                    return;
                }

                var kind = options.FirstOrDefault(o => o.PollLabel.Equals(winner.Title, StringComparison.OrdinalIgnoreCase))
                           ?? PredictionCatalog.FindByPollLabel(winner.Title)
                           ?? PredictionCatalog.WinLose;

                await _botState.UpdateStateAsync(s => s.NextPredictionKey = kind.Scope == PredictionScope.WinLose ? null : kind.Key);

                if (kind.Scope == PredictionScope.WinLose)
                    await _ircClient.SendMessage($"Чат выбрал вин/луз ({winner.Votes} голосов).");
                else
                    await _ircClient.SendMessage($"Чат выбрал «{kind.PollLabel}» ({winner.Votes} голосов)! Ставка запустится на следующую игру.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FinishPollAsync failed for {PollId}", pollId);
            }
            finally
            {
                Interlocked.Exchange(ref _pollRunning, 0);
            }
        }

        public string DescribePollState()
        {
            var s = _botState.Current;
            string lastPoll = s.LastPredictionPollUtc == DateTime.MinValue ? "never" : Services.HealthState.FormatAge(DateTime.UtcNow - s.LastPredictionPollUtc) + " ago";
            var next = PredictionCatalog.Find(s.NextPredictionKey) ?? PredictionCatalog.WinLose;
            return $"Опросы: {(s.PredictionPollEnabled ? "on" : "off")} | следующая ставка: {next.PollLabel} | последний опрос: {lastPoll} | идет сейчас: {(_pollRunning == 1 ? "да" : "нет")} | интервал: {PollInterval.TotalHours:0}ч";
        }

        public async Task<string> SetNextKindAsync(string key)
        {
            if (string.Equals(key, "clear", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "winlose", StringComparison.OrdinalIgnoreCase))
            {
                await _botState.UpdateStateAsync(s => s.NextPredictionKey = null);
                return "Следующая ставка: вин/луз.";
            }
            var kind = PredictionCatalog.Find(key);
            if (kind == null) return $"Неизвестный тип «{key}». Доступные: {ListKinds()}";
            await _botState.UpdateStateAsync(s => s.NextPredictionKey = kind.Key);
            return $"Следующая ставка: {kind.PollLabel} ({kind.Key}).";
        }

        public static string ListKinds() =>
            string.Join(", ", PredictionCatalog.Alternatives.Select(k => $"{k.Key}={k.PollLabel}"));

        #endregion

        private async Task UpdateDailyStats(bool won)
        {
            const int LowEloMaxLP = 100;
            var buffdata = await _riotApi.GetRankBySummonerAsync();
            if (buffdata == null) return;

            if (!int.TryParse(buffdata[1], out int bufflp))
            {
                _logger.LogError("UpdateDailyStats() -> cant convert LP to int. buffdata: {data}", string.Join(" ", buffdata));
                return;
            }

            string newRank = buffdata[0];
            string newTier = buffdata[2];
            bool isHighElo = newTier.Equals("master", StringComparison.OrdinalIgnoreCase) ||
                             newTier.Equals("grandmaster", StringComparison.OrdinalIgnoreCase) ||
                             newTier.Equals("challenger", StringComparison.OrdinalIgnoreCase);

            await _gameState.UpdateStateAsync(s =>
            {
                bool divisionChanged = newRank != s.Elo || newTier != s.Tier;

                if (won)
                {
                    if (!isHighElo)
                    {
                        // Promoted: finish the old division (100 LP) and add the LP in the new one.
                        s.EarnedLP += divisionChanged ? LowEloMaxLP - s.StartLP + bufflp : bufflp - s.StartLP;
                    }
                    else
                    {
                        bool promotedToMaster = string.Equals(s.Tier, "diamond", StringComparison.OrdinalIgnoreCase);
                        s.EarnedLP += promotedToMaster ? LowEloMaxLP - s.StartLP + bufflp : bufflp - s.StartLP;
                    }
                }
                else
                {
                    if (!isHighElo && divisionChanged)
                        s.EarnedLP -= s.StartLP + (LowEloMaxLP - bufflp); // Demoted
                    else
                        s.EarnedLP -= s.StartLP - bufflp;
                }

                s.StartLP = bufflp;
                s.Elo = newRank;
                s.Tier = newTier;
            });
        }
    }
}
