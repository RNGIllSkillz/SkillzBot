using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SkillzBot.JSON.MediaQueue
{  
    public partial class MediaQueueJson
    {
        [JsonProperty("duration")]
        public string Duration { get; set; }

        [JsonProperty("tags")]
        public List<object> Tags { get; set; }

        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("videoId")]
        public string VideoId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("statistics")]
        public Statistics Statistics { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("voteskips")]
        public List<object> Voteskips { get; set; }
    }

    public partial class Statistics
    {
        [JsonProperty("viewCount")]
        public string ViewCount { get; set; }

        [JsonProperty("likeCount")]
        public string LikeCount { get; set; }

        [JsonProperty("dislikeCount")]
        public string DislikeCount { get; set; }
    }

    public partial class User
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("providerId")]
        [JsonConverter(typeof(ParseStringConverter))]
        public string ProviderId { get; set; }
    }

    public partial class MediaQueueJson
    {
        public static List<MediaQueueJson> FromJson(string json) => JsonConvert.DeserializeObject<List<MediaQueueJson>>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this List<MediaQueueJson> self) => JsonConvert.SerializeObject(self, Converter.Settings);
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
