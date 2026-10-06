using System;
using System.Collections.Generic;

namespace SkillzBot.MODELS
{
    /// <summary>
    /// One chat message as the bot sees it, independent of the transport that delivered it (EventSub or IRC).
    /// Ids are Twitch's message ids, identical on both transports, which is what makes dedup possible.
    /// </summary>
    public sealed record IncomingChatMessage(
        string Id,
        string UserId,
        string Login,
        string DisplayName,
        string Text,
        string Color,
        bool IsModerator,
        bool IsVip,
        bool IsSubscriber,
        bool IsBroadcaster,
        bool IsPartner,
        int Bits,
        string CustomRewardId,
        string ReplyParentMessageId,
        string Source)
    {
        public const string SourceEventSub = "eventsub";
        public const string SourceIrc = "irc";

        public DateTime ReceivedUtc { get; init; } = DateTime.UtcNow;
    }
}
