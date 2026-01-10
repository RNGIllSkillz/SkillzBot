using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using SkillzBot.Interfaces;
using SkillzBot.IllConfiguration; 
using SkillzBot.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace SkillzBot.API.YouTube
{
    public class YouTubeApiService : IYouTubeService
    {
        private readonly YouTubeService _youTubeClient;
        private readonly ILogger<YouTubeApiService> _logger;
        private readonly bool _isValidToken;
        private readonly List<string> _requestParts;

        public YouTubeApiService(BotConfigModel config, ILogger<YouTubeApiService> logger)
        {
            _logger = logger;
            _isValidToken = StringUtil.IsValidApiToken(config.YouTubeApiToken);

            if (_isValidToken)
            {
                _youTubeClient = new YouTubeService(new BaseClientService.Initializer()
                {
                    ApiKey = config.YouTubeApiToken,
                    ApplicationName = "IllSkillzBot v3.0"
                });
            }
            else
            {
                _logger.LogWarning("YouTube API Token is invalid or missing. Service disabled.");
            }

            _requestParts = new List<string>
            {
                 "ContentDetails",
                 "Statistics",
                 "Snippet",
                 "Status"
            };
        }

        public async Task<List<string>> SearchByIdAsync(string vidID)
        {
            if (!_isValidToken) return null;

            try
            {
                var searchRequest = _youTubeClient.Videos.List(_requestParts);
                searchRequest.Id = vidID;

                var searchResponse = await searchRequest.ExecuteAsync();

                if (searchResponse.Items == null || searchResponse.Items.Count == 0)
                {
                    _logger.LogWarning("No YouTube video found for ID: {VideoID}", vidID);
                    return null;
                }

                var youTubeVideo = searchResponse.Items[0];
                TimeSpan ts = XmlConvert.ToTimeSpan(youTubeVideo.ContentDetails.Duration);

                // Validation Logic
                if (youTubeVideo.Statistics.ViewCount < 280000) return new List<string> { "view" };
                if (ts.TotalSeconds >= 375) return new List<string> { "duration" };
                if (youTubeVideo.ContentDetails.ContentRating?.YtRating != null) return new List<string> { "age" };
                if (youTubeVideo.Status.Embeddable != true) return new List<string> { "Embeddable" };

                return new List<string>
                {
                    youTubeVideo.Snippet.Title,
                    youTubeVideo.Snippet.ChannelTitle
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching YouTube by ID: {VideoID}", vidID);
                return null;
            }
        }

        public async Task<string> SearchByKeywordAsync(string keyWord)
        {
            if (!_isValidToken) return null;

            try
            {
                var searchListRequest = _youTubeClient.Search.List("snippet");
                searchListRequest.Q = keyWord;
                searchListRequest.MaxResults = 10;

                var searchListResponse = await searchListRequest.ExecuteAsync();
                var videoRequest = _youTubeClient.Videos.List(_requestParts);

                foreach (var searchResult in searchListResponse.Items)
                {
                    if (searchResult.Id.VideoId != null)
                    {
                        videoRequest.Id = searchResult.Id.VideoId;
                        var searchResponse = await videoRequest.ExecuteAsync();
                        var youTubeVideo = searchResponse.Items.FirstOrDefault();

                        if (youTubeVideo != null &&
                            youTubeVideo.Statistics.ViewCount >= 280000 &&
                            XmlConvert.ToTimeSpan(youTubeVideo.ContentDetails.Duration).TotalSeconds < 375 &&
                            youTubeVideo.ContentDetails.ContentRating?.YtRating == null &&
                            youTubeVideo.Status.Embeddable == true)
                        {
                            return searchResult.Id.VideoId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching YouTube by keyword: {Keyword}", keyWord);
            }
            return null;
        }
    }
}