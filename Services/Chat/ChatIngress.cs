using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Chat
{
    public enum ChatTransportMode { Auto, EventSub, Irc }

    /// <summary>
    /// The single entry point for incoming chat, whichever transport delivered it. Deduplicates by message id
    /// (both transports carry Twitch's id, so a message seen twice during a hand-over is processed once), drops
    /// the bot's own messages, and holds the transport policy: EventSub chat is preferred, IRC runs only while
    /// EventSub chat is not available (or always/never, per ChatTransport in the config).
    /// </summary>
    public sealed class ChatIngress
    {
        private const int RememberIds = 4096;
        private static readonly TimeSpan HandoverGrace = TimeSpan.FromSeconds(60);

        private readonly ILogger<ChatIngress> _logger;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private readonly string _botLogin;
        private long _lastMessageTicks = DateTime.UtcNow.Ticks;
        private long _fromEventSub, _fromIrc, _duplicates;
        private string _eventSubReason = "not subscribed yet";

        public ChatTransportMode Mode { get; }
        public bool EventSubChatActive { get; private set; }
        public DateTime? EventSubChatSinceUtc { get; private set; }
        public DateTime? EventSubChatLostUtc { get; private set; }
        public DateTime LastMessageUtc => new DateTime(Interlocked.Read(ref _lastMessageTicks), DateTimeKind.Utc);
        public string LastSource { get; private set; } = "none";

        public event Func<IncomingChatMessage, Task> MessageReceived;

        public ChatIngress(BotConfigModel config, ILogger<ChatIngress> logger)
        {
            _logger = logger;
            _botLogin = config.BotTwitchName?.ToLowerInvariant();
            Mode = Enum.TryParse<ChatTransportMode>(config.ChatTransport, true, out var m) ? m : ChatTransportMode.Auto;
        }

        /// <summary>Routes one message to the handler unless it is a duplicate or the bot's own.</summary>
        public async Task PublishAsync(IncomingChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id)) return;
            if (!string.IsNullOrEmpty(_botLogin) && string.Equals(message.Login, _botLogin, StringComparison.OrdinalIgnoreCase)) return;
            lock (_seen)
            {
                if (!_seen.Add(message.Id)) { _duplicates++; return; }
                _seenOrder.Enqueue(message.Id);
                while (_seenOrder.Count > RememberIds) _seen.Remove(_seenOrder.Dequeue());
                if (message.Source == IncomingChatMessage.SourceEventSub) _fromEventSub++; else _fromIrc++;
                LastSource = message.Source;
            }
            Interlocked.Exchange(ref _lastMessageTicks, DateTime.UtcNow.Ticks);
            var handler = MessageReceived;
            if (handler != null) await handler(message);
        }

        /// <summary>Called by the EventSub service when the chat subscription is confirmed or lost.</summary>
        public void SetEventSubChat(bool active, string reason)
        {
            lock (_seen)
            {
                if (active == EventSubChatActive && (active || reason == _eventSubReason)) return;
                EventSubChatActive = active;
                _eventSubReason = reason;
                if (active) { EventSubChatSinceUtc = DateTime.UtcNow; EventSubChatLostUtc = null; }
                else { EventSubChatLostUtc = DateTime.UtcNow; EventSubChatSinceUtc = null; }
            }
            if (active) _logger.LogInformation("[Chat] EventSub chat is active ({Reason}).", reason);
            else _logger.LogWarning("[Chat] EventSub chat is not available: {Reason}. {Fallback}", reason, Mode == ChatTransportMode.EventSub ? "ChatTransport=eventsub, so chat is DOWN." : "IRC takes over.");
        }

        /// <summary>
        /// Whether IRC should be connected right now. In auto mode IRC parks 60 seconds after EventSub chat comes up
        /// and resumes 60 seconds after it goes away, so a brief EventSub hiccup does not flap the IRC connection.
        /// </summary>
        public bool IrcWanted(DateTime nowUtc)
        {
            switch (Mode)
            {
                case ChatTransportMode.Irc: return true;
                case ChatTransportMode.EventSub: return false;
                default:
                    lock (_seen)
                    {
                        if (EventSubChatActive) return nowUtc - (EventSubChatSinceUtc ?? nowUtc) < HandoverGrace;
                        return nowUtc - (EventSubChatLostUtc ?? DateTime.MinValue) >= HandoverGrace || EventSubChatLostUtc == null;
                    }
            }
        }

        /// <summary>Whether the EventSub service should subscribe to chat at all.</summary>
        public bool EventSubWanted => Mode != ChatTransportMode.Irc;

        /// <summary>Short status for the health line: which transport carries chat and why.</summary>
        public string Describe(bool ircConnected)
        {
            string active = EventSubChatActive ? "eventsub" : ircConnected ? "irc" : "NONE";
            string detail = EventSubChatActive ? "" : $" (eventsub: {_eventSubReason})";
            long es, irc, dup; lock (_seen) { es = _fromEventSub; irc = _fromIrc; dup = _duplicates; }
            return $"{active}{detail} mode={Mode.ToString().ToLowerInvariant()} es={es} irc={irc} dup={dup}";
        }
    }
}
