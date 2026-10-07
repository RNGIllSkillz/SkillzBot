using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Twitch;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Chat
{
    public enum ChatTransportMode { Auto, EventSub, Irc }

    /// <summary>What the IRC monitor should do with the IRC connection right now.</summary>
    public enum IrcPolicy
    {
        /// <summary>Connect if down, keep if up: IRC carries chat or is the only way to send.</summary>
        Required,
        /// <summary>Keep if up, do not start if down: a hand-over is in progress.</summary>
        Preferred,
        /// <summary>Park if up: EventSub carries chat and Helix can send.</summary>
        Unwanted
    }

    /// <summary>
    /// The single entry point for incoming chat, whichever transport delivered it. Deduplicates by message id
    /// (both transports carry Twitch's id, so a message seen twice during a hand-over is processed once), drops
    /// the bot's own messages and shared-chat messages from other channels, and holds the transport policy:
    /// EventSub chat is preferred; IRC runs while EventSub chat is not available, during a 60-second hand-over,
    /// and whenever Helix cannot send on the bot's behalf (then IRC stays as the outbound fallback).
    /// ChatTransport in the config forces one side: "eventsub" never starts IRC, "irc" never subscribes to chat.
    /// </summary>
    public sealed class ChatIngress
    {
        private const int RememberIds = 4096;
        private static readonly TimeSpan HandoverGrace = TimeSpan.FromSeconds(60);

        private readonly ILogger<ChatIngress> _logger;
        private readonly TwitchTokenService _tokens;
        private readonly ITwitchService _twitch;
        private readonly HealthState _health;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private readonly string _botLogin;
        private readonly string _broadcasterId;
        private long _lastMessageTicks = DateTime.UtcNow.Ticks;
        private long _fromEventSub, _fromIrc, _duplicates, _foreign;
        private string _eventSubReason = "not subscribed yet";

        public ChatTransportMode Mode { get; }
        public bool EventSubChatActive { get; private set; }
        public DateTime? EventSubChatSinceUtc { get; private set; }
        public DateTime? EventSubChatLostUtc { get; private set; }
        public DateTime LastMessageUtc => new DateTime(Interlocked.Read(ref _lastMessageTicks), DateTimeKind.Utc);
        public string LastSource { get; private set; } = "none";
        public string EventSubReason { get { lock (_seen) return _eventSubReason; } }

        public event Func<IncomingChatMessage, Task> MessageReceived;

        /// <summary>Set by the EventSub service: asks for a fresh websocket session (used when EventSub chat goes silent on a live stream).</summary>
        public Func<string, Task> EventSubRecovery { get; set; }

        public ChatIngress(BotConfigModel config, TwitchTokenService tokens, ITwitchService twitch, HealthState health, ILogger<ChatIngress> logger)
        {
            _logger = logger;
            _tokens = tokens;
            _twitch = twitch;
            _health = health;
            _botLogin = config.BotTwitchName?.ToLowerInvariant();
            _broadcasterId = config.BroadcasterId;
            if (Enum.TryParse<ChatTransportMode>(config.ChatTransport, true, out var m)) Mode = m;
            else
            {
                Mode = ChatTransportMode.Auto;
                if (!string.IsNullOrWhiteSpace(config.ChatTransport))
                    _logger.LogWarning("[Chat] ChatTransport '{Value}' is not auto|eventsub|irc; using auto.", config.ChatTransport);
            }
        }

        /// <summary>True when the bot account can send through Helix (bot token present with user:write:chat).</summary>
        public bool HelixSendAvailable
        {
            get
            {
                var bot = _tokens.Current(TwitchIdentity.Bot);
                return !string.IsNullOrEmpty(bot?.UserId) && _tokens.HasScope(TwitchIdentity.Bot, "user:write:chat");
            }
        }

        /// <summary>Helix can send right now: the bot token allows it and the send circuit is not tripped.</summary>
        public bool HelixSendUsable => HelixSendAvailable && _twitch.HelixSendHealthy;

        /// <summary>Whether the EventSub service should subscribe to chat at all.</summary>
        public bool EventSubWanted => Mode != ChatTransportMode.Irc;

        /// <summary>Routes one message to the handler unless it is a duplicate, the bot's own, or from another channel's shared chat.</summary>
        public async Task PublishAsync(IncomingChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id)) return;
            if (IsSelf(message)) return;
            if (!string.IsNullOrEmpty(message.SourceChannelId) && !string.IsNullOrEmpty(_broadcasterId) && message.SourceChannelId != _broadcasterId)
            {
                lock (_seen) _foreign++;
                return;
            }
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

        /// <summary>The bot's own messages: by configured login, by the bot token's login, or by the bot token's user id.</summary>
        private bool IsSelf(IncomingChatMessage m)
        {
            if (!string.IsNullOrEmpty(_botLogin) && string.Equals(m.Login, _botLogin, StringComparison.OrdinalIgnoreCase)) return true;
            var bot = _tokens.Current(TwitchIdentity.Bot);
            if (bot == null) return false;
            if (!string.IsNullOrEmpty(bot.UserId) && bot.UserId == m.UserId) return true;
            return !string.IsNullOrEmpty(bot.Login) && string.Equals(m.Login, bot.Login, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Called by the EventSub service when the chat subscription is confirmed or lost. Only an active-to-inactive
        /// transition starts the hand-over clock; a new reason while already inactive just updates the text.
        /// </summary>
        public void SetEventSubChat(bool active, string reason)
        {
            bool wasActive; string oldReason;
            lock (_seen)
            {
                wasActive = EventSubChatActive; oldReason = _eventSubReason;
                if (active == wasActive && (active || reason == oldReason)) return;
                EventSubChatActive = active;
                _eventSubReason = reason;
                if (active && !wasActive) { EventSubChatSinceUtc = DateTime.UtcNow; EventSubChatLostUtc = null; }
                else if (!active && wasActive) { EventSubChatLostUtc = DateTime.UtcNow; EventSubChatSinceUtc = null; }
            }
            if (active) _logger.LogInformation("[Chat] EventSub chat is active ({Reason}).", reason);
            else if (wasActive) _logger.LogWarning("[Chat] EventSub chat lost: {Reason}. {Fallback}", reason, Mode == ChatTransportMode.EventSub ? "ChatTransport=eventsub, so chat is DOWN until it returns." : "IRC carries chat meanwhile.");
            else if (Mode != ChatTransportMode.Irc) _logger.LogInformation("[Chat] EventSub chat not available: {Reason}.", reason);
        }

        /// <summary>The IRC policy for this moment; see <see cref="IrcPolicy"/>.</summary>
        public IrcPolicy Decide(DateTime nowUtc)
        {
            if (Mode == ChatTransportMode.Irc) return IrcPolicy.Required;
            if (Mode == ChatTransportMode.EventSub) return IrcPolicy.Unwanted;
            if (!HelixSendUsable) return IrcPolicy.Required; // nothing else can send when StreamElements is down
            lock (_seen)
            {
                if (EventSubChatActive)
                {
                    // Timeouts are bookkept from channel.ban; without that subscription IRC must stay to see them.
                    if (!_health.IsEventSubSubscriptionActive("channel.ban")) return IrcPolicy.Required;
                    return nowUtc - (EventSubChatSinceUtc ?? nowUtc) < HandoverGrace ? IrcPolicy.Preferred : IrcPolicy.Unwanted;
                }
                if (EventSubChatLostUtc == null) return IrcPolicy.Required;           // never had EventSub chat
                return nowUtc - EventSubChatLostUtc.Value < HandoverGrace ? IrcPolicy.Preferred : IrcPolicy.Required;
            }
        }

        /// <summary>Short status for the health line: which transport carries chat and why.</summary>
        public string Describe(bool ircConnected, bool ircInChannel)
        {
            bool esActive; string reason; long es, irc, dup, foreign;
            lock (_seen) { esActive = EventSubChatActive; reason = _eventSubReason; es = _fromEventSub; irc = _fromIrc; dup = _duplicates; foreign = _foreign; }
            string active = esActive ? "eventsub" : ircConnected && ircInChannel ? "irc" : "NONE";
            string detail = esActive ? "" : $" (eventsub: {reason})";
            string helix = !HelixSendAvailable ? "no" : _twitch.HelixSendHealthy ? "yes" : "tripped";
            return $"{active}{detail} mode={Mode.ToString().ToLowerInvariant()} helixSend={helix} es={es} irc={irc} dup={dup}{(foreign > 0 ? $" foreign={foreign}" : "")}";
        }
    }
}
