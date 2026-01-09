using SkillzBot.Utils;
using SkillzBot.WRITERS;
using System;
using System.Collections.Generic;
using System.IO;
using IllSkillzBot;
using SkillzBot.API.YouTube;
using System.Threading.Tasks;
using SkillzBot.MODELS;
using TwitchLib.Client.Events;
using SkillzBot.API.Twitch;
using System.Linq;
using urldetector.detection;
using SkillzBot.Singleton;
using SkillzBot.IRC;
using SkillzBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace SkillzBot.IllSkillzBot
{
    public sealed class IllChatFilters
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly ILogger<IllChatFilters> _logger;
        private readonly ConfPathes _dataPaths;
        private readonly ITwitchService _twitchService;

        private AhoCorasick _pichkaMatcher;
        private HashSet<string> _mediaBlacklist;
        private HashSet<string> _channelBlacklist;
        private HashSet<string> _dictionary;
        private readonly BannedWordsTrie _bannedWordsTrie = new();
        private HashSet<string> _whitelist;
        private HashSet<string> _userBlacklist;

        private static readonly int[] Arabic2 = Enumerable.Range('\ufb50', 687).ToArray();
        private const int CharsInRow = 29;
        private const int ArabCharsInRow = 4;
        private const int RowsNum = 3;

        public IllChatFilters(ITtvIRCClient ircClient, ILogger<IllChatFilters> logger, ITwitchService twitchService)
        {
            _ircClient = ircClient;
            _logger = logger;
            _dataPaths = IllSkillzBotMain.GetDataPath();
            ReloadFilters();
            _twitchService = twitchService;
        }

        public void ReloadFilters()
        {
            _logger.LogInformation("Reloading chat filters from files...");
            try
            {
                _mediaBlacklist = new HashSet<string>(File.ReadLines(Path.Combine(_dataPaths.sharedPath, IllSingleton.Config.FilePaths.MediaListFileName)));
                _channelBlacklist = new HashSet<string>(File.ReadLines(Path.Combine(_dataPaths.sharedPath, IllSingleton.Config.FilePaths.ChannelListFileName)));
                _dictionary = new HashSet<string>(File.ReadLines(Path.Combine(_dataPaths.sharedPath, IllSingleton.Config.FilePaths.DicFileName)));
                _whitelist = new HashSet<string>(File.ReadLines(Path.Combine(_dataPaths.sharedPath, IllSingleton.Config.FilePaths.DicWhiteListFileName)));
                _userBlacklist = new HashSet<string>(File.ReadLines(Path.Combine(_dataPaths.uniquePath, IllSingleton.Config.FilePaths.UserBlacklistFileName)));

                _bannedWordsTrie.BuildTrie(_dictionary);

                var pichkaLines = File.ReadLines(Path.Combine(_dataPaths.sharedPath, IllSingleton.Config.FilePaths.PichkaListFileName));
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
                await FlagWriter.FlagWriterTask($"{name} : {message} (detected: {bannedWord})").ConfigureAwait(false);
                if (IllSingleton.State.Debug)
                    await _ircClient.SendMessage($"Filter: {bannedWord}").ConfigureAwait(false);
                return true;
            }
            return false;
        }
        public async Task<List<string>> YouTubeFilter(string ID)
        {
            List<string> output = new List<string>();
            var yRes = await YouTubeSearch.YouTubeSearchByIDTask(ID).ConfigureAwait(false);
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
            return _userBlacklist.Contains(userID);
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
                    await _twitchService.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
                    return true;
                }
            }
            if (detectedUrls.Count > 1)
            {
                await _twitchService.DeleteMessage(e.ChatMessage.Id).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        public bool FilterASCII(OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.CustomRewardId != IllSingleton.Config.ChannelIds.Pi4KaId)
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
            _userBlacklist.Remove(UserTtvID);
        }
        public void AddToWhiteList(string WordToAdd)
        {
            _whitelist.Add(WordToAdd);
        }
    }
}