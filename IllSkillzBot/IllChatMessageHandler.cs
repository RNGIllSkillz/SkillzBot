using F23.StringSimilarity;
using Microsoft.Extensions.Logging;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.IllSTRINGS;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    public class IllChatMessageHandler
    {
        private readonly ConcurrentDictionary<string, UserChatTracker> _userTrackers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<MessageBuffer> _messagesBuffer = new();
        private readonly NormalizedLevenshtein _levenshtein = new();
        private readonly ILogger<IllChatMessageHandler> _logger;
        private readonly IllChatFilters _chatFilters;
        private readonly IDatabaseService _database;
        private readonly IllCommandHandler _commandHandler;
        private readonly IllGames _illGames;
        private readonly ITwitchService _twitchService;
        private readonly IBotStateService _botState;
        private readonly IllModeratorsInteractions _modInteractions;
        private readonly IIllAccess _illAccess;
        private readonly IStreamElementsService _streamElementsService;
        private readonly Services.Vip.VipRegistryService _vips;
        private readonly Api.ChatFeed _feed;

        private long _totalMessagesProcessed = 0;
        private int _pendingMessageCount = 0;
        private long _lastLagAlertTicks = 0;
        private long _lastDbWarningTicks = 0;

        private const int HardTimeoutSec = 600;
        private const int TimeoutSec = 300;
        private const int LightTimeoutSec = 10;

        private readonly Channel<IncomingChatMessage> _messageChannel;
        private const int SaveBufferCount = 20;
        /// <summary>Upper bound for unsaved messages kept in memory while the database is down.</summary>
        private const int MaxBufferedMessages = 5000;
        private const int LagAlertThreshold = 5;
        private static readonly TimeSpan LagAlertInterval = TimeSpan.FromSeconds(30);

        // One message may never hold the loop: after MessageTimeout the loop moves on and the
        // message keeps running in the background. _stage names the step that was running.
        private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(30);
        private volatile string _stage = "idle";
        private long _stalledMessages;
        private volatile string _lastStall = "";

        private const string SPAM_MARKER = "___SPAM___";

        public IllChatMessageHandler
            (
            ILogger<IllChatMessageHandler> logger,
            IllChatFilters chatFilters,
            IDatabaseService database,
            IllCommandHandler commandHandler,
            IllGames illGames,
            IllCommands illCommands,
            ITwitchService twitchService,
            IBotStateService botState,
            IllModeratorsInteractions modInteractions,
            IIllAccess illAccess,
            IStreamElementsService streamElementsService,
            Services.Vip.VipRegistryService vips,
            Api.ChatFeed feed
            )
        {
            _vips = vips;
            _feed = feed;
            _logger = logger;
            _chatFilters = chatFilters;
            _database = database;
            _commandHandler = commandHandler;
            _illGames = illGames;
            _twitchService = twitchService;
            _botState = botState;
            _modInteractions = modInteractions;
            _illAccess = illAccess;
            _streamElementsService = streamElementsService;
            illCommands._chatStats = GetStats;
            _messageChannel = Channel.CreateUnbounded<IncomingChatMessage>(new UnboundedChannelOptions
            {
                SingleReader = true, // We have one processing loop
                SingleWriter = true  // Only the IRC client writes
            });
        }

        public Task HandleMessage(IncomingChatMessage e)
        {
            _feed.PublishIncoming(e);
            Interlocked.Increment(ref _pendingMessageCount);
            _messageChannel.Writer.TryWrite(e);
            return Task.CompletedTask;
        }

        public async Task StartProcessingLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Chat Message Processing Loop Started.");

            while (await _messageChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_messageChannel.Reader.TryRead(out var e))
                {
                    int currentPending = Interlocked.Decrement(ref _pendingMessageCount);
                    if (currentPending >= LagAlertThreshold)
                    {
                        long now = DateTime.UtcNow.Ticks;
                        if (now - Interlocked.Read(ref _lastLagAlertTicks) > LagAlertInterval.Ticks)
                        {
                            Interlocked.Exchange(ref _lastLagAlertTicks, now);
                            _logger.LogWarning("[LAG ALERT] Chat Queue is backing up! Pending messages: {Count}", currentPending);
                        }
                    }
                    var work = Task.Run(() => ProcessMessageInternal(e));
                    try
                    {
                        await work.WaitAsync(MessageTimeout);
                    }
                    catch (TimeoutException)
                    {
                        string stage = _stage;
                        Interlocked.Increment(ref _stalledMessages);
                        _lastStall = $"{stage} at {DateTime.UtcNow:HH:mm:ss}Z";
                        _logger.LogWarning("[STALL] Message from {User} still running after {Seconds}s at stage '{Stage}'; chat loop moves on, it continues in the background. Content: {Message}",
                            e.Login, (int)MessageTimeout.TotalSeconds, stage, e.Text);
                        var started = DateTime.UtcNow;
                        _ = work.ContinueWith(t =>
                        {
                            if (t.IsFaulted) _logger.LogError(t.Exception?.GetBaseException(), "Stalled message from {User} failed", e.Login);
                            else _logger.LogWarning("Stalled message from {User} finished after {Seconds}s more.", e.Login, (int)(DateTime.UtcNow - started).TotalSeconds);
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chat message from {User}", e.Login);
                    }
                }
            }
        }

        public async Task ProcessMessageInternal(IncomingChatMessage e)
        {
            var sw = Stopwatch.StartNew();
            if (e.Login.Equals("streamelements", StringComparison.OrdinalIgnoreCase)) return;

            SaveToBuffer(e);
            var tracker = AddToTracker(e.Login, e.Text);

            _stage = "load-user";
            UserObject user = await GetAddUser(e);
            if (user == null) return;

            user.messageCon++;
            try
            {
                _stage = "save-buffer";
                await SaveBuffer(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background save buffer failed.");
            }

            if (_botState.Current.IsSubActive)
            {
                _stage = "filters";
                if (_chatFilters.CheckBooB(e.Text))
                {
                    await _twitchService.TimeOutUser(user, HardTimeoutSec, STRINGS.TimeOutBadPic);
                    await SaveUserAsync(user);
                    return;
                }

                if (_chatFilters.FilterASCII(e))
                {
                    await _twitchService.TimeOutUser(user, TimeoutSec, STRINGS.TimeOutPic);
                }

                if (await _chatFilters.ZapCheck(e.Text, e.DisplayName).ConfigureAwait(false))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var updatedUser = await _modInteractions.IllFilterTrigger(user, e.Id);
                            await SaveUserAsync(updatedUser);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background moderation failed for {User}", user.Name);
                        }
                    });
                    return;
                }

                _stage = "links";
                await _chatFilters.DeleteLinks(user, e);
                _stage = "spam-phrase";

                if (CheckSpam(tracker, e.Text))
                {
                    await _twitchService.TimeOutUser(user, LightTimeoutSec, STRINGS.TimeOutSpam);
                    await SaveUserAsync(user);
                    return;
                }

                if (_chatFilters.ContainsBlockedPhrase(e.Text))
                {
                    await _twitchService.TimeOutUser(user, TimeoutSec, STRINGS.TimeOut1wReason);
                    await SaveUserAsync(user);
                    return;
                }

                _stage = "quiz";
                if (_botState.Current.QuizIsRunning)
                    user = await _illGames.UserGuessAnswer(user, e.Text);
                else
                    _illGames.QuizzActiveUser(user.TwitchID.ToString());
            }

            if (e.Text.StartsWith("!"))
            {
                int space = e.Text.IndexOf(' ');
                _stage = "command " + (space > 0 ? e.Text.Substring(0, space) : e.Text);
                user = await _commandHandler.CommandHandler(user, e.Text);
            }
            _stage = "save-user";
            await SaveUserAsync(user);
            _stage = "idle";

            sw.Stop();
            Interlocked.Increment(ref _totalMessagesProcessed);
            // Commands that call the Riot API take ~1s through the proxy; only longer stalls are worth a warning.
            if (sw.ElapsedMilliseconds > 1500)
            {
                _logger.LogWarning("[SLOW OP] Message from {User} took {Time}ms to process. Content: {Message}",
                    e.Login, sw.ElapsedMilliseconds, e.Text);
            }
            else if (sw.ElapsedMilliseconds > 500)
            {
                _logger.LogDebug("[SLOW OP] Message from {User} took {Time}ms to process. Content: {Message}",
                    e.Login, sw.ElapsedMilliseconds, e.Text);
            }

            if (_botState.Current.PerformanceDebugMode && _illAccess.Root(user))
            {
                double memoryUsed = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                string perfMsg = $"[Perf] Time: {sw.ElapsedMilliseconds}ms | RAM: {memoryUsed:F2} MB";
                await _streamElementsService.SendChatMessage(perfMsg).ConfigureAwait(false);
            }
        }

        /// <summary>Persists the user unless it is a transient fallback object; never throws.</summary>
        private async Task SaveUserAsync(UserObject user)
        {
            if (user == null || user.IsTransient) return;
            try
            {
                await _database.UpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                WarnDatabaseDown(ex, "Failed to persist user {User}", user.Name);
            }
        }

        private void WarnDatabaseDown(Exception ex, string message, params object[] args)
        {
            // The same failure repeats for every message while the DB is down; log it at most once a minute.
            long now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _lastDbWarningTicks) > TimeSpan.FromMinutes(1).Ticks)
            {
                Interlocked.Exchange(ref _lastDbWarningTicks, now);
                _logger.LogError(ex, message, args);
            }
        }

        private void SaveToBuffer(IncomingChatMessage e)
        {
            _messagesBuffer.Enqueue(new MessageBuffer()
            {
                Message = e.Text,
                TtvID = e.UserId,
                Name = e.Login,
                TimeStamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString()
            });

            // Bound memory while the database is unreachable: drop the oldest entries.
            while (_messagesBuffer.Count > MaxBufferedMessages && _messagesBuffer.TryDequeue(out _)) { }
        }

        public async Task SaveBuffer(bool IsForced)
        {
            if (_messagesBuffer.IsEmpty) return;
            if (_messagesBuffer.Count < SaveBufferCount && !IsForced) return;

            var batch = new List<MessageBuffer>();
            while (_messagesBuffer.TryDequeue(out var msg))
            {
                batch.Add(msg);
            }
            if (batch.Count == 0) return;

            try
            {
                await _database.SaveMessagesAsync(batch);
            }
            catch (Exception)
            {
                // Put the batch back so it is retried on the next flush instead of being lost.
                foreach (var msg in batch) _messagesBuffer.Enqueue(msg);
                throw;
            }
        }

        private UserChatTracker AddToTracker(string username, string message)
        {
            var tracker = _userTrackers.GetOrAdd(username, key => new UserChatTracker { Username = key });

            lock (tracker)
            {
                tracker.AddMessage(message);
            }
            return tracker;
        }

        public void PruneTrackers()
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var keysToRemove = new List<string>();

            foreach (var kvp in _userTrackers)
            {
                if (now - kvp.Value.LastMessageTimestamp > 600)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _userTrackers.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 10)
            {
                _logger.LogDebug("Pruned {Count} inactive user trackers.", keysToRemove.Count);
            }
        }

        /// <summary>
        /// Loads the user from the database, creating or refreshing the row as needed.
        /// When the database is unreachable a transient user is built from the chat
        /// metadata so moderation and commands keep working.
        /// </summary>
        private async Task<UserObject> GetAddUser(IncomingChatMessage chatmessage)
        {
            if (!long.TryParse(chatmessage.UserId, out long ttvid))
            {
                _logger.LogError("GetAddUser(): TtvID Conversion Error for user {Username}", chatmessage.Login);
                return null;
            }

            UserObject user;
            try
            {
                user = await _database.GetUserAsync(ttvid);
            }
            catch (Exception ex)
            {
                WarnDatabaseDown(ex, "Database unreachable; processing {User} with a transient profile.", chatmessage.Login);
                return BuildTransientUser(chatmessage, ttvid);
            }

            bool needsUpdate = false;

            if (user.dbID == -404)
            {
                user.TwitchID = ttvid;
                needsUpdate = true;
            }

            if (user.Name != chatmessage.Login ||
                user.isSub != (chatmessage.IsSubscriber ? 1 : 0) ||
                user.isMod != (chatmessage.IsModerator ? 1 : 0) ||
                user.isVip != (chatmessage.IsVip ? 1 : 0))
            {
                needsUpdate = true;
            }

            int wasVip = user.isVip;
            ApplyChatMetadata(user, chatmessage);
            if (needsUpdate && wasVip != user.isVip && !user.IsTransient)
                _ = _vips.NoteChatBadgeAsync(user, user.isVip == 1);

            if (needsUpdate)
            {
                try
                {
                    await _database.AddOrUpdateUserAsync(user);
                }
                catch (Exception ex)
                {
                    WarnDatabaseDown(ex, "Failed to add/update user {User}; continuing with a transient profile.", chatmessage.Login);
                    user.IsTransient = true;
                }
            }

            return user;
        }

        private static UserObject BuildTransientUser(IncomingChatMessage chatmessage, long ttvid)
        {
            var user = new UserObject { dbID = -404, TwitchID = ttvid, IsTransient = true };
            ApplyChatMetadata(user, chatmessage);
            return user;
        }

        private static void ApplyChatMetadata(UserObject user, IncomingChatMessage chatmessage)
        {
            user.Name = chatmessage.Login;
            user.isSub = chatmessage.IsSubscriber ? 1 : 0;
            user.isVip = chatmessage.IsVip ? 1 : 0;
            user.IsBroadcaster = chatmessage.IsBroadcaster ? 1 : 0;
            user.isMod = chatmessage.IsModerator ? 1 : 0;
            user.isPartner = chatmessage.IsPartner ? 1 : 0;
        }

        private bool CheckSpam(UserChatTracker tracker, string currentMessage)
        {
            lock (tracker)
            {
                double sim1 = _levenshtein.Distance(tracker.RecentMessages[0], tracker.RecentMessages[1]);
                if (sim1 >= 0.4) return false;

                double sim2 = _levenshtein.Distance(tracker.RecentMessages[1], tracker.RecentMessages[2]);
                if (sim2 >= 0.4) return false;

                double sim3 = _levenshtein.Distance(tracker.RecentMessages[2], tracker.RecentMessages[3]);

                bool isSpam = false;
                if (currentMessage.Length < 118)
                {
                    if (sim3 < 0.4 && tracker.SecondsSinceLastMessage < 5)
                    {
                        isSpam = true;
                    }
                }
                else
                {
                    if (tracker.SecondsSinceLastMessage < 10)
                    {
                        isSpam = true;
                    }
                }

                if (isSpam)
                {
                    tracker.RecentMessages[0] = SPAM_MARKER;
                    tracker.RecentMessages[1] = SPAM_MARKER;
                    tracker.RecentMessages[2] = SPAM_MARKER;
                    tracker.RecentMessages[3] = SPAM_MARKER;
                    return true;
                }
            }
            return false;
        }

        public (int Pending, long Processed, int Buffered, long Stalled, string LastStall) GetStats()
        {
            return (_pendingMessageCount, Interlocked.Read(ref _totalMessagesProcessed), _messagesBuffer.Count, Interlocked.Read(ref _stalledMessages), _lastStall);
        }
    }
}
