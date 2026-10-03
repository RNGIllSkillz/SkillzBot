using System;
using System.Collections.Generic;

namespace SkillzBot.MODELS
{
    /// <summary>One VIP row from Helix.</summary>
    public record VipInfo(string UserId, string UserLogin, string UserName);

    /// <summary>A VIP as the bot knows them. Twitch does not expose when a VIP was granted, so the bot records it.</summary>
    public class VipRecord
    {
        public long TwitchId { get; set; }
        public string Login { get; set; }
        public string DisplayName { get; set; }
        /// <summary>When the VIP was granted; null for VIPs that predate tracking.</summary>
        public DateTime? Since { get; set; }
        /// <summary>bot, twitch (EventSub), chat (badge seen), sync, initial, manual.</summary>
        public string Source { get; set; }
        /// <summary>Row id in dbUserTable: a seniority proxy for ordering VIPs with an unknown date.</summary>
        public int? DbId { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        /// <summary>Pinned VIPs are never removed by the automatic rotation.</summary>
        public bool Pinned { get; set; }
    }
}
