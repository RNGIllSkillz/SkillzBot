using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    public interface IYouTubeService
    {
        Task<List<string>> SearchByIdAsync(string vidID);
        Task<string> SearchByKeywordAsync(string keyWord);
    }
}