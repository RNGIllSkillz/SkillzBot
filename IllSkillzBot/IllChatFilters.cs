using Microsoft.Extensions.Logging;
using SkillzBot.API.Twitch;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Writers;
using SkillzBot.IllConfiguration; 
using SkillzBot.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Client.Events;
using urldetector.detection;

namespace SkillzBot.IllSkillzBot
{
    public sealed class IllChatFilters
    {
        private readonly ILogger<IllChatFilters> _logger;
        private readonly IPathProvider _paths;
        private readonly ITwitchService _twitchService;
        private readonly BotConfigModel _config;
        private readonly IBotStateService _botState;
        private readonly FlagWriterService _flagWriter;
        private readonly IYouTubeService _youTubeService;

        private AhoCorasick _pichkaMatcher;
        private HashSet<string> _mediaBlacklist;
        private HashSet<string> _channelBlacklist;
        private HashSet<string> _dictionary;
        private readonly BannedWordsTrie _bannedWordsTrie = new();
        private HashSet<string> _whitelist;
        private ConcurrentDictionary<string, byte> _userBlacklist;

        private static readonly int[] Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
        private const int CharsInRow = 29;
        private const int ArabCharsInRow = 4;
        private const int RowsNum = 3;

        public IllChatFilters(ILogger<IllChatFilters> logger, 
            ITwitchService twitchService, 
            BotConfigModel config, 
            IBotStateService botState,
            FlagWriterService flagWriter,
            IYouTubeService youTubeService,
            IPathProvider paths)
        {
            _logger = logger; 
            _twitchService = twitchService;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _botState = botState;
            _flagWriter = flagWriter;
            _youTubeService = youTubeService;
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            ReloadFilters();
        }

        public void ReloadFilters()
        {
            _logger.LogInformation("Reloading chat filters from files...");
            try
            {
                _mediaBlacklist = new HashSet<string>(File.ReadLines(Path.Combine(_paths.SharedPath, _config.FilePaths.MediaListFileName)));
                _channelBlacklist = new HashSet<string>(File.ReadLines(Path.Combine(_paths.SharedPath, _config.FilePaths.ChannelListFileName)));
                _dictionary = new HashSet<string>(File.ReadLines(Path.Combine(_paths.SharedPath, _config.FilePaths.DicFileName)));
                _whitelist = new HashSet<string>(File.ReadLines(Path.Combine(_paths.SharedPath, _config.FilePaths.DicWhiteListFileName)));
                _userBlacklist = new ConcurrentDictionary<string, byte>(
                    File.ReadLines(Path.Combine(_paths.DataPath, _config.FilePaths.UserBlacklistFileName))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .Select(x => new KeyValuePair<string, byte>(x, 0))
                );

                _bannedWordsTrie.BuildTrie(_dictionary);

                var pichkaLines = File.ReadLines(Path.Combine(_paths.SharedPath, _config.FilePaths.PichkaListFileName));
                _pichkaMatcher = new AhoCorasick();
                foreach (var line in pichkaLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _pichkaMatcher.AddPattern(line);
                    }
                }
                _pichkaMatcher.Build();
                _logger.LogInformation("Chat filters reloaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload chat filters.");
            }
        }

        public bool CheckBooB(string message)
        {
            bool hasSuspiciousChars = false;
            foreach (char c in message)
            {
                if ((c >= '\u2800' && c <= '\u28FF') || (c >= '\u2580' && c <= '\u259F'))
                {
                    hasSuspiciousChars = true;
                    break;
                }
            }
            if (!hasSuspiciousChars) return false;

            return _pichkaMatcher.ContainsAny(message);
        }
        public bool CheckTreck(string ID)
        {
            return _mediaBlacklist.Contains(ID);
        }
        public bool CheckChannel(string channelName)
        {
            return _channelBlacklist.Contains(channelName);
        }
        public async Task<bool> ZapCheck(string message, string name)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            string processingMsg = StringUtil.Normalize(message);

            if (_whitelist != null)
            {
                foreach (var white in _whitelist)
                {
                    processingMsg = processingMsg.Replace(white, " ");
                }
            }

            string squashedMsg = StringUtil.GetAggressiveString(processingMsg);
            var bannedWord = _bannedWordsTrie.FindBannedWord(squashedMsg);

            if (bannedWord != null)
            {
                await _flagWriter.WriteAsync($"{name} : {message} (detected: {bannedWord})");
                return true;
            }
            return false;
        }
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
        public bool IsUserBlacklisted(string userID)
        {
            return _userBlacklist.ContainsKey(userID);
        }
        public async Task<bool> DeleteLinks(UserObject user, OnMessageReceivedArgs e)
        {
            if (user.isMod == 1 || user.IsBroadcaster == 1) return false;
            if (!NetUtil.IsValidLink(e.ChatMessage.Message)) return false;
            UrlDetector parser = new UrlDetector(e.ChatMessage.Message, UrlDetectorOptions.Default);
            var detectedUrls = parser.Detect();
            if (detectedUrls.Count == 1)
            {
                var clipId = StringUtil.ExtractClipId(e.ChatMessage.Message);
                if (clipId == null || !await _twitchService.CheckClipExistence(clipId).ConfigureAwait(false))
                {
                    await _twitchService.DeleteMessage(e.ChatMessage.Id);
                    return true;
                }
            }
            if (detectedUrls.Count > 1)
            {
                await _twitchService.DeleteMessage(e.ChatMessage.Id);
                return true;
            }
            return false;
        }
        public bool FilterASCII(OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.CustomRewardId != _config.ChannelIds.Pi4KaId)
            {
                int count = StringUtil.CheckASCII(e.ChatMessage.Message);
                if (count / CharsInRow >= RowsNum && e.ChatMessage.Message.Length / CharsInRow > RowsNum)
                    return true;
                var arabicCount = Arabic2.Select(b => e.ChatMessage.Message.Count(f => f == (char)b)).Sum();
                if (arabicCount / ArabCharsInRow >= RowsNum && e.ChatMessage.Message.Length / ArabCharsInRow >= RowsNum)
                    return true;
                if (e.ChatMessage.Message.Contains("ﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞﱞ"))
                    return true;
            }
            return false;
        }
        public void EditUserBlackList(string UserTtvID)
        {
            _userBlacklist.TryRemove(UserTtvID, out _);
        }
        public void AddToWhiteList(string WordToAdd)
        {
            _whitelist.Add(WordToAdd);
        }
    }
}