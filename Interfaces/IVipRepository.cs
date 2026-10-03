using SkillzBot.MODELS;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillzBot.Interfaces
{
    /// <summary>Storage for the VIP registry (table dbVipTable).</summary>
    public interface IVipRepository
    {
        Task<List<VipRecord>> GetVipsAsync();
        Task UpsertVipAsync(VipRecord record);
        Task<bool> DeleteVipAsync(long twitchId);
    }
}
