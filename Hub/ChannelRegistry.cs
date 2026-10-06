using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkillzBot.Hub
{
    public sealed class ChannelEntry
    {
        public string Login { get; set; }
        public string DisplayName { get; set; }
        public string BroadcasterId { get; set; }
        public int ApiPort { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime CreatedUtc { get; set; }
        public string AddedBy { get; set; }
    }

    /// <summary>Channels_Data/channels.json: which channels exist, who owns them, which port each process listens on.</summary>
    public sealed class ChannelRegistry
    {
        public const string FileName = "channels.json";
        private readonly string _path;
        private readonly int _portBase;
        private readonly List<ChannelEntry> _channels = new List<ChannelEntry>();
        private readonly object _lock = new object();

        public ChannelRegistry(string path, int portBase)
        {
            _path = path;
            _portBase = portBase;
            Load();
        }

        public bool Exists => File.Exists(_path);

        public IReadOnlyList<ChannelEntry> All() { lock (_lock) return _channels.Select(Clone).ToList(); }
        public ChannelEntry Get(string login) { lock (_lock) return _channels.Where(c => c.Login.Equals(login, StringComparison.OrdinalIgnoreCase)).Select(Clone).FirstOrDefault(); }
        public ChannelEntry GetByBroadcaster(string userId) { lock (_lock) return _channels.Where(c => c.BroadcasterId == userId).Select(Clone).FirstOrDefault(); }

        public ChannelEntry Add(string login, string displayName, string broadcasterId, string addedBy)
        {
            lock (_lock)
            {
                var existing = _channels.FirstOrDefault(c => c.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return Clone(existing);
                var entry = new ChannelEntry { Login = login.ToLowerInvariant(), DisplayName = displayName ?? login, BroadcasterId = broadcasterId, ApiPort = NextPort(), CreatedUtc = DateTime.UtcNow, AddedBy = addedBy };
                _channels.Add(entry);
                Save();
                return Clone(entry);
            }
        }

        public bool SetEnabled(string login, bool enabled)
        {
            lock (_lock)
            {
                var e = _channels.FirstOrDefault(c => c.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                if (e == null) return false;
                e.Enabled = enabled; Save(); return true;
            }
        }

        public bool Remove(string login)
        {
            lock (_lock)
            {
                int n = _channels.RemoveAll(c => c.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                if (n > 0) Save();
                return n > 0;
            }
        }

        private int NextPort()
        {
            int port = _portBase;
            while (_channels.Any(c => c.ApiPort == port)) port++;
            return port;
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            var doc = JsonConvert.DeserializeObject<RegistryFile>(File.ReadAllText(_path));
            if (doc?.Channels != null) _channels.AddRange(doc.Channels.Where(c => !string.IsNullOrWhiteSpace(c.Login)));
        }

        private void Save()
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(new RegistryFile { Channels = _channels }, Formatting.Indented));
            File.Move(tmp, _path, true);
        }

        private static ChannelEntry Clone(ChannelEntry e) => new ChannelEntry { Login = e.Login, DisplayName = e.DisplayName, BroadcasterId = e.BroadcasterId, ApiPort = e.ApiPort, Enabled = e.Enabled, CreatedUtc = e.CreatedUtc, AddedBy = e.AddedBy };

        private sealed class RegistryFile { public List<ChannelEntry> Channels { get; set; } }
    }
}
