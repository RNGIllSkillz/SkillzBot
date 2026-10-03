using System.Text.Json.Serialization;

namespace SkillzBot.MODELS
{
    public class UserObject
    {
        public int dbID { get; set; }
        public long TwitchID { get; set; }
        public string Name { get; set; }
        public int isSub { get; set; }
        public int isVip { get; set; }
        public int isMod { get; set; }
        public int isPartner { get; set; }
        public int IsBroadcaster { get; set; }
        public int UvalCon { get; set; }
        public int messageCon { get; set; }
        public int roulettCon { get; set; }
        public double roulettCD { get; set; }
        public double UvalTimer { get; set; }
        public int banCount { get; set; }
        public double Points { get; set; }
        public int IsOnline { get; set; }
        public int QuizPoints { get; set; }
        public int QuizTotal { get; set; }

        /// <summary>
        /// True when this object was built from chat metadata because the database was
        /// unreachable. Such users are never written back to the database.
        /// </summary>
        [JsonIgnore]
        public bool IsTransient { get; set; }
    }
}
