using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using SkillzBot.API.Twitch;
using SkillzBot.MODELS;
using SkillzBot.IllSTRINGS;
using SkillzBot.IllSkillzBot.IllCommandsNest;
using SkillzBot.Singleton;
using SkillzBot.Hosts;
using SkillzBot.Utils;
using F23.StringSimilarity;
using SkillzBot.IRC;
using SkillzBot.Interfaces;

namespace SkillzBot.IllSkillzBot
{
    internal class IllChatMessageHandler
    {
        private readonly ConcurrentDictionary<string, UserChatTracker> _userTrackers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<MessageBuffer> _messagesBuffer = new();
        private readonly NormalizedLevenshtein _levenshtein = new();
        private readonly ILogger<IllChatMessageHandler> _logger;
        private readonly IllChatFilters _chatFilters;
        private readonly ITtvIRCClient _ircClient;
        private readonly IDatabaseService _database;
        private readonly IllCommandHandler _commandHandler;
        private readonly IllGames _illGames;
        private readonly IllCommands _illCommands;

        private const int HardTimeoutSec = 600;
        private const int TimeoutSec = 300;
        private const int LightTimeoutSec = 10;
        private const int SaveBufferCount = 100;

        public IllChatMessageHandler(ILogger<IllChatMessageHandler> logger, IllChatFilters chatFilters, ITtvIRCClient ircClient, IDatabaseService database, IllCommandHandler commandHandler, IllGames illGames, IllCommands illCommands)
        {
            _logger = logger;
            _chatFilters = chatFilters;
            _ircClient = ircClient;
            _database = database;
            _commandHandler = commandHandler;
            _illGames = illGames;
            _illCommands = illCommands;
        }

        public async Task<UserObject> MessageHandler(OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Username.Equals("streamelements", StringComparison.OrdinalIgnoreCase)) return null;

            SaveToBuffer(e);
            var tracker = AddToTracker(e.ChatMessage.Username, e.ChatMessage.Message);
            var user = await GetAddUser(e.ChatMessage).ConfigureAwait(false);
            if (user == null) return null;

            user.messageCon++;
            await SaveBuffer(false);

            if (IllSingleton.State.isSubActive)
            {
                if (_chatFilters.CheckBooB(e.ChatMessage.Message))
                {
                    await TtvAPI.TimeOutUser(user, HardTimeoutSec, STRINGS.TimeOutBadPic).ConfigureAwait(false);
                    return user;
                }

                if (_chatFilters.FilterASCII(e))
                {
                    await TtvAPI.TimeOutUser(user, TimeoutSec, STRINGS.TimeOutPic).ConfigureAwait(false);
                }

                if (await _chatFilters.ZapCheck(e.ChatMessage.Message, e.ChatMessage.DisplayName).ConfigureAwait(false))
                {
                    return await _illCommands.IllFilterTrigger(user, e.ChatMessage.Id).ConfigureAwait(false);
                }

                await _chatFilters.DeleteLinks(user, e).ConfigureAwait(false);

                if (CheckSpam(tracker, e.ChatMessage.Message))
                {
                    await TtvAPI.TimeOutUser(user, LightTimeoutSec, STRINGS.TimeOutSpam).ConfigureAwait(false);
                    return user;
                }

                string normalizedMsg = StringUtil.Normalize(e.ChatMessage.Message);
                if (normalizedMsg.Contains("хохол") || normalizedMsg.Contains("хахол"))
                {
                    await TtvAPI.TimeOutUser(user, TimeoutSec, STRINGS.TimeOut1wReason).ConfigureAwait(false);
                    return user;
                }

                if (IllSingleton.State.QuizIsRunning)
                    user = await _illGames.UserGuessAnswer(user, e.ChatMessage.Message).ConfigureAwait(false);
                else
                    _illGames.QuizzActiveUser(user.TwitchID.ToString());
            }

            if (e.ChatMessage.Message.StartsWith("!"))
            {
                user = await _commandHandler.CommandHandler(user, e.ChatMessage.Message).ConfigureAwait(false);
            }

            return user;
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
                await _database.SaveMessagesAsync(temp).ConfigureAwait(false);
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

        private async Task<UserObject> GetAddUser(ChatMessage chatmessage)
        {
            if (!int.TryParse(chatmessage.UserId, out int ttvid))
            {
                _logger.LogError("GetAddUser(): TtvID Conversion Error for user {Username}", chatmessage.Username);
                return null;
            }

            UserObject user = await _database.GetUserAsync(ttvid).ConfigureAwait(false);

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
                await _database.AddOrUpdateUserAsync(user).ConfigureAwait(false);
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