using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SkillzBot.JSON.Whatismymmr
{
    public partial class WhatismymmrJSON
    {
        [JsonProperty("ranked")]
        public Ranked Ranked { get; set; }

        [JsonProperty("normal")]
        public Aram Normal { get; set; }

        [JsonProperty("ARAM")]
        public Aram Aram { get; set; }
    }

    public partial class Aram
    {
        [JsonProperty("avg")]
        public long? Avg { get; set; }

        [JsonProperty("err")]
        public long Err { get; set; }

        [JsonProperty("warn")]
        public bool Warn { get; set; }

        [JsonProperty("closestRank")]
        public string ClosestRank { get; set; }

        [JsonProperty("percentile")]
        public double? Percentile { get; set; }

        [JsonProperty("timestamp")]
        public long? Timestamp { get; set; }

        [JsonProperty("historical")]
        public Historical[] Historical { get; set; }
    }

    public partial class Historical
    {
        [JsonProperty("avg")]
        public long Avg { get; set; }

        [JsonProperty("err")]
        public long Err { get; set; }

        [JsonProperty("warn")]
        public bool Warn { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }
    }

    public partial class Ranked
    {
        [JsonProperty("avg")]
        public object Avg { get; set; }

        [JsonProperty("err")]
        public long Err { get; set; }

        [JsonProperty("warn")]
        public bool Warn { get; set; }

        [JsonProperty("summary")]
        public object Summary { get; set; }

        [JsonProperty("closestRank")]
        public object ClosestRank { get; set; }

        [JsonProperty("percentile")]
        public object Percentile { get; set; }

        [JsonProperty("tierData")]
        public object[] TierData { get; set; }

        [JsonProperty("timestamp")]
        public object Timestamp { get; set; }

        [JsonProperty("historical")]
        public object[] Historical { get; set; }

        [JsonProperty("historicalTierData")]
        public object[] HistoricalTierData { get; set; }
    }

    public partial class Whatismymmr
    {
        public static Whatismymmr FromJson(string json) => JsonConvert.DeserializeObject<Whatismymmr>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this Whatismymmr self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }
}