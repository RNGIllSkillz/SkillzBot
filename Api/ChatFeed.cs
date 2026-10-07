using SkillzBot.MODELS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace SkillzBot.Api
{
    /// <summary>
    /// Live view of the chat for the web panel: a ring buffer of recent messages (both
    /// directions) and a broadcast channel for Server-Sent Events subscribers.
    /// </summary>
    public sealed class ChatFeed
    {
        private const int Capacity = 500;
        private readonly ConcurrentQueue<ChatMessageDto> _recent = new ConcurrentQueue<ChatMessageDto>();
        private readonly ConcurrentDictionary<Guid, Channel<FeedEvent>> _subscribers = new ConcurrentDictionary<Guid, Channel<FeedEvent>>();
        private readonly string _botLogin;

        public ChatFeed(IllConfiguration.BotConfigModel config)
        {
            _botLogin = config.BotTwitchName?.ToLowerInvariant() ?? "bot";
        }

        public int Subscribers => _subscribers.Count;

        public void PublishIncoming(IncomingChatMessage m)
        {
            Publish(new ChatMessageDto(m.Id, DateTime.UtcNow, m.Login, m.DisplayName, m.Text,
                m.IsModerator, m.IsVip, m.IsSubscriber, m.IsBroadcaster, false, m.Color));
        }

        public void PublishOutgoing(string text)
        {
            Publish(new ChatMessageDto(Guid.NewGuid().ToString("N"), DateTime.UtcNow, _botLogin, _botLogin, text,
                false, false, false, false, true, null));
        }

        private void Publish(ChatMessageDto dto)
        {
            _recent.Enqueue(dto);
            while (_recent.Count > Capacity && _recent.TryDequeue(out _)) { }
            Broadcast(new FeedEvent("chat", dto));
        }

        public void Broadcast(FeedEvent ev)
        {
            foreach (var sub in _subscribers.Values) sub.Writer.TryWrite(ev);
        }

        public IReadOnlyList<ChatMessageDto> Recent(int limit)
        {
            var all = _recent.ToArray();
            return all.Skip(Math.Max(0, all.Length - limit)).ToList();
        }

        public ChannelReader<FeedEvent> Subscribe(out Guid id)
        {
            id = Guid.NewGuid();
            var channel = Channel.CreateBounded<FeedEvent>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            _subscribers[id] = channel;
            return channel.Reader;
        }

        public void Unsubscribe(Guid id)
        {
            if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
        }
    }
}
