using System;
using SkillzBot.JSON.MediaHistory;
using SkillzBot.JSON.MediaQueue;
using SkillzBot.JSON.StreamElements;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IStreamElementsService
{
    Task<bool> SendMediaAsync(string youTubeVideoId, CancellationToken token = default);
    Task<MediaHistoryJSON> GetHistory(CancellationToken token = default);
    Task<List<MediaQueueJson>> GetQueue(CancellationToken token = default);
    Task<StreamElementsJSON> GetCurrentSong(CancellationToken token = default);
    Task SendChatMessage(string message, CancellationToken token = default);
    /// <summary>Used for chat messages when StreamElements is unreachable or has no token (set by the IRC client).</summary>
    Func<string, CancellationToken, Task> FallbackSender { get; set; }
}