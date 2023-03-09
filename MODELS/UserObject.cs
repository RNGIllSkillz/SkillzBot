namespace SkillzBot.MODELS
{
    public class UserObject
    {
        public int dbID { get; set; }
        public int TwitchID { get; set; }
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
    }
}
