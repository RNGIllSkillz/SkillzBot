using Microsoft.Extensions.Logging;
using SkillzBot.IllConfiguration;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Writers;
using SkillzBot.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SkillzBot.IllSkillzBot
{
    public sealed class IllChatFilters
    {
        private readonly ILogger<IllChatFilters> _logger;
        private readonly IPathProvider _paths;
        private readonly ITwitchService _twitchService;
        private readonly BotConfigModel _config;
        private readonly FlagWriterService _flagWriter;
        private readonly IYouTubeService _youTubeService;
        private readonly LinkDetector _linkDetector;

        private AhoCorasick _pichkaMatcher = new AhoCorasick();
        private HashSet<string> _mediaBlacklist = new HashSet<string>();
        private HashSet<string> _channelBlacklist = new HashSet<string>();
        private ConcurrentDictionary<string, byte> _userBlacklist = new ConcurrentDictionary<string, byte>();

        // Dictionary-driven slur detector (dic.txt + dicWhiteList.txt).
        private volatile ProfanityDetector _profanity = ProfanityDetector.Build(Array.Empty<string>(), Array.Empty<string>());
        // Hard-coded phrases that get their own (shorter) timeout in the message handler.
        private static readonly ProfanityDetector BlockedPhrases =
            ProfanityDetector.Build(new[] { "хохол", "хахол" }, Array.Empty<string>());

        private const int CharsInRow = 29;
        private const int ArabCharsInRow = 4;
        private const int RowsNum = 3;
        private const char ArabicPresentationStart = 'ﭐ';
        private const char ArabicPresentationEnd = '︀';

        public IllChatFilters(ILogger<IllChatFilters> logger,
            ITwitchService twitchService,
            BotConfigModel config,
            FlagWriterService flagWriter,
            IYouTubeService youTubeService,
            IPathProvider paths,
            LinkDetector linkDetector)
        {
            _logger = logger;
            _twitchService = twitchService;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _flagWriter = flagWriter;
            _youTubeService = youTubeService;
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _linkDetector = linkDetector ?? throw new ArgumentNullException(nameof(linkDetector));
            ReloadFilters();
        }

        public void ReloadFilters()
        {
            _logger.LogInformation("Reloading chat filters from files...");
            try
            {
                _mediaBlacklist = new HashSet<string>(ReadLines(_paths.SharedPath, _config.FilePaths.MediaListFileName));
                _channelBlacklist = new HashSet<string>(ReadLines(_paths.SharedPath, _config.FilePaths.ChannelListFileName));
                _userBlacklist = new ConcurrentDictionary<string, byte>(
                    ReadLines(_paths.DataPath, _config.FilePaths.UserBlacklistFileName)
                        .Distinct()
                        .Select(x => new KeyValuePair<string, byte>(x, 0)));

                var banned = ReadLines(_paths.SharedPath, _config.FilePaths.DicFileName).ToList();
                var whitelist = ReadLines(_paths.SharedPath, _config.FilePaths.DicWhiteListFileName).ToList();
                _profanity = ProfanityDetector.Build(banned, whitelist);

                var pichka = new AhoCorasick();
                foreach (var line in ReadLines(_paths.SharedPath, _config.FilePaths.PichkaListFileName))
                    pichka.AddPattern(line);
                pichka.Build();
                _pichkaMatcher = pichka;

                _logger.LogInformation("Chat filters reloaded: {Banned} banned words, {White} whitelist words, {Users} blacklisted users.",
                    _profanity.BannedWordCount, _profanity.WhitelistCount, _userBlacklist.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload chat filters.");
            }
        }

        private static IEnumerable<string> ReadLines(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return Enumerable.Empty<string>();
            return File.ReadLines(path).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));
        }

        public bool CheckBooB(string message)
        {
            bool hasSuspiciousChars = false;
            foreach (char c in message)
            {
                if ((c >= '⠀' && c <= '⣿') || (c >= '▀' && c <= '▟'))
                {
                    hasSuspiciousChars = true;
                    break;
                }
            }
            if (!hasSuspiciousChars) return false;

            return _pichkaMatcher.ContainsAny(message);
        }

        public bool CheckTreck(string ID) => _mediaBlacklist.Contains(ID);

        public bool CheckChannel(string channelName) => _channelBlacklist.Contains(channelName);

        /// <summary>Dictionary slur check. Writes a flag record and returns true on a hit.</summary>
        public async Task<bool> ZapCheck(string message, string name)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var bannedWord = _profanity.Find(message);
            if (bannedWord == null) return false;

            await _flagWriter.WriteAsync($"{name} : {message} (detected: {bannedWord})");
            return true;
        }

        /// <summary>Hard-coded ethnic slur check that is independent of dic.txt.</summary>
        public bool ContainsBlockedPhrase(string message) => BlockedPhrases.Find(message) != null;

        public async Task<List<string>> YouTubeFilter(string ID)
        {
            List<string> output = new List<string>();
            var yRes = await _youTubeService.SearchByIdAsync(ID);
            if (yRes == null) return null;
            if (yRes[0] != "view" && yRes[0] != "duration" && yRes[0] != "age" && yRes[0] != "Embeddable")
            {
                if (await ZapCheck(yRes[0], "YouTube").ConfigureAwait(false))
                {
                    output.Add("ZAP");
                    return output;
                }
                else
                {
                    output.Add("ok");
                    output.Add(yRes[1]);
                    output.Add(yRes[0]);
                    return output;
                }
            }
            else
            {
                output.Add(yRes[0]);
                return output;
            }
        }

        public bool IsUserBlacklisted(string userID) => _userBlacklist.ContainsKey(userID);

        /// <summary>
        /// Deletes messages from non-moderators that contain links, except a single
        /// clip link from this channel.
        /// </summary>
        public async Task<bool> DeleteLinks(UserObject user, IncomingChatMessage e)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1) return false;

            int links = await _linkDetector.CountLinksAsync(e.Text).ConfigureAwait(false);
            if (links == 0) return false;

            if (links == 1)
            {
                var clipId = StringUtil.ExtractClipId(e.Text);
                if (clipId != null && await _twitchService.CheckClipExistence(clipId).ConfigureAwait(false))
                    return false;
            }

            await _twitchService.DeleteMessage(e.Id);
            return true;
        }

        public bool FilterASCII(IncomingChatMessage e)
        {
            if (e.CustomRewardId == _config.ChannelIds.Pi4KaId) return false;

            string message = e.Text;
            int count = StringUtil.CheckASCII(message);
            if (count / CharsInRow >= RowsNum && message.Length / CharsInRow > RowsNum)
                return true;

            int arabicCount = 0;
            foreach (char c in message)
                if (c >= ArabicPresentationStart && c < ArabicPresentationEnd) arabicCount++;
            if (arabicCount / ArabCharsInRow >= RowsNum && message.Length / ArabCharsInRow >= RowsNum)
                return true;

            return message.Contains("ﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞ");
        }

        public void EditUserBlackList(string UserTtvID) => _userBlacklist.TryRemove(UserTtvID, out _);

        public void AddToWhiteList(string WordToAdd) => _profanity.AddWhitelist(WordToAdd);
    }
}
