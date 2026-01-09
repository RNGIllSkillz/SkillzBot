using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Camille.Enums;
using Camille.RiotGames.LeagueV4;
using SkillzBot.Singleton;
using System;

namespace SkillzBot.API.RiotGames
{
    internal class HttpHandler
    {
        private static readonly HttpClient _client = new HttpClient();

        public static async Task<LeagueEntry[]> GetLeagueEntriesByPUUIDAsync(PlatformRoute platformRoute, string puuid)
        {
            string splatformRoute = platformRoute.ToString().ToLower();

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{splatformRoute}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}");
            request.Headers.Add("X-Riot-Token", IllSingleton.Config.RiotApiToken);

            using var response = await _client.SendAsync(request).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            string jsonString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<LeagueEntry[]>(jsonString);
        }
    }
}