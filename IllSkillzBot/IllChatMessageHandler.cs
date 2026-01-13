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
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

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

        private const int HardTimeoutSec = 600;
        private const int TimeoutSec = 300;
        private const int LightTimeoutSec = 10;

        private readonly Channel<OnMessageReceivedArgs> _messageChannel;
        private const int SaveBufferCount = 20;

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
            IStreamElementsService streamElementsService
            )
        {
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
            _messageChannel = Channel.CreateUnbounded<OnMessageReceivedArgs>(new UnboundedChannelOptions
            {
                SingleReader = true, // We have one processing loop
                SingleWriter = true  // Only the IRC client writes
            });
        }
        public Task HandleMessage(OnMessageReceivedArgs e)
        {
            // Just push to queue.
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
                    try
                    {
                        await ProcessMessageInternal(e);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chat message from {User}", e.ChatMessage.Username);
                    }
                }
            }
        }
        public async Task ProcessMessageInternal(OnMessageReceivedArgs e)
        {
            var sw = Stopwatch.StartNew();
            if (e.ChatMessage.Username.Equals("streamelements", StringComparison.OrdinalIgnoreCase)) return;

            SaveToBuffer(e);
            var tracker = AddToTracker(e.ChatMessage.Username, e.ChatMessage.Message);
            var user = await GetAddUser(e.ChatMessage);
            if (user == null) return;

            user.messageCon++;
            await SaveBuffer(false);

            if (_botState.Current.IsSubActive)
            {
                if (_chatFilters.CheckBooB(e.ChatMessage.Message))
                {
                    await _twitchService.TimeOutUser(user, HardTimeoutSec, STRINGS.TimeOutBadPic);
                    await _database.UpdateUserAsync(user);
                    return;
                }

                if (_chatFilters.FilterASCII(e))
                {
                    await _twitchService.TimeOutUser(user, TimeoutSec, STRINGS.TimeOutPic);
                }

                if (await _chatFilters.ZapCheck(e.ChatMessage.Message, e.ChatMessage.DisplayName).ConfigureAwait(false))
                {
                    user = await _modInteractions.IllFilterTrigger(user, e.ChatMessage.Id);
                    await _database.UpdateUserAsync(user);
                    return;
                }

                await _chatFilters.DeleteLinks(user, e);

                if (CheckSpam(tracker, e.ChatMessage.Message))
                {
                    await _twitchService.TimeOutUser(user, LightTimeoutSec, STRINGS.TimeOutSpam);
                    await _database.UpdateUserAsync(user);
                    return;
                }

                string normalizedMsg = StringUtil.Normalize(e.ChatMessage.Message);
                if (normalizedMsg.Contains("хохол") || normalizedMsg.Contains("хахол"))
                {
                    await _twitchService.TimeOutUser(user, TimeoutSec, STRINGS.TimeOut1wReason);
                    await _database.UpdateUserAsync(user);
                    return;
                }

                if (_botState.Current.QuizIsRunning)
                    user = await _illGames.UserGuessAnswer(user, e.ChatMessage.Message);
                else
                    _illGames.QuizzActiveUser(user.TwitchID.ToString());
            }

            if (e.ChatMessage.Message.StartsWith("!"))
            {
                user = await _commandHandler.CommandHandler(user, e.ChatMessage.Message);
            }
            await _database.UpdateUserAsync(user);
            sw.Stop();
            if (_botState.Current.PerformanceDebugMode && _illAccess.Root(user))
            {
                double memoryUsed = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                string perfMsg = $"[Perf] Time: {sw.ElapsedMilliseconds}ms | RAM: {memoryUsed:F2} MB";
                await _streamElementsService.SendChatMessage(perfMsg).ConfigureAwait(false);
            }
            return;
        }

        private void SaveToBuffer(OnMessageReceivedArgs e)
        {
            _messagesBuffer.Enqueue(new MessageBuffer()
            {
                Message = e.ChatMessage.Message,
                TtvID = e.ChatMessage.UserId,
                Name = e.ChatMessage.Username,
                TimeStamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString()
            });
        }

        public async Task SaveBuffer(bool IsForced)
        {
            if (_messagesBuffer.IsEmpty) return;
            if (_messagesBuffer.Count < SaveBufferCount && !IsForced) return;

            List<MessageBuffer> temp = new List<MessageBuffer>();
            while (_messagesBuffer.TryDequeue(out var msg))
            {
                temp.Add(msg);
            }

            if (temp.Count > 0)
            {
                await _database.SaveMessagesAsync(temp);
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
        private async Task<UserObject> GetAddUser(ChatMessage chatmessage)
        {
            if (!int.TryParse(chatmessage.UserId, out int ttvid))
            {
                _logger.LogError("GetAddUser(): TtvID Conversion Error for user {Username}", chatmessage.Username);
                return null;
            }

            UserObject user = await _database.GetUserAsync(ttvid);

            bool needsUpdate = false;

            if (user.dbID == -404)
            {
                user.TwitchID = ttvid;
                needsUpdate = true;
            }

            if (user.Name != chatmessage.Username ||
                user.isSub != (chatmessage.UserDetail.IsSubscriber ? 1 : 0) ||
                user.isMod != (chatmessage.UserDetail.IsModerator ? 1 : 0) ||
                user.isVip != (chatmessage.UserDetail.IsVip ? 1 : 0))
            {
                needsUpdate = true;
            }

            user.Name = chatmessage.Username;
            user.isSub = chatmessage.UserDetail.IsSubscriber ? 1 : 0;
            user.isVip = chatmessage.UserDetail.IsVip ? 1 : 0;
            user.IsBroadcaster = chatmessage.IsBroadcaster ? 1 : 0;
            user.isMod = chatmessage.UserDetail.IsModerator ? 1 : 0;
            user.isPartner = chatmessage.UserDetail.IsPartner ? 1 : 0;

            if (needsUpdate)
            {
                await _database.AddOrUpdateUserAsync(user);
            }

            return user;
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
                    tracker.RecentMessages[0] = Guid.NewGuid().ToString();
                    tracker.RecentMessages[1] = Guid.NewGuid().ToString();
                    tracker.RecentMessages[2] = Guid.NewGuid().ToString();
                    tracker.RecentMessages[3] = Guid.NewGuid().ToString();
                    return true;
                }
            }
            return false;
        }
    }
}