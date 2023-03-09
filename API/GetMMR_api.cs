using System;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using SkillzBot.IRC;
using System.Collections.Generic;

namespace SkillzBot.API.MMR
{
    public class MyLOLMMRApi
    {
        private static readonly HttpClient client;
        private static readonly string baseUrl = "https://api.mylolmmr.com/api/mmr/euw1/";

        static MyLOLMMRApi()
        {
            client = new HttpClient();
        }

        public static async Task<List<string>> GetMMR(string summonerName)
        {
            string url = baseUrl + summonerName + "/420";
            HttpResponseMessage response = await client.GetAsync(url);

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
                TtvIRCClient.SendMessage("API call failed with status code " + response.StatusCode);
                return null;
            }
        }
    }
}