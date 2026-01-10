using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    public interface IMmrService
    {
        Task<List<string>> GetMMR(string summonerName);
    }
}