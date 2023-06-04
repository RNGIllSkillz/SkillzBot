using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SkillzBot.JSON.Settings
{
    public partial class SettingsJson
    {
        [JsonProperty("BotTwitchName")]
        public string BotTwitchName { get; set; }

        [JsonProperty("BotTwitchAuth")]
        public string BotTwitchAuth { get; set; }

        [JsonProperty("RiotApiToken")]
        public string RiotApiToken { get; set; }

        [JsonProperty("YouTubeApiToken")]
        public string YouTubeApiToken { get; set; }

        [JsonProperty("TApiClientId")]
        public string TApiClientId { get; set; }

        [JsonProperty("TApiAccessToken")]
        public string TApiAccessToken { get; set; }

        [JsonProperty("ChannelName")]
        public string ChannelName { get; set; }

        [JsonProperty("BrodcasterId")]
        public string BrodcasterId { get; set; }

        [JsonProperty("ZakazTrekaId")]
        public string ZakazTrekaId { get; set; }

        [JsonProperty("Pi4KaId")]
        public string Pi4KaId { get; set; }

        [JsonProperty("UvalId")]
        public string UvalId { get; set; }

        [JsonProperty("UvalSabId")]
        public string UvalSabId { get; set; }

        [JsonProperty("UvalVipId")]
        public string UvalVipId { get; set; }

        [JsonProperty("EmoteModeId")]
        public string EmoteModeId { get; set; }

        [JsonProperty("Summoner_Name")]
        public string SummonerName { get; set; }

        [JsonProperty("CenceleUval")]
        public string CenceleUval { get; set; }

        [JsonProperty("uvalMod")]
        public string uvalMod { get; set; }

        [JsonProperty("MySQL_User")]
        public string MySQL_User { get; set; }

        [JsonProperty("MySQL_password")]
        public string MySQL_password { get; set; }
        
        [JsonProperty("StreamElementsApiToken")] 
        public string StreamElementsApiToken { get; set; }

        [JsonProperty("StreamElementsID")]
        public string StreamElementsID { get; set; }

        [JsonProperty("ChatWithBot")]
        public string ChatWithBot { get; set; }

        [JsonProperty("ReleaseBot")]
        public string ReleaseBot { get; set; }

        [JsonProperty("GPTApiToken")]
        public string GPTApiToken { get; set; }

        [JsonProperty("SummonerRegion")]
        public string SummonerRegion { get; set; }

        [JsonProperty("MySQL_IP")]
        public string MySQL_IP { get; set; }

        [JsonProperty("MySQL_Port")]
        public int MySQL_Port { get; set; }
    }

    public partial class SettingsJson
    {
        public static SettingsJson FromJson(string json) => JsonConvert.DeserializeObject<SettingsJson>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this SettingsJson self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }

    internal class ParseStringConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(long) || t == typeof(long?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (Int64.TryParse(value, out long l))
            {
                return l;
            }
            throw new Exception("Cannot unmarshal type long");
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
