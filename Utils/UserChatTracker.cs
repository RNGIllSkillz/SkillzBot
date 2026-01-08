using System;

namespace SkillzBot.Utils
{
    public class UserChatTracker
    {
        public string Username;
        // Store last 4 messages. Using a fixed array is faster than a List.
        public string[] RecentMessages = new string[4] { "", "", "", "" };
        public long LastMessageTimestamp;
        public double SecondsSinceLastMessage;

        public void AddMessage(string message)
        {
            // Shift messages (Ring buffer style would be faster, but array shift is fine for size 4)
            RecentMessages[3] = RecentMessages[2];
            RecentMessages[2] = RecentMessages[1];
            RecentMessages[1] = RecentMessages[0];
            RecentMessages[0] = message;

            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            SecondsSinceLastMessage = now - LastMessageTimestamp;
            LastMessageTimestamp = now;
        }
    }
}