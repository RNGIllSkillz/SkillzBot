using System.Threading.Tasks;
using System.Xml;
using System;
using System.Collections.Generic;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System.Linq;
using Org.BouncyCastle.Asn1.Ocsp;
using SkillzBot.Readers;
using SkillzBot.Singleton;
using Google.Apis.YouTube.v3.Data;
using SkillzBot.WRITERS;

namespace SkillzBot.API.YouTube
{
    internal sealed class YouTubeSearch
    {
        private readonly static YouTubeService _YouTubeService;
        private readonly static List<string> _request;
        static YouTubeSearch()
        {
            _YouTubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = IllSingleton.GetInstance().YouTubeApiToken,
                ApplicationName = "IllSKillzBot v2.0"
            });
            _request = new List<string>
            {
                 "ContentDetails",
                 "Statistics",
                 "Snippet",
                 "Status"
            };
        }
        public static async Task<List<string>> YouTubeSearchByIDTask(string vidID)
        {
            var searchRequest = _YouTubeService.Videos.List(_request);
            searchRequest.Id = vidID;
            VideoListResponse searchResponse;
            try
            {
                searchResponse = await searchRequest.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "YouTubeSearchByIDTask");
                return null;
            }
            var youTubeVideo = searchResponse.Items[0];
            TimeSpan ts = XmlConvert.ToTimeSpan(youTubeVideo.ContentDetails.Duration);

            if (youTubeVideo.Statistics.ViewCount < 280000) return new List<string> { "view" };      
            if (ts.TotalSeconds >= 375) return new List<string> { "duration" };
            if (youTubeVideo.ContentDetails.ContentRating.YtRating != null) return new List<string> { "age" };
            if (youTubeVideo.Status.Embeddable != true) return new List<string> { "Embeddable" }; 

            return new List<string>
            {
                youTubeVideo.Snippet.Title,
                youTubeVideo.Snippet.ChannelTitle
            };
        }
        public static async Task<string> YouTubeSearchByKeyWordTask(string KeyWord)
        {
            var searchListRequest = _YouTubeService.Search.List("snippet");
            searchListRequest.Q = KeyWord;
            searchListRequest.MaxResults = 10;
            SearchListResponse searchListResponse;
            try
            {
                searchListResponse = await searchListRequest.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "YouTubeSearchByKeyWordTask");
                return null;
            }           
            var searchRequest = _YouTubeService.Videos.List(_request);
            foreach (var searchResult in searchListResponse.Items)
            {
                if (searchResult.Id.VideoId != null)
                {
                    searchRequest.Id = searchResult.Id.VideoId;
                    var searchResponse = await searchRequest.ExecuteAsync().ConfigureAwait(false);
                    var youTubeVideo = searchResponse.Items.FirstOrDefault();

                    if (youTubeVideo != null && youTubeVideo.Statistics.ViewCount >= 280000
                        && XmlConvert.ToTimeSpan(youTubeVideo.ContentDetails.Duration).TotalSeconds < 375
                        && youTubeVideo.ContentDetails.ContentRating.YtRating == null
                        && youTubeVideo.Status.Embeddable == true)
                    {
                        return searchResult.Id.VideoId.ToString();
                    }
                }
            }
            return null;
        }
    }
}