using Camille.Enums;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.SpectatorV5;
using Participant = Camille.RiotGames.MatchV5.Participant;
using Microsoft.Extensions.Logging;
using SkillzBot.API.RiotGames;
using SkillzBot.IllConfiguration;
using SkillzBot.IllSkillzBot.Predictions;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api.Core.Enums;

namespace SkillzBot.IllSkillzBot
{
    /// <summary>
    /// Auto-predictions for the streamer's League games. The default is win/lose; after a
    /// game, at most once per <see cref="PollInterval"/>, chat picks the type of the next
    /// prediction through a Twitch poll (win/lose plus three weighted random alternatives).
    /// The prediction being tracked is persisted so a restart mid-game resumes and resolves it.
    /// </summary>
    public class IllPredictions
    {
        private const int MaxGameLengthSec = 5400;
        private const int PredictionWindowSec = 180;
        private const int NewGameMaxLengthSec = 30;
        private const int RemakeThresholdSec = 300;
        private const int PollDurationSec = 120;
        private const int PollReminderAfterSec = PollDurationSec / 2;
        // The next poll is due after a random amount of *live* time (offline time does not count).
        private static readonly TimeSpan MinLiveBetweenPolls = TimeSpan.FromHours(3);
        private static readonly TimeSpan MaxLiveBetweenPolls = TimeSpan.FromHours(5);
        private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan StaleMatchGrace = TimeSpan.FromHours(1);
        private static readonly TimeSpan StalePollAge = TimeSpan.FromDays(1);

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
        private readonly DateTime _processStartedUtc = DateTime.UtcNow;
        private DateTime _lastRecoveryAttemptUtc = DateTime.MinValue;
        private bool _recoveryAnnounced;
        private bool _pollRecoveryChecked;

        /// <summary>In-memory view of the persisted <see cref="ActivePredictionState"/>.</summary>
        private sealed class ActivePrediction
        {
            public PredictionKind Kind;
            public string MatchId;
            public string PredictionId;
            public DateTime StartedUtc;
            public bool StatsRecorded;
            /// <summary>Outcome title used for each champion id, so resolution matches what Twitch shows.</summary>
            public Dictionary<int, string> OutcomeByChampion;

            public ActivePredictionState ToState() => new ActivePredictionState
            {
                MatchId = MatchId,
                PredictionId = PredictionId,
                KindKey = Kind.Key,
                StartedUtc = StartedUtc,
                StatsRecorded = StatsRecorded,
                Outcomes = OutcomeByChampion?.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
            };

            public static ActivePrediction FromState(ActivePredictionState s) => new ActivePrediction
            {
                Kind = PredictionCatalog.Find(s.KindKey) ?? PredictionCatalog.WinLose,
                MatchId = s.MatchId,
                PredictionId = s.PredictionId,
                StartedUtc = s.StartedUtc,
                StatsRecorded = s.StatsRecorded,
                OutcomeByChampion = s.Outcomes?
                    .Where(kv => int.TryParse(kv.Key, out _))
                    .ToDictionary(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture), kv => kv.Value),
            };
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

        /// <summary>Called every few seconds by the monitoring service.</summary>
        public async Task GetCurrentMatchTask()
        {
            if (_botState.Current.Debug) _logger.LogDebug("Running GetCurrentMatchTask()");
            if (!_botState.Current.IsSubActive) return;

            if (!_pollRecoveryChecked)
            {
                _pollRecoveryChecked = true;
                RecoverActivePoll();
            }

            // An owed resolution (after a restart or a failed attempt) comes before anything else.
            if (_botState.Current.ActivePrediction != null)
            {
                await ResumeActivePredictionAsync();
                return;
            }

            if (_botState.Current.InMatch || !_botState.Current.AutoPred) return;

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
            if (_currentMatchId == matchId) return;
            _currentMatchId = matchId;

            if (currentGame.GameLength > NewGameMaxLengthSec)
            {
                _logger.LogInformation("Game {MatchId} already in progress ({Length}s); the prediction window has passed, no prediction for this game.", matchId, currentGame.GameLength);
                return;
            }
            _logger.LogInformation("New game detected: {MatchId} ({Mode}, {Players} players).", matchId, currentGame.GameMode, currentGame.Participants?.Length ?? 0);

            var predictions = await _twitchService.GetCurrentPredPublic();
            if (predictions == null || predictions.Data.Length == 0) return;
            var lastStatus = predictions.Data.First().Status;
            if (lastStatus != PredictionStatus.RESOLVED && lastStatus != PredictionStatus.CANCELED)
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
                var active = await StartPredictionAsync(kind, currentGame, matchId);
                await PersistActiveAsync(active);
                await ContinueAsync(active);
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

        private Task PersistActiveAsync(ActivePrediction active) =>
            _botState.UpdateStateAsync(s => s.ActivePrediction = active.ToState());

        private Task ClearActiveAsync() => _botState.UpdateStateAsync(s => s.ActivePrediction = null);

        /// <summary>Takes the chat-chosen type for this game (one-shot) or falls back to win/lose.</summary>
        private async Task<PredictionKind> ConsumeNextKindAsync()
        {
            string key = _botState.Current.NextPredictionKey;
            var kind = PredictionCatalog.Find(key) ?? PredictionCatalog.WinLose;
            if (!string.IsNullOrEmpty(key))
            {
                _logger.LogInformation("Chat-chosen prediction type {Key} is used for this game.", kind.Key);
                await _botState.UpdateStateAsync(s => s.NextPredictionKey = null);
            }
            return kind;
        }

        #endregion

        #region Restart recovery

        /// <summary>
        /// Picks up the persisted prediction after a restart (or a failed attempt): re-attaches
        /// to the Twitch prediction, then waits for or fetches the match and resolves it.
        /// </summary>
        private async Task ResumeActivePredictionAsync()
        {
            if (DateTime.UtcNow - _lastRecoveryAttemptUtc < RecoveryRetryDelay) return;
            _lastRecoveryAttemptUtc = DateTime.UtcNow;

            var state = _botState.Current.ActivePrediction;
            if (state == null) return;
            var active = ActivePrediction.FromState(state);

            // Only a record created before this process started means the bot actually restarted mid-game.
            if (!_recoveryAnnounced && active.StartedUtc < _processStartedUtc)
            {
                _recoveryAnnounced = true;
                _logger.LogInformation("Resuming tracked match {MatchId} ({Kind}, started {Age} ago, prediction {PredictionId}).",
                    active.MatchId, active.Kind.Key, Services.HealthState.FormatAge(DateTime.UtcNow - active.StartedUtc), active.PredictionId ?? "none");
                await _ircClient.SendMessage($"Бот перезапустился, продолжаю следить за матчем (ставка «{active.Kind.PollLabel}»).");
            }

            if (active.PredictionId != null)
            {
                PredictionStatus? status;
                try
                {
                    status = await _twitchService.GetPredictionStatusAsync(active.PredictionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read prediction {PredictionId} status; will retry.", active.PredictionId);
                    return;
                }
                if (status == null)
                {
                    _logger.LogWarning("Prediction {PredictionId} was not found on Twitch; tracking match {MatchId} for stats only.", active.PredictionId, active.MatchId);
                    active.PredictionId = null;
                }
                else if (status == PredictionStatus.RESOLVED || status == PredictionStatus.CANCELED)
                {
                    _logger.LogInformation("Prediction {PredictionId} is already {Status}; tracking match {MatchId} for stats only.", active.PredictionId, status, active.MatchId);
                    active.PredictionId = null;
                }
                else if (!await _twitchService.AdoptPredictionAsync(active.PredictionId))
                {
                    _logger.LogWarning("Could not re-attach to prediction {PredictionId}; will retry.", active.PredictionId);
                    return;
                }
                if (active.PredictionId == null) await PersistActiveAsync(active);
            }

            _currentMatchId = active.MatchId;
            await SetInMatchAsync(true);
            try
            {
                await ContinueAsync(active);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resumed tracking of {MatchId} failed; will retry.", active.MatchId);
            }
            finally
            {
                await SetInMatchAsync(false);
            }
        }

        private void RecoverActivePoll()
        {
            var poll = _botState.Current.ActivePoll;
            if (poll == null) return;

            var age = DateTime.UtcNow - poll.StartedUtc;
            if (age > StalePollAge || string.IsNullOrEmpty(poll.PollId))
            {
                _logger.LogWarning("Dropping stale poll record {PollId} ({Age} old).", poll.PollId, Services.HealthState.FormatAge(age));
                _ = _botState.UpdateStateAsync(s => s.ActivePoll = null);
                return;
            }

            var options = (poll.OptionKeys ?? new List<string>()).Select(PredictionCatalog.Find).Where(k => k != null).ToList();
            double remaining = Math.Max(0, PollDurationSec + 5 - age.TotalSeconds);
            _logger.LogInformation("Resuming poll {PollId} after restart ({Remaining:F0}s left).", poll.PollId, remaining);
            Interlocked.Exchange(ref _pollRunning, 1);
            _ = Task.Run(() => FinishPollAsync(poll.PollId, options, TimeSpan.FromSeconds(remaining)));
        }

        #endregion

        #region Prediction lifecycle

        /// <summary>Creates the Twitch prediction for the requested type, or win/lose when the game does not fit it.</summary>
        private async Task<ActivePrediction> StartPredictionAsync(PredictionKind kind, CurrentGameInfo game, string matchId)
        {
            if (kind.Scope == PredictionScope.WinLose)
                return await StartWinLoseAsync(matchId);

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
                        {
                            var predictionId = await CreateOnTwitchAsync(() => _twitchService.Start_2_Prediction(kind.Title, StreamerLabel, PredictionCatalog.OpponentLabel, PredictionWindowSec).AsTask());
                            _logger.LogInformation("Prediction started: {Key} ({Title}) id {PredictionId}.", kind.Key, kind.Title, predictionId ?? "none");
                            return NewActive(kind, matchId, predictionId, null);
                        }

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

                            var predictionId = await CreateOnTwitchAsync(() => expected == 5
                                ? _twitchService.Start_5_Prediction(titles, kind.Title, PredictionWindowSec).AsTask()
                                : _twitchService.Start_10_Prediction(titles, kind.Title, PredictionWindowSec).AsTask());
                            _logger.LogInformation("Prediction started: {Key} ({Title}) id {PredictionId} with outcomes {Outcomes}.", kind.Key, kind.Title, predictionId ?? "none", string.Join(", ", titles));
                            return NewActive(kind, matchId, predictionId, outcomes);
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
            return await StartWinLoseAsync(matchId);
        }

        private async Task<ActivePrediction> StartWinLoseAsync(string matchId)
        {
            var predictionId = await CreateOnTwitchAsync(() => _twitchService.Start_2_Prediction(PredictionCatalog.WinLose.Title, "вин", "луз", PredictionWindowSec).AsTask());
            _logger.LogInformation("Prediction started: winlose ({Title}) id {PredictionId} for {MatchId}.", PredictionCatalog.WinLose.Title, predictionId ?? "none", matchId);
            return NewActive(PredictionCatalog.WinLose, matchId, predictionId, null);
        }

        /// <summary>
        /// Runs a Start_* call and returns the id of the prediction it created, or null when
        /// Twitch did not create one (so a stale earlier prediction is never resolved by mistake).
        /// </summary>
        private async Task<string> CreateOnTwitchAsync(Func<Task> start)
        {
            string before = _twitchService.CurrentPredictionId;
            await start();
            string after = _twitchService.CurrentPredictionId;
            if (string.IsNullOrEmpty(after) || after == before)
            {
                _logger.LogError("Twitch did not create the prediction; the game will be tracked for stats only.");
                return null;
            }
            return after;
        }

        private static ActivePrediction NewActive(PredictionKind kind, string matchId, string predictionId, Dictionary<int, string> outcomes) => new ActivePrediction
        {
            Kind = kind,
            MatchId = matchId,
            PredictionId = predictionId,
            StartedUtc = DateTime.UtcNow,
            OutcomeByChampion = outcomes,
        };

        /// <summary>Waits for the match to finish, records stats, resolves the prediction and clears the record.</summary>
        private async Task ContinueAsync(ActivePrediction active)
        {
            long deadline = new DateTimeOffset(active.StartedUtc).ToUnixTimeSeconds() + MaxGameLengthSec;
            var match = await WaitForMatchEndAsync(active.MatchId, deadline);

            if (match == null)
            {
                var age = DateTime.UtcNow - active.StartedUtc;
                if (age < TimeSpan.FromSeconds(MaxGameLengthSec) + StaleMatchGrace)
                {
                    // Deadline passed but the match may still be unavailable for a while; let the next tick retry.
                    _logger.LogWarning("Match {MatchId} is not finished after {Age}; will keep checking.", active.MatchId, Services.HealthState.FormatAge(age));
                    return;
                }
                _logger.LogWarning("Match {MatchId} never showed up as finished after {Age}; giving up.", active.MatchId, Services.HealthState.FormatAge(age));
                await _ircClient.SendMessage($"Кажется я забаговал. Матч {active.MatchId} так и не завершился. Прекращаю отслеживать" + (active.PredictionId != null ? ", ставка отменена." : "."));
                if (active.PredictionId != null) await _twitchService.CencelePrediction();
                await ClearActiveAsync();
                return;
            }

            var participant = _riotApi.GetParticipantByMatch(match);
            if (participant == null)
            {
                await _botState.UpdateStateAsync(s => s.AutoPred = false);
                _logger.LogCritical("Participant could not be found in match {MatchId}. Auto-predictions disabled.", active.MatchId);
                await _ircClient.SendMessage("Критическая ошибка: не удалось найти призывателя в матче. Автоставки выключены.");
                await ClearActiveAsync();
                return;
            }

            if (match.Info.GameDuration <= RemakeThresholdSec)
            {
                await _ircClient.SendMessage("Матч отменен. Ставка будет отменена.");
                if (active.PredictionId != null) await _twitchService.CencelePrediction();
                await ClearActiveAsync();
                return;
            }

            bool won = participant.Win;
            _logger.LogInformation("Match {MatchId} finished: {Result} after {Duration} ({Kind}, prediction {PredictionId}).",
                active.MatchId, won ? "win" : "loss", Services.HealthState.FormatAge(TimeSpan.FromSeconds(match.Info.GameDuration)), active.Kind.Key, active.PredictionId ?? "none");

            if (!active.StatsRecorded)
            {
                await _gameState.UpdateStateAsync(s =>
                {
                    s.NumGames++;
                    if (won) s.NumWins++;
                    else s.NumLosses++;
                });
                await UpdateDailyStats(won);
                active.StatsRecorded = true;
                await PersistActiveAsync(active);
            }

            if (active.PredictionId != null)
                await ResolveAsync(active, match, participant, won);
            else
                _logger.LogInformation("Match {MatchId} finished ({Result}); no prediction was attached.", active.MatchId, won ? "win" : "loss");

            await ClearActiveAsync();
            await MaybeStartPollAsync();
        }

        /// <summary>
        /// Fetches the match until it shows up as finished. Always tries at least once, so a
        /// long-finished match resolves immediately after a restart; returns null past the deadline.
        /// </summary>
        private async Task<Match> WaitForMatchEndAsync(string matchId, long deadlineUnix)
        {
            int consecutiveErrors = 0;
            while (true)
            {
                Match match = null;
                try
                {
                    match = await _riotApi.GetMatchAsync(matchId);
                    if (consecutiveErrors != 0)
                    {
                        _logger.LogInformation("Recovered from {ErrorCount} consecutive Riot API errors", consecutiveErrors);
                        consecutiveErrors = 0;
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogError(ex, "GetMatchAsync failed ({Count} in a row) for {MatchId}", consecutiveErrors, matchId);
                }

                if (match != null) return match;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > deadlineUnix) return null;

                // 4s between normal polls; errors back off up to 30s.
                int delayMs = consecutiveErrors == 0 ? 4000 : Math.Min(30000, 2000 * (1 << Math.Min(consecutiveErrors, 4)));
                await Task.Delay(delayMs);
            }
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
                _logger.LogInformation("Prediction {PredictionId} resolved: {Outcome}.", active.PredictionId, won ? "вин" : "луз");
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
                                _logger.LogInformation("Prediction {PredictionId} resolved: {Key} streamer {StreamerValue} vs {Opponent} {OpponentValue} -> {Winner}.",
                                    active.PredictionId, kind.Key, result.StreamerValue, opponentName, result.OpponentValue, streamerWins ? "streamer" : "opponent");
                                await _ircClient.SendMessage(
                                    $"Итог ставки «{kind.PollLabel}»: {StreamerLabel} {PredictionCatalog.FormatValue(result.StreamerValue, kind.Metric)} vs {opponentName} {PredictionCatalog.FormatValue(result.OpponentValue, kind.Metric)}. " +
                                    (streamerWins ? "Стример доминировал на лайне PogChamp" : "git gud"));
                                break;
                            case LaneOutcome.Tie:
                                _logger.LogWarning("Prediction {PredictionId} canceled: lane tie ({Key}).", active.PredictionId, kind.Key);
                                await _ircClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                                await _twitchService.CencelePrediction();
                                break;
                            default:
                                _logger.LogWarning("Prediction {PredictionId} canceled: lane opponent not found ({Key}).", active.PredictionId, kind.Key);
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
                            _logger.LogWarning("Prediction {PredictionId} canceled: tie ({Key}).", active.PredictionId, kind.Key);
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
                        _logger.LogInformation("Prediction {PredictionId} resolved: {Key} winner {Outcome} ({Value}) -> {EndResult}.", active.PredictionId, kind.Key, outcomeTitle, result.WinnerValue, endResult);
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

        private static double LiveSecondsSinceLastPoll(BotStateModel s)
        {
            double total = s.LiveSecondsBank;
            if (s.BroadcasterIsOnline && s.LiveSinceUtc.HasValue)
                total += Math.Max(0, (DateTime.UtcNow - s.LiveSinceUtc.Value).TotalSeconds);
            return total;
        }

        private static double RequiredLiveSeconds(BotStateModel s) =>
            s.NextPollAfterLiveSec > 0 ? s.NextPollAfterLiveSec : TimeSpan.FromHours(4).TotalSeconds;

        private static double DrawNextPollThresholdSeconds() =>
            Random.Shared.Next((int)MinLiveBetweenPolls.TotalSeconds, (int)MaxLiveBetweenPolls.TotalSeconds + 1);

        private async Task MaybeStartPollAsync()
        {
            var s = _botState.Current;
            if (!s.PredictionPollEnabled || !s.AutoPred || !s.IsSubActive || !s.BroadcasterIsOnline) return;
            if (LiveSecondsSinceLastPoll(s) < RequiredLiveSeconds(s)) return;
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

                // Restart the live-time counter and draw when the next poll becomes due.
                await _botState.UpdateStateAsync(st =>
                {
                    var now = DateTime.UtcNow;
                    st.LastPredictionPollUtc = now;
                    st.LiveSecondsBank = 0;
                    st.LiveSinceUtc = st.BroadcasterIsOnline ? now : null;
                    st.NextPollAfterLiveSec = DrawNextPollThresholdSeconds();
                });

                var pollId = await _twitchService.CreatePollAsync(PredictionCatalog.PollTitle, choices, PollDurationSec);
                if (pollId == null)
                {
                    _logger.LogError("Prediction poll could not be created. The token needs channel:manage:polls and the channel must be affiliate/partner.");
                    return "Не удалось создать опрос (см. лог).";
                }

                await _botState.UpdateStateAsync(st => st.ActivePoll = new ActivePollState
                {
                    PollId = pollId,
                    StartedUtc = DateTime.UtcNow,
                    OptionKeys = options.Select(o => o.Key).ToList(),
                });

                _logger.LogInformation("Prediction poll {PollId} started with options: {Options}", pollId, string.Join(" | ", choices));
                await AnnouncePollStartAsync(choices);

                handedOff = true;
                _ = Task.Run(() => RemindPollAsync(pollId));
                _ = Task.Run(() => FinishPollAsync(pollId, options, TimeSpan.FromSeconds(PollDurationSec + 5)));
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

        /// <summary>Same style as the prediction-start notice: three identical lines back to back, then the options.</summary>
        private async Task AnnouncePollStartAsync(List<string> choices)
        {
            for (int i = 0; i < 3; i++)
            {
                await _ircClient.SendMessage("Опрос на некст ставку запущен PopNemo PopNemo PopNemo");
                await Task.Delay(100);
            }
            await _ircClient.SendMessage($"{PredictionCatalog.PollTitle} Варианты: {string.Join(" | ", choices)}. Голосуем {PollDurationSec / 60} мин, опрос над чатом.");
        }

        /// <summary>Halfway through the poll, reminds chat that it is still open.</summary>
        private async Task RemindPollAsync(string pollId)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollReminderAfterSec));
                if (_pollRunning != 1 || _botState.Current.ActivePoll?.PollId != pollId) return;
                await _ircClient.SendMessage($"Опрос еще идет, осталось {PollDurationSec - PollReminderAfterSec} с: {PredictionCatalog.PollTitle} Голосуй над чатом PopNemo");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Poll reminder failed: {Error}", ex.Message);
            }
        }

        private async Task FinishPollAsync(string pollId, List<PredictionKind> options, TimeSpan wait)
        {
            try
            {
                if (wait > TimeSpan.Zero) await Task.Delay(wait);

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
                    _logger.LogInformation("Poll {PollId}: nobody voted, next prediction stays winlose.", pollId);
                    await _botState.UpdateStateAsync(s => s.NextPredictionKey = null);
                    await _ircClient.SendMessage("Никто не проголосовал, остается вин/луз.");
                    return;
                }

                var kind = options.FirstOrDefault(o => o.PollLabel.Equals(winner.Title, StringComparison.OrdinalIgnoreCase))
                           ?? PredictionCatalog.FindByPollLabel(winner.Title)
                           ?? PredictionCatalog.WinLose;

                await _botState.UpdateStateAsync(s => s.NextPredictionKey = kind.Scope == PredictionScope.WinLose ? null : kind.Key);
                _logger.LogInformation("Poll {PollId}: chat chose {Key} for the next game.", pollId, kind.Key);

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
                await _botState.UpdateStateAsync(s => s.ActivePoll = null);
                Interlocked.Exchange(ref _pollRunning, 0);
            }
        }

        public string DescribePollState()
        {
            var s = _botState.Current;
            string lastPoll = s.LastPredictionPollUtc == DateTime.MinValue ? "never" : Services.HealthState.FormatAge(DateTime.UtcNow - s.LastPredictionPollUtc) + " ago";
            var next = PredictionCatalog.Find(s.NextPredictionKey) ?? PredictionCatalog.WinLose;
            string tracking = s.ActivePrediction == null
                ? "нет"
                : $"{s.ActivePrediction.MatchId} ({(PredictionCatalog.Find(s.ActivePrediction.KindKey) ?? PredictionCatalog.WinLose).PollLabel}, {Services.HealthState.FormatAge(DateTime.UtcNow - s.ActivePrediction.StartedUtc)})";
            string live = $"{Services.HealthState.FormatAge(TimeSpan.FromSeconds(LiveSecondsSinceLastPoll(s)))} из {Services.HealthState.FormatAge(TimeSpan.FromSeconds(RequiredLiveSeconds(s)))}";
            return $"Опросы: {(s.PredictionPollEnabled ? "on" : "off")} | следующая ставка: {next.PollLabel} | последний опрос: {lastPoll} | эфира с тех пор: {live} | идет сейчас: {(_pollRunning == 1 ? "да" : "нет")} | трекинг матча: {tracking}";
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
