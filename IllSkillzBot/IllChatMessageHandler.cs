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
using SkillzBot.Utils; // Ensure StringUtil is accessible
using F23.StringSimilarity;
using SkillzBot.IRC;

namespace SkillzBot.IllSkillzBot
{
    internal class IllChatMessageHandler
    {
        // REPLACEMENT: ConcurrentDictionary is thread-safe and instant O(1) lookup
        private static readonly ConcurrentDictionary<string, UserChatTracker> _userTrackers =
            new ConcurrentDictionary<string, UserChatTracker>(StringComparer.OrdinalIgnoreCase);

        // REPLACEMENT: ConcurrentQueue is better for producer-consumer buffers than List+Lock
        private static readonly ConcurrentQueue<MessageBuffer> _messagesBuffer = new ConcurrentQueue<MessageBuffer>();

        // STATIC TOOLS
        private static readonly NormalizedLevenshtein _levenshtein = new NormalizedLevenshtein();
        private static readonly ILogger<IllChatMessageHandler> _logger = IllServiceProvider.GetLogger<IllChatMessageHandler>();

        // CONFIG
        private const int HardTimeoutSec = 600;
        private const int TimeoutSec = 300;
        private const int LightTimeoutSec = 10;
        private const int SaveBufferCount = 100;

        public static async Task<UserObject> MessageHandler(OnMessageReceivedArgs e)
        {
            // 1. Fast fail / Ignore specific bots
            if (e.ChatMessage.Username.Equals("streamelements", StringComparison.OrdinalIgnoreCase)) return null;

            // 2. Buffer to DB (Non-blocking)
            SaveToBuffer(e);

            // 3. Update internal tracker (Memory)
            var tracker = AddToTracker(e.ChatMessage.Username, e.ChatMessage.Message);

            // 4. Get/Update User (Database)
            var user = await GetAddUser(e.ChatMessage).ConfigureAwait(false);
            if (user == null) return null; // Handle error case

            user.messageCon++;

            // 5. Async Buffer Save
            // Fire and forget usually, or await if critical. 
            // Ideally, this should be a background service.
            SaveBuffer(false);

            // 6. Active Protection Checks
            if (IllSingleton.State.isSubActive)
            {
                // A. Bad Pictures (ASCII Art checks)
                if (IllChatFilters.CheckBooB(e.ChatMessage.Message))
                {
                    await TtvAPI.TimeOutUser(user, HardTimeoutSec, STRINGS.TimeOutBadPic).ConfigureAwait(false);
                    return user;
                }

                if (IllChatFilters.FilterASCII(e))
                {
                    await TtvAPI.TimeOutUser(user, TimeoutSec, STRINGS.TimeOutPic).ConfigureAwait(false);
                    // Don't return here, flag it but continue checking other things? 
                    // Original code set fl2=true but continued.
                }

                // B. Profanity Filter (The Optimized ZapCheck)
                if (await IllChatFilters.ZapCheck(e.ChatMessage.Message, e.ChatMessage.DisplayName).ConfigureAwait(false))
                {
                    return await IllCommands.IllFilterTrigger(user, e.ChatMessage.Id).ConfigureAwait(false);
                }

                // C. Link Deletion
                await IllChatFilters.DeleteLinks(user, e).ConfigureAwait(false);

                // D. Spam Check
                 if (CheckSpam(tracker, e.ChatMessage.Message)) 
                 {
                    await TtvAPI.TimeOutUser(user, LightTimeoutSec, STRINGS.TimeOutSpam).ConfigureAwait(false);
                    return user; 
                 }

                // E. Hardcoded Slur Check (Consider moving to ZapCheck/Filter file)
                // Using StringUtil.Normalize to catch variations like "xaxol" if x maps to h
                string normalizedMsg = StringUtil.Normalize(e.ChatMessage.Message);
                if (normalizedMsg.Contains("хохол") || normalizedMsg.Contains("хахол"))
                {
                    await TtvAPI.TimeOutUser(user, TimeoutSec, STRINGS.TimeOut1wReason).ConfigureAwait(false);
                    return user;
                }

                // F. Game Logic
                if (IllSingleton.State.QuizIsRunning)
                    user = await IllGames.UserGuessAnswer(user, e.ChatMessage.Message).ConfigureAwait(false);
                else
                    IllGames.QuizzActiveUser(user.TwitchID.ToString());
            }

            // 7. Command Handling
            if (e.ChatMessage.Message.StartsWith("!"))
            {
                user = await IllCommandHandler.CommandHandler(user, e.ChatMessage.Message).ConfigureAwait(false);
            }

            return user;
        }

        // ==========================================
        //  BUFFER LOGIC
        // ==========================================

        private static void SaveToBuffer(OnMessageReceivedArgs e)
        {
            _messagesBuffer.Enqueue(new MessageBuffer()
            {
                Message = e.ChatMessage.Message,
                TtvID = e.ChatMessage.UserId,
                Name = e.ChatMessage.Username,
                TimeStamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString()
            });
        }

        public static async Task SaveBuffer(bool IsForced)
        {
            if (_messagesBuffer.IsEmpty) return;
            if (_messagesBuffer.Count < SaveBufferCount && !IsForced) return;

            // Dequeue all current items
            List<MessageBuffer> temp = new List<MessageBuffer>();
            while (_messagesBuffer.TryDequeue(out var msg))
            {
                temp.Add(msg);
            }

            if (temp.Count > 0)
            {
                await IllServiceProvider.Database.SaveMessagesAsync(temp).ConfigureAwait(false);
            }
        }

        // ==========================================
        //  TRACKER LOGIC
        // ==========================================

        private static UserChatTracker AddToTracker(string username, string message)
        {
            var tracker = _userTrackers.GetOrAdd(username, key => new UserChatTracker { Username = key });

            // Lock only this specific user instance to ensure message order
            lock (tracker)
            {
                tracker.AddMessage(message);
            }
            return tracker;
        }

        // ==========================================
        //  USER DB LOGIC
        // ==========================================

        private static async Task<UserObject> GetAddUser(ChatMessage chatmessage)
        {
            if (!int.TryParse(chatmessage.UserId, out int ttvid))
            {
                _logger.LogError("GetAddUser(): TtvID Conversion Error");
                return null;
            }

            // Optimization: If IllServiceProvider has a memory cache, this is fine.
            // If it hits SQL every time, consider adding a MemoryCache layer here.
            UserObject user = await IllServiceProvider.Database.GetUserAsync(ttvid).ConfigureAwait(false);

            // Update volatile fields
            bool needsUpdate = false;

            if (user.dbID == -404)
            {
                // New User
                user.TwitchID = ttvid;
                needsUpdate = true;
            }

            // Check for changes (Basic optimization to avoid DB writes if nothing changed)
            if (user.Name != chatmessage.Username ||
                user.isSub != (chatmessage.IsSubscriber ? 1 : 0) ||
                user.isMod != (chatmessage.IsModerator ? 1 : 0) ||
                user.isVip != (chatmessage.IsVip ? 1 : 0))
            {
                needsUpdate = true;
            }

            user.Name = chatmessage.Username;
            user.isSub = chatmessage.IsSubscriber ? 1 : 0;
            user.isVip = chatmessage.IsVip ? 1 : 0;
            user.IsBroadcaster = chatmessage.IsBroadcaster ? 1 : 0;
            user.isMod = chatmessage.IsModerator ? 1 : 0;
            user.isPartner = chatmessage.IsPartner ? 1 : 0;

            if (needsUpdate)
            {
                await IllServiceProvider.Database.AddOrUpdateUserAsync(user).ConfigureAwait(false);
            }

            return user;
        }

        // ==========================================
        //  SPAM CHECK
        // ==========================================

        static bool CheckSpam(UserChatTracker tracker, string currentMessage)
        {
            lock (tracker)
            {
                // Logic: 
                // 1. Check similarity between Msg 0 and 1
                // 2. Check similarity between Msg 1 and 2
                // 3. Check similarity between Msg 2 and 3
                // 4. Check time frequency

                // Note: Messages[0] is current, [1] is previous, etc.

                double sim1 = _levenshtein.Distance(tracker.RecentMessages[0], tracker.RecentMessages[1]);
                if (sim1 >= 0.4) return false; // Not similar enough

                double sim2 = _levenshtein.Distance(tracker.RecentMessages[1], tracker.RecentMessages[2]);
                if (sim2 >= 0.4) return false;

                // Thresholds based on message length
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
                    // Longer messages: don't need to check 4th message deep, just 3 messages fast
                    if (tracker.SecondsSinceLastMessage < 10)
                    {
                        isSpam = true;
                    }
                }

                if (isSpam)
                {
                    // Reset buffer so they don't get banned instantly again for the next message
                    tracker.RecentMessages[0] = "0";
                    tracker.RecentMessages[1] = "1";
                    tracker.RecentMessages[2] = "2";
                    tracker.RecentMessages[3] = "3";
                    return true;
                }
            }
            return false;
        }
    }
}