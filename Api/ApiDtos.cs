using System;
using System.Collections.Generic;

namespace SkillzBot.Api
{
    public record ChatMessageDto(string Id, DateTime Time, string Login, string DisplayName, string Text, bool IsMod, bool IsVip, bool IsSub, bool IsBroadcaster, bool FromBot, string Color);

    public record FeedEvent(string Type, object Data);

    public class StatusDto
    {
        public DateTime TimeUtc { get; set; }
        public string Version { get; set; }
        public double UptimeSeconds { get; set; }
        public double RamMb { get; set; }
        public int Threads { get; set; }
        public bool IrcConnected { get; set; }
        public double IrcLastTrafficSeconds { get; set; }
        public double IrcLastMessageSeconds { get; set; }
        public bool IrcInChannel { get; set; }
        public bool EventSubConnected { get; set; }
        public double? EventSubSinceSeconds { get; set; }
        public double? EventSubLastEventSeconds { get; set; }
        public long EventSubReconnects { get; set; }
        public int ChatPending { get; set; }
        public long ChatProcessed { get; set; }
        public int ChatBuffered { get; set; }
        public long ChatStalled { get; set; }
        public string ChatLastStall { get; set; }
        public bool DbOk { get; set; }
        public long DbFailures { get; set; }
        public long StreamElementsFailures { get; set; }
        public double? StreamElementsLastOkSeconds { get; set; }
        public string Proxy { get; set; }
        public bool Silent { get; set; }
        public bool SubActive { get; set; }
        public int FilterLevel { get; set; }
        public bool AutoPred { get; set; }
        public bool InMatch { get; set; }
        public bool Online { get; set; }
    }

    public record UserInfoDto(long TwitchId, string Login, string Role);

    public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

    public record MessageRow(long Id, long TwitchId, string Name, string Message, DateTime Time);

    public record ActivityBucket(DateTime Time, int Messages, int Users);

    public record FilterListDto(string Name, string Title, bool Shared, IReadOnlyList<string> Lines);

    public record ConfigViewDto(Dictionary<string, object> Values, IReadOnlyList<string> SecretKeys, IReadOnlyList<string> SecretKeysSet, IReadOnlyList<string> EditorKeys);

    public record EditorRow(long TwitchId, string Login, string AddedBy, DateTime AddedAt);

    public record QuizRow(int Id, string Question, string Answer, int Prize);

    public record PredictionOutcomeRow(string Title, string Color, int Users, long ChannelPoints, bool IsWinner);
    public record PredictionRow(string PredictionId, string Kind, string Source, string Title, string MatchId, DateTime? StartedAt, DateTime? EndedAt, string Status, int TotalUsers, long TotalPoints, List<PredictionOutcomeRow> Outcomes);
    public record PollChoiceRow(string Title, string KindKey, int Votes, int ChannelPointsVotes);
    public record PollRow(string PollId, string Source, string Title, DateTime? StartedAt, DateTime? EndedAt, string Status, int TotalVotes, string WinnerTitle, List<PollChoiceRow> Choices);

    public record SystemInfoDto(string Version, string Runtime, DateTime StartedUtc, string DataPath, string Channel, int ApiPort, string PublicUrl);
}
