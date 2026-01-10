using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkillzBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace SkillzBot.API.MMR
{
    public class MmrApiService : IMmrService
    {
        private readonly HttpClient _client;
        private readonly ILogger<MmrApiService> _logger;
        private readonly ITtvIRCClient _ircClient;
        private const string BaseUrl = "https://api.mylolmmr.com/api/mmr/euw1/";

        public MmrApiService(HttpClient client, ILogger<MmrApiService> logger, ITtvIRCClient ircClient)
        {
            _client = client;
            _logger = logger;
            _ircClient = ircClient;
        }

        public async Task<List<string>> GetMMR(string summonerName)
        {
            string url = BaseUrl + summonerName + "/420";
            try
            {
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    dynamic json = JsonConvert.DeserializeObject(jsonResponse);
                    return new List<string>
                    {
                        json.name.ToString(),
                        json.mmr.ToString()
                    };
                }
                else
                {
                    await _ircClient.SendMessage("API call failed with status code " + response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMMR Failed");
                return null;
            }
        }
    }
}