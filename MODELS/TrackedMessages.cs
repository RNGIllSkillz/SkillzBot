using System;
using System.Collections.Generic;
using System.Text;

namespace SkillzBot.MODELS
{
    internal class TrackedMessages
    {
        public long TimeStamp { get; set; }
        public string ChannelName { get; set; }
        public string Message { get; set; }
    }
}
