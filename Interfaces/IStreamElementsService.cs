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
}