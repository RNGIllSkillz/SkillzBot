using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Camille.Enums;
using Camille.RiotGames.LeagueV4;
using SkillzBot.Singleton;

namespace SkillzBot.API.RiotGames
{
    internal class HttpHandler
    {
        public static async Task<LeagueEntry[]> GetLeagueEntriesByPUUIDAsync(PlatformRoute platformRoute, string puuid)
        {
            using var client = new HttpClient();
            string splatformRoute = platformRoute.ToString();
            client.DefaultRequestHeaders.Add("X-Riot-Token", IllSingleton.Config.RiotApiToken);
            var response = await client.GetAsync($"https://{splatformRoute}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}");
            response.EnsureSuccessStatusCode();
            string jsonString = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<LeagueEntry[]>(jsonString);
        }
    }
}