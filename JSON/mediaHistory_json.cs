
namespace SkillzBot.JSON.MediaHistory
{
    using System;
    using System.Collections.Generic;

    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public partial class MediaHistoryJSON
    {
        [JsonProperty("limit")]
        public int Limit { get; set; }

        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("history")]
        public List<History> History { get; set; }
    }

    public partial class History
    {
        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("song")]
        public Song Song { get; set; }

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    public partial class Song
    {
        [JsonProperty("duration")]
        public int Duration { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("statistics")]
        public Statistics Statistics { get; set; }

        [JsonProperty("voteskips")]
        public List<string> Voteskips { get; set; }

        [JsonProperty("videoId")]
        public string VideoId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }
    }

    public partial class Statistics
    {
        [JsonProperty("viewCount")]
        public int ViewCount { get; set; }

        [JsonProperty("likeCount")]
        public int LikeCount { get; set; }

        [JsonProperty("dislikeCount")]
        public int DislikeCount { get; set; }
    }

    public partial class User
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("providerId")]
        [JsonConverter(typeof(ParseStringConverter))]
        public string ProviderId { get; set; }
    }

    public partial class MediaHistoryJSON
    {
        public static MediaHistoryJSON FromJson(string json) => JsonConvert.DeserializeObject<MediaHistoryJSON>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this MediaHistoryJSON self) => JsonConvert.SerializeObject(self, Converter.Settings);
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

    internal class ParseStringConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(long) || t == typeof(long?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            return serializer.Deserialize<string>(reader);            
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (long)untypedValue;
            serializer.Serialize(writer, value.ToString());
            return;
        }

        public static readonly ParseStringConverter Singleton = new ParseStringConverter();
    }
}
