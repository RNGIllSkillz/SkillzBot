using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SkillzBot.JSON.nChatters
{
    public partial class SChatters
    {
        [JsonProperty("_links")]
        public Links Links { get; set; }

        [JsonProperty("chatter_count")]
        public long ChatterCount { get; set; }

        [JsonProperty("chatters")]
        public Chatters Chatters { get; set; }
    }

    public partial class Chatters
    {
        [JsonProperty("broadcaster")]
        public List<string> Broadcaster { get; set; }

        [JsonProperty("vips")]
        public List<string> Vips { get; set; }

        [JsonProperty("moderators")]
        public List<string> Moderators { get; set; }

        [JsonProperty("staff")]
        public List<object> Staff { get; set; }

        [JsonProperty("admins")]
        public List<object> Admins { get; set; }

        [JsonProperty("global_mods")]
        public List<object> GlobalMods { get; set; }

        [JsonProperty("viewers")]
        public List<string> Viewers { get; set; }
    }

    public partial class Links
    {
    }

    public partial class SChatters
    {
        public static SChatters FromJson(string json) => JsonConvert.DeserializeObject<SChatters>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this SChatters self) => JsonConvert.SerializeObject(self, Converter.Settings);
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
