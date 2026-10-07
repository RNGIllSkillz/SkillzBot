using SkillzBot.MODELS;
using System;
using System.Linq;
using TwitchLib.Client.Models;
using TwitchLib.EventSub.Core.SubscriptionTypes.Channel;

namespace SkillzBot.TtvClient
{
    /// <summary>Builds the transport-neutral message from either library's payload.</summary>
    public static class ChatMessageMapper
    {
        public static IncomingChatMessage FromIrc(ChatMessage m) => new IncomingChatMessage(
            m.Id, m.UserId, m.Username?.ToLowerInvariant(), string.IsNullOrEmpty(m.DisplayName) ? m.Username : m.DisplayName, m.Message ?? "",
            m.HexColor, m.UserDetail.IsModerator, m.UserDetail.IsVip, m.UserDetail.IsSubscriber, m.IsBroadcaster, m.UserDetail.IsPartner,
            m.Bits, m.CustomRewardId, null, IncomingChatMessage.SourceIrc);

        public static IncomingChatMessage FromEventSub(ChannelChatMessage e)
        {
            var badges = e.Badges ?? Array.Empty<TwitchLib.EventSub.Core.Models.Chat.ChatBadge>();
            bool Has(string set) => badges.Any(b => string.Equals(b.SetId, set, StringComparison.OrdinalIgnoreCase));
            return new IncomingChatMessage(
                e.MessageId, e.ChatterUserId, e.ChatterUserLogin?.ToLowerInvariant(), string.IsNullOrEmpty(e.ChatterUserName) ? e.ChatterUserLogin : e.ChatterUserName,
                e.Message?.Text ?? "", e.Color,
                e.IsModerator || Has("moderator"), e.IsVip || Has("vip"), e.IsSubscriber || Has("subscriber") || Has("founder"),
                e.IsBroadcaster || Has("broadcaster"), Has("partner"),
                e.Cheer?.Bits ?? 0, e.ChannelPointsCustomRewardId, e.Reply?.ParentMessageId, IncomingChatMessage.SourceEventSub,
                string.IsNullOrEmpty(e.SourceBroadcasterUserId) ? null : e.SourceBroadcasterUserId);
        }
    }
}
