using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkillzBot.API.Twitch;
using SkillzBot.API.Riot;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
using SkillzBot.Singleton;
using SkillzBot.IRC;
using System.Linq;
using RiotSharp.Endpoints.SpectatorEndpoint;
using RiotSharp.Endpoints.MatchEndpoint;

namespace SkillzBot.IllSkillzBot
{
    internal sealed class IllPredictions
    {
        private readonly static IllSingleton singleton = IllSingleton.GetInstance();
        private readonly static string tChannel = singleton.ChannelName;
        //private readonly static string englishWis = singleton.EnglishWis;        
        private static string CurrentMatchID;
        private static string PlatformID;
        public static async Task GetCurrentMatchTask()
        {
            if (singleton.inAmatch || !singleton.autoPred) return;
            PlatformID = singleton.SummonerRegion switch
            {
                "ru" => "RU_",
                "euw" => "EUW1_",
                "na" => "NA1_",
                _ => "EUW1_",
            };
            //await EnableRewardAsync().ConfigureAwait(false);
            var currentGame = await RiotAPI.GetCurrentGameAsync().ConfigureAwait(false);
            if (currentGame == null) return;
            if (CurrentMatchID == (PlatformID + Convert.ToString(currentGame.GameId)) || currentGame.GameLength.TotalMilliseconds > 30) return;
            if (singleton.debug)
                Log.WriteLog(null, "Матч начался!");
            CurrentMatchID = PlatformID + Convert.ToString(currentGame.GameId);
            var predictions = await TtvAPI.GetCurrentPredPublic().ConfigureAwait(false);
            if (predictions == null) return;
            if (predictions.Data.First().Status != TwitchLib.Api.Core.Enums.PredictionStatus.RESOLVED && predictions.Data.First().Status != TwitchLib.Api.Core.Enums.PredictionStatus.CANCELED) return;
            if (currentGame.GameType == RiotSharp.Misc.GameType.CustomGame)
            {
                TtvIRCClient.SendMessage("Кастомные игры не поддерживаются. Ставка не запустится.");
                return;
            }
            //await DisableRewardAsync().ConfigureAwait(false);
            await CalculateGameStats(currentGame).ConfigureAwait(false);
            string currentGameID = PlatformID + Convert.ToString(currentGame.GameId);
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
            if (rank == null) return;
            singleton.inAmatch = true;
            int wchance = 65;
            foreach (var mType in rank)
            {
                if (mType.QueueType == "RANKED_SOLO_5x5")
                    wchance = 93;
            }
            while (true)
            {
                if (IntUtil.GetChance(wchance))
                {
                    await Prediction_WIN_LOOSE(currentGameID, "Вин или луз?", "вин", "луз", 120).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(15))
                {
                    await Prediction_MAX_FLAG_2(currentGameID, "У кого будет больше убийств", tChannel, "Оппонент", p => p.Kills, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(20))
                {
                    await Prediction_MAX_FLAG_2(currentGameID, "У кого будет больше CS", tChannel, "Оппонент", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(20))
                {
                    await Prediction_MAX_FLAG_2(currentGameID, "Кто заработает больше золота", tChannel, "Оппонент", p => p.GoldEarned, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(20))
                {
                    await Prediction_MAX_FLAG_2(currentGameID, "Чей урон будет выше", tChannel, "Оппонент", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(30))
                {
                    await Prediction_MAX_KDA_2(currentGameID, "Чей KDA будет больше", tChannel, "Оппонент", 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(25))
                {
                    await Prediction_MAX_FLAG_5(currentGame, currentGameID, "У кого будет больше всего убийств", p => p.Kills, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(20))
                {
                    await Prediction_MAX_FLAG_5(currentGame, currentGameID, "У кого будет самый большой CS", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(15))
                {
                    await Prediction_MAX_FLAG_5(currentGame, currentGameID, "Кто заработает больше золота", p => p.GoldEarned, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(18))
                {
                    await Prediction_MAX_FLAG_5(currentGame, currentGameID, "У кого будет самый высокий урон", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(20))
                {
                    await Prediction_MAX_KDA_5(currentGame, currentGameID, "У кого будет самый высокий KDA", 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(5))
                {
                    await Prediction_MAX_FLAG(currentGame, currentGameID, "У кого будет больше всего убийств", p => p.Kills, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(5))
                {
                    await Prediction_MAX_FLAG(currentGame, currentGameID, "У кого будет самый большой CS", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(4))
                {
                    await Prediction_MAX_FLAG(currentGame, currentGameID, "Кто заработает больше золота", p => p.GoldEarned, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(4))
                {
                    await Prediction_MAX_FLAG(currentGame, currentGameID, "У кого будет самый высокий урон", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                    break;
                }
                if (IntUtil.GetChance(5))
                {
                    await Prediction_MAX_KDA(currentGame, currentGameID, "У кого будет самый высокий KDA", 300).ConfigureAwait(false);
                    break;
                }
            }
            singleton.inAmatch = false;
        }
        private static async Task Prediction_WIN_LOOSE(string currentGameID, string Title, string blue, string red, int sec)
        {
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            Match onMatch;
            if (singleton.debug)
            {
                Log.WriteLog(null, "Ставка запущена");
                Log.WriteLog(null, $"currentGameID: {currentGameID}");
            }
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_WIN_LOOSE_1");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                singleton.inAmatch = false;
                var Participant = RiotAPI.GetParticipantByMatch(onMatch);
                if (Participant != null)
                {
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {                        
                        singleton.numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                            if (singleton.debug)
                                Log.WriteLog(null, $"Матч завершен {RiotAPI.GetParticipantByMatch(onMatch).Winner}");
                            singleton.numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        else
                        {
                            await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                            if (singleton.debug)
                                Log.WriteLog(null, $"Матч завершен {RiotAPI.GetParticipantByMatch(onMatch).Winner}");
                            singleton.numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                    }
                    else
                    {
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    singleton.autoPred = false;
                    Log.WriteLog(null, "(Prediction_WIN_LOOSE) Критическая ошибка в методе GetOutcome(RioTtvAPI.Endpoints.MatchEndpoint.Participant), Participant не может быть null.");
                    TtvIRCClient.SendMessage("Критическая ошибка в методе GetOutCome(RiotAPI.Endpoints.MatchEndpoint.Participant), Participant не может быть null. Автоставки выключены");
                }                
            }
        }
        private static async Task Prediction_MAX_KDA(CurrentGame CurrentGame, string currentGameID, string Title, int windowSec)
        {
            Match onMatch;
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in CurrentGame.Participants)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);                
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_10_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    singleton.inAmatch = false;
                    singleton.numGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numLoose++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var CompPartisList = new PlayersObject
                    {
                        Flag = 0
                    };
                    foreach (var Participant in onMatch.Info.Participants)
                    {
                        long vFlag;
                        if (Participant.Deaths != 0)
                            vFlag = (Participant.Kills + Participant.Assists) / Participant.Deaths;
                        else
                            vFlag = Participant.Kills + Participant.Assists;
                        Players.Add(new PlayersObject()
                        {
                            champ = Participant.ChampionName,
                            Flag = vFlag
                        });
                    }
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag < Player.Flag)
                        {
                            CompPartisList.champ = Player.champ;
                            CompPartisList.Flag = Player.Flag;
                        }
                    }
                    int outnum = 0;
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag == Player.Flag)
                        {
                            outnum++;
                        }
                    }
                    if (outnum == 1)
                    {
                        string output = await TtvAPI.End_Multy_Prediction(CompPartisList.champ).ConfigureAwait(false);
                        if (output != "OK")
                            TtvIRCClient.SendMessage(output);
                    }
                    else
                    {
                        TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    singleton.inAmatch = false;
                    TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                    await TtvAPI.CencelePrediction().ConfigureAwait(false);
                }
            }
        }
        private static async Task Prediction_MAX_KDA_2(string currentGameID, string Title, string blue, string red, int sec)
        {
            Match onMatch;
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA_2");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    singleton.inAmatch = false;
                    singleton.numGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numLoose++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    int TeamID = 0;
                    string getPosition = "";
                    foreach (var champGetData in FinParticipants)
                    {
                        if (champGetData.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                        {
                            TeamID = champGetData.TeamId;
                            getPosition = champGetData.IndividualPosition;
                        }
                    }
                    foreach (var champ in FinParticipants)
                    {
                        if (champ.TeamPosition == getPosition)
                        {
                            long vFlag;
                            if (champ.Deaths != 0)
                                vFlag = (champ.Kills + champ.Assists) / champ.Deaths;
                            else
                                vFlag = champ.Kills + champ.Assists;
                            Players.Add(new PlayersObject()
                            {
                                Flag = vFlag,
                                teamID = champ.TeamId
                            });
                        }
                    }
                    if (Players[0].Flag == Players[1].Flag)
                    {
                        TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else if (Players.Count > 2)
                    {
                        TtvIRCClient.SendMessage("Ошибка распознавания роли! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else
                    {
                        if (Players[0].Flag > Players[1].Flag)
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                        }
                        else
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    singleton.inAmatch = false;
                    TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                    await TtvAPI.CencelePrediction().ConfigureAwait(false);
                }
            }
        }
        private static async Task Prediction_MAX_KDA_5(CurrentGame CurrentGame, string currentGameID, string Title, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            long teamid = 0;
            Match onMatch;
            foreach (var champ in Participants)
            {
                if (champ.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    teamid = champ.TeamId;
                }
            }
            CurrentGameParticipant[] teammates = new CurrentGameParticipant[5];
            int i = 0;
            foreach (var champ in Participants)
            {
                if (champ.TeamId == teamid)
                {
                    teammates[i] = champ;
                    i++;
                }
            }
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in teammates)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_5_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA_5");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    singleton.inAmatch = false;
                    singleton.numGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numLoose++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    foreach (var champ in FinParticipants)
                    {
                        if (champ.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                        {
                            teamid = champ.TeamId;
                        }
                    }
                    RiotSharp.Endpoints.MatchEndpoint.Participant[] teammatesFIN = new RiotSharp.Endpoints.MatchEndpoint.Participant[5];
                    i = 0;
                    foreach (var champ in FinParticipants)
                    {
                        if (champ.TeamId == teamid)
                        {
                            teammatesFIN[i] = champ;
                            i++;
                        }
                    }
                    var CompPartisList = new PlayersObject
                    {
                        Flag = 0
                    };
                    foreach (var Participant in teammatesFIN)
                    {
                        long vFlag;
                        if (Participant.Deaths != 0)
                            vFlag = (Participant.Kills + Participant.Assists) / Participant.Deaths;
                        else
                            vFlag = Participant.Kills + Participant.Assists;
                        Players.Add(new PlayersObject()
                        {
                            champ = Participant.ChampionName,
                            Flag = vFlag
                        });
                    }
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag < Player.Flag)
                        {
                            CompPartisList.champ = Player.champ;
                            CompPartisList.Flag = Player.Flag;
                        }
                    }
                    int outnum = 0;
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag == Player.Flag)
                        {
                            outnum++;
                        }
                    }
                    if (outnum == 1)
                        await TtvAPI.End_Multy_Prediction(CompPartisList.champ).ConfigureAwait(false);
                    else
                    {
                        TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    singleton.inAmatch = false;
                    TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                    await TtvAPI.CencelePrediction().ConfigureAwait(false);
                }
            }
        }
        private static async Task Prediction_MAX_FLAG_5(CurrentGame CurrentGame, string currentGameID, string Title, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            long teamid = 0;
            Match onMatch;
            foreach (var champ in Participants)
            {
                if (champ.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    teamid = champ.TeamId;
                }
            }
            CurrentGameParticipant[] teammates = new CurrentGameParticipant[5];
            int i = 0;
            foreach (var champ in Participants)
            {
                if (champ.TeamId == teamid)
                {
                    teammates[i] = champ;
                    i++;
                }
            }
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in teammates)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_5_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG_5");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        singleton.inAmatch = false;
                        singleton.numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            singleton.numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            singleton.numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                            {
                                teamid = champ.TeamId;
                            }
                        }
                        RiotSharp.Endpoints.MatchEndpoint.Participant[] teammatesFIN = new RiotSharp.Endpoints.MatchEndpoint.Participant[5];
                        i = 0;
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.TeamId == teamid)
                            {
                                teammatesFIN[i] = champ;
                                i++;
                            }
                        }
                        var CompPartisList = new PlayersObject
                        {
                            Flag = 0
                        };
                        foreach (var Participant in teammatesFIN)
                        {
                            Players.Add(new PlayersObject()
                            {
                                champ = Participant.ChampionName,
                                Flag = func(Participant)
                            });
                        }
                        foreach (var Player in Players)
                        {
                            if (CompPartisList.Flag < Player.Flag)
                            {
                                CompPartisList.champ = Player.champ;
                                CompPartisList.Flag = Player.Flag;
                            }
                        }
                        int outnum = 0;
                        foreach (var Player in Players)
                        {
                            if (CompPartisList.Flag == Player.Flag)
                            {
                                outnum++;
                            }
                        }
                        if (outnum == 1)
                            await TtvAPI.End_Multy_Prediction(CompPartisList.champ).ConfigureAwait(false);
                        else
                        {
                            TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        singleton.inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }                
            }
        }
        private static async Task Prediction_MAX_FLAG_2(string currentGameID, string Title, string blue, string red, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int sec)
        {
            Match onMatch;
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG_2");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        singleton.inAmatch = false;
                        singleton.numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            singleton.numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            singleton.numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        int TeamID = 0;
                        string getPosition = "";
                        foreach (var champGetData in FinParticipants)
                        {
                            if (champGetData.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                            {
                                TeamID = champGetData.TeamId;
                                getPosition = champGetData.IndividualPosition;
                            }
                        }
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.TeamPosition == getPosition)
                            {
                                Players.Add(new PlayersObject()
                                {
                                    Flag = func(champ),
                                    teamID = champ.TeamId
                                });
                            }
                        }
                        if (Players[0].Flag == Players[1].Flag)
                        {
                            TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                        else if (Players.Count > 2)
                        {
                            TtvIRCClient.SendMessage("Ошибка распознавания роли! Ставка будет отменена PoroSad");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                        else
                        {
                            if (Players[0].Flag > Players[1].Flag)
                            {
                                if (Players[0].teamID == TeamID)
                                    await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                                else
                                    await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                            }
                            else
                            {
                                if (Players[0].teamID == TeamID)
                                    await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                                else
                                    await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        singleton.inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                
            }
        }
        private static async Task Prediction_MAX_FLAG(CurrentGame CurrentGame, string currentGameID, string Title, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int windowSec)
        {
            Match onMatch;
            var Participants = CurrentGame.Participants.ToArray();
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in Participants)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_10_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (singleton.debug)
                Log.WriteLog(null, "Ставка запущена");
            while (singleton.inAmatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG");
                    singleton.inAmatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    singleton.inAmatch = false;
                    singleton.numGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numWins++;
                        var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                        if (buffdata != null)
                        {
                            int bufflp = int.Parse(buffdata[1]);
                            if (buffdata[0] != singleton.elo & buffdata[2] != "MASTER")
                            {
                                singleton.earnedLP += 100 - singleton.startLP;
                                singleton.startLP = 0;
                                singleton.elo = buffdata[0];
                            }
                            singleton.earnedLP += bufflp - singleton.startLP;
                            singleton.startLP = bufflp;
                        }
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        singleton.numLoose++;
                        var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                        if (buffdata != null)
                        {
                            int bufflp = int.Parse(buffdata[1]);
                            if (buffdata[0] != singleton.elo & buffdata[2] != "MASTER")
                            {
                                singleton.startLP = 100;
                                singleton.elo = buffdata[0];
                                singleton.tier = buffdata[2];
                            }
                            singleton.earnedLP -= singleton.startLP - bufflp;
                            singleton.startLP = bufflp;
                        }
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    var CompPartisList = new PlayersObject
                    {
                        Flag = 0
                    };
                    foreach (var Participant in FinParticipants)
                    {
                        Players.Add(new PlayersObject()
                        {
                            champ = Participant.ChampionName,
                            Flag = func(Participant)
                        });
                    }
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag < Player.Flag)
                        {
                            CompPartisList.champ = Player.champ;
                            CompPartisList.Flag = Player.Flag;
                        }
                    }
                    int outnum = 0;
                    foreach (var Player in Players)
                    {
                        if (CompPartisList.Flag == Player.Flag)
                        {
                            outnum++;
                        }
                    }
                    if (outnum == 1)
                        await TtvAPI.End_Multy_Prediction(CompPartisList.champ).ConfigureAwait(false);
                    else
                    {
                        TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    singleton.inAmatch = false;
                    TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                    await TtvAPI.CencelePrediction().ConfigureAwait(false);
                }
            }
        }
        private static async Task CalculateGameStats(CurrentGame CurrentGame)
        {
            var champs = CurrentGame.Participants;
            int teamWr = 0;
            int enemyWr = 0;
            int teamElo = 0;
            int enemyElo = 0;
            long teamid = 0;
            foreach (var champ in champs)
            {
                if (champ.SummonerName.Equals(singleton.SUMMONER_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    teamid = champ.TeamId;
                }
            }

            foreach (var champ in champs)
            {
                int[] data = await GettInfo(champ.SummonerName).ConfigureAwait(false);
                if (champ.TeamId == teamid)
                {
                    teamWr += data[0];
                    teamElo += data[1];
                }
                else
                {
                    enemyWr += data[0];
                    enemyElo += data[1];
                }
            }
            teamWr = (int)Math.Ceiling((double)teamWr / 5);
            enemyWr = (int)Math.Ceiling((double)enemyWr / 5);
            teamElo = (int)Math.Ceiling((double)teamElo / 5);
            enemyElo = (int)Math.Ceiling((double)enemyElo / 5);
            string elo = StringUtil.ConvertRank(Convert.ToString(teamElo), false);
            string elo2 = StringUtil.ConvertRank(Convert.ToString(enemyElo), false);
            TtvIRCClient.SendMessage($"Среднее ило команды союзников: {elo}, средний WR {teamWr}%. Среднее ило команды противников {elo2}, средний WR {enemyWr}%");
        }
        private static async Task<int[]> GettInfo(string summonerName)
        {
            int[] data = new int[2];
            try
            {
                bool isRanked = false;
                var summoner = await RiotAPI.GetSummonerByNameAsync(summonerName).ConfigureAwait(false);
                var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync(summoner.Id).ConfigureAwait(false);
                foreach (var mType in rank)
                {
                    if (mType.QueueType == "RANKED_SOLO_5x5")
                    {
                        data[0] = (int)Math.Round((double)mType.Wins * 100 / (mType.Wins + mType.Losses), MidpointRounding.AwayFromZero);
                        var sRank = $"{mType.Tier} {mType.Rank}";
                        data[1] = int.Parse(StringUtil.ConvertRank(sRank, true));
                        isRanked = true;
                    }
                }
                if (!isRanked)
                {
                    data[0] = 0;
                    data[1] = 0;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "");
                data[0] = 0;
                data[1] = 0;
                return data;
            }
            return data;
        }        
        
        private static async Task UpdateDailyStats(bool won)
        {
            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            int LowEloMaxLP = 100;
            if (buffdata == null) return;
            if (int.TryParse(buffdata[1], out int bufflp))
                if (won)
                {
                    if (!buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) &&
                        !buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) &&
                        !buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase))
                    {
                        if (buffdata[0] != singleton.elo || buffdata[2] != singleton.tier)
                        {
                            singleton.earnedLP += LowEloMaxLP - singleton.startLP + bufflp;
                            singleton.startLP = 0;
                            singleton.elo = buffdata[0];
                            singleton.tier = buffdata[2];
                        }
                        else
                        {
                            singleton.earnedLP += bufflp - singleton.startLP;
                            singleton.startLP = bufflp;
                        }
                    }
                    else
                    {
                        if (singleton.tier.Equals("diamond", StringComparison.OrdinalIgnoreCase))
                            singleton.earnedLP += LowEloMaxLP - singleton.startLP + bufflp;
                        else
                            singleton.earnedLP += bufflp - singleton.startLP;
                        singleton.startLP = bufflp;
                        singleton.elo = buffdata[0];
                        singleton.tier = buffdata[2];
                    }
                }
                else
                {
                    if (!buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) &&
                        !buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) &&
                        !buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase))
                    {
                        if (buffdata[0] != singleton.elo || buffdata[2] != singleton.tier)
                        {
                            singleton.startLP = LowEloMaxLP;
                            singleton.elo = buffdata[0];
                            singleton.tier = buffdata[2];
                        }
                        singleton.earnedLP -= singleton.startLP - bufflp;
                        singleton.startLP = bufflp;
                    }
                    else
                    {
                        singleton.earnedLP -= singleton.startLP - bufflp;
                        singleton.startLP = bufflp;
                    }
                }
            else
                Log.WriteLog(null, $"UpdateDailyStats() -> cant convert to int. buffdata: {string.Join(" ", buffdata)}");
        }
    }
}