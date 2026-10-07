namespace SkillzBot.MODELS
{
    public enum ChatSendOutcome { Sent, Failed, Unknown }

    /// <summary>Result of a Helix chat send: Failed means certainly not delivered (safe to try another path), Unknown means maybe delivered.</summary>
    public sealed record ChatSendResult(ChatSendOutcome Outcome, string Reason)
    {
        public bool Sent => Outcome == ChatSendOutcome.Sent;
        public static ChatSendResult Ok() => new ChatSendResult(ChatSendOutcome.Sent, null);
        public static ChatSendResult Fail(string reason) => new ChatSendResult(ChatSendOutcome.Failed, reason);
        public static ChatSendResult Maybe(string reason) => new ChatSendResult(ChatSendOutcome.Unknown, reason);
    }
}
