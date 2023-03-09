using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SkillzBot.JSON.StreamElements
{
    public partial class StreamElementsJSON
    {
        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("statistics")]
        public Statistics Statistics { get; set; }

        [JsonProperty("duration")]
        public long Duration { get; set; }

        [JsonProperty("tags")]
        public object[] Tags { get; set; }

        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("voteskips")]
        public object[] Voteskips { get; set; }

        [JsonProperty("videoId")]
        public string VideoId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("amount")]
        public long Amount { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("promoted")]
        public bool Promoted { get; set; }
    }

    public partial class Statistics
    {
        [JsonProperty("viewCount")]
        public long ViewCount { get; set; }

        [JsonProperty("likeCount")]
        public long LikeCount { get; set; }

        [JsonProperty("dislikeCount")]
        public long DislikeCount { get; set; }
    }

    public partial class User
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("providerId")]
        public string ProviderId { get; set; }

        [JsonProperty("subscriber")]
        public bool Subscriber { get; set; }
    }

    public partial class Settings
    {
        public static Settings FromJson(string json) => JsonConvert.DeserializeObject<Settings>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this Settings self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }
}