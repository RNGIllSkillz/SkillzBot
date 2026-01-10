using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Camille.Enums;
using Camille.RiotGames.LeagueV4;
using SkillzBot.IllConfiguration;

namespace SkillzBot.API.RiotGames
{
    public class RiotHttpHandler
    {
        private readonly BotConfigModel _config;
        private readonly HttpClient _client;

        public RiotHttpHandler(BotConfigModel config, HttpClient client)
        {
            _config = config;
            _client = client;
        }

        public async Task<LeagueEntry[]> GetLeagueEntriesByPUUIDAsync(PlatformRoute platformRoute, string puuid)
        {
            string splatformRoute = platformRoute.ToString().ToLower();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{splatformRoute}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}");

            request.Headers.Add("X-Riot-Token", _config.RiotApiToken);

            using var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string jsonString = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<LeagueEntry[]>(jsonString);
        }
    }
}