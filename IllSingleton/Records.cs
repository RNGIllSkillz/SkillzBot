namespace SkillzBot.Singleton
{
    public record DatabaseConfig(
        string Host,
        int Port,
        string Username,
        string Password
    );

    public record FilePathsConfig(
        string PichkaListFileName,
        string MediaListFileName,
        string ChannelListFileName,
        string DicFileName,
        string DicWhiteListFileName,
        string UserBlacklistFileName,
        string GameStateFileName,
        string BotStateFileName
    );

    public record ChannelIdsConfig(
        string CenceleUval,
        string EmoteModeId,
        string UvalMod,
        string UvalId,
        string Pi4KaId,
        string ZakazTrekaId,
        string UvalSabId,
        string UvalVipId
    );
}
