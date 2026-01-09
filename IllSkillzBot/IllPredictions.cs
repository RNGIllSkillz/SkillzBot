using System;
using System.Threading.Tasks;
using SkillzBot.API.Twitch;
using SkillzBot.Utils;
using SkillzBot.IRC;
using System.Linq;
using Camille.Enums;
using Camille.RiotGames.MatchV5;
using Camille.RiotGames.SpectatorV5;
using SkillzBot.API.RiotGames;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Singleton;
using SkillzBot.Interfaces;

namespace SkillzBot.IllSkillzBot
{
    internal sealed class IllPredictions
    {
        private static string CurrentMatchID;
        private static string PlatformID;
        private static readonly int _maxGameLengthsec = 5400;
        private static readonly ILogger<IllPredictions> _logger = IllServiceProvider.GetLogger<IllPredictions>();
        private static IRiotApiService RiotAPI = IllServiceProvider.GetService<IRiotApiService>();
        private static ITtvIRCClient IRCClient = IllServiceProvider.GetService<ITtvIRCClient>();
        public static async Task GetCurrentMatchTask()
        {
            if (IllSingleton.State.Debug) _logger.LogDebug("Running GetCurrentMatchTask()");
            if (!IllSingleton.State.isSubActive || IllSingleton.State.InMatch || !IllSingleton.State.AutoPred) return;

            PlatformID = IllSingleton.Game.SummonerRegion switch
            {
                "ru" => "RU_",
                "euw" => "EUW1_",
                "na" => "NA1_",
                _ => "EUW1_",
            };

            var currentGame = await RiotAPI.GetCurrentGameAsync().ConfigureAwait(false);
            if (currentGame == null) return;
            if (CurrentMatchID == (PlatformID + Convert.ToString(currentGame.GameId)) || currentGame.GameLength > 30) return;
            if (IllSingleton.State.Debug)
                _logger.LogDebug("Матч начался!");

            CurrentMatchID = PlatformID + Convert.ToString(currentGame.GameId);
            var predictions = await TtvAPI.GetCurrentPredPublic().ConfigureAwait(false);
            if (predictions == null || predictions.Data.Length == 0) return;
            if (predictions.Data.First().Status != TwitchLib.Api.Core.Enums.PredictionStatus.RESOLVED && predictions.Data.First().Status != TwitchLib.Api.Core.Enums.PredictionStatus.CANCELED) return;
            if (currentGame.GameType == GameType.CUSTOM)
            {  
                //await TtvIRCClient.SendMessage("Кастомные игры не поддерживаются. Ставка не запустится.");
                //return;
            }
            //await DisableRewardAsync().ConfigureAwait(false);
            //await CalculateGameStats(currentGame).ConfigureAwait(false);
            string currentGameID = PlatformID + Convert.ToString(currentGame.GameId);
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
            if (rank == null) return;
            IllSingleton.State.InMatch = true;
            int wchance = 100;
            foreach (var mType in rank)
            {
                if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
                    wchance = 100;
            }
            while (true)
            {
                try
                {
                    if (IntUtil.GetChance(wchance))
                    {
                        await Prediction_WIN_LOOSE(currentGameID, "Вин или луз?", "вин", "луз", 180).ConfigureAwait(false);
                        break;
                    }
                    /*if (IntUtil.GetChance(15))
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
                    }*/
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error starting prediction task");
                    IllSingleton.State.InMatch = false; 
                }
            }
            IllSingleton.State.InMatch = false;
        }
        private static async Task Prediction_WIN_LOOSE(string currentGameID, string Title, string blue, string red, int sec)
        {
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            Match onMatch;
            if (IllSingleton.State.Debug)
            {
                _logger.LogDebug("Ставка запущена");
                _logger.LogDebug("currentGameID: {CurrentGameID}", currentGameID);
            }
            int errorThreshHold = 0;
            var maxGameTime = DateTimeOffset.Now.ToUnixTimeSeconds() + _maxGameLengthsec;

            try
            {
                while (IllSingleton.State.InMatch)
                {
                    if (DateTimeOffset.Now.ToUnixTimeSeconds() > maxGameTime)
                    {
                        IllSingleton.State.InMatch = false;
                        await IRCClient.SendMessage($"Кажется я забаговал. Матч длится 1.5 часа. Прекращаю отслеживать матч с ID:{currentGameID}");
                        _logger.LogWarning("Match tracking timed out after 1.5 hours for Match ID: {CurrentGameID}", currentGameID);
                        break;
                    }

                    try
                    {
                        onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Prediction_WIN_LOOSE_1");
                        errorThreshHold++;

                        if (errorThreshHold > 5)
                        {
                            IllSingleton.State.InMatch = false;
                            break;
                        }
                        else
                        {
                            await Task.Delay(2000).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (errorThreshHold != 0)
                    {
                        _logger.LogInformation("Recovered from {ErrorCount} consecutive API errors", errorThreshHold);
                        errorThreshHold = 0;
                    }

                    if (onMatch == null)
                    {
                        await Task.Delay(4000).ConfigureAwait(false);
                        continue;
                    }

                    IllSingleton.State.InMatch = false;
                    var participant = RiotAPI.GetParticipantByMatch(onMatch);
                    if (participant != null)
                    {
                        if (onMatch.Info.GameDuration > 300)
                        {
                            IllSingleton.Game.NumGames++;
                            bool won = participant.Win;

                            await TtvAPI.End_WinLoose_Prediction(won, 0).ConfigureAwait(false);
                            if (IllSingleton.State.Debug)
                                _logger.LogDebug("Матч завершен {Win}", won);

                            if (won)
                                IllSingleton.Game.NumWins++;
                            else
                                IllSingleton.Game.NumLosses++;

                            await UpdateDailyStats(won).ConfigureAwait(false);
                            await IllSingleton.Game.SaveAsync();
                        }
                        else
                        {
                            await IRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        IllSingleton.State.AutoPred = false;
                        _logger.LogCritical("Critical error in GetParticipantByMatch. Participant could not be found. Auto-predictions disabled.");
                        await IRCClient.SendMessage("Критическая ошибка: не удалось найти призывателя в матче. Автоставки выключены.");
                    }
                }
            }
            finally
            {
                IllSingleton.State.InMatch = false;
            }
        }
        /*
        private static async Task Prediction_MAX_KDA(CurrentGameInfo CurrentGame, string currentGameID, string Title, int windowSec)
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    IllSingleton.State.InMatch = false;
                    IllSingleton.Game.NumGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumLosses++;
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
                            await TtvIRCClient.SendMessage(output);
                    }
                    else
                    {
                        await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    IllSingleton.State.InMatch = false;
                    await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA_2");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    IllSingleton.State.InMatch = false;
                    IllSingleton.Game.NumGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumLosses++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    int TeamID = 0;
                    string getPosition = "";
                    foreach (var champGetData in FinParticipants)
                    {
                        if (champGetData.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
                        await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else if (Players.Count > 2)
                    {
                        await TtvIRCClient.SendMessage("Ошибка распознавания роли! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else
                    {
                        if (Players[0].Flag > Players[1].Flag)
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(true, 0).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(false, 0).ConfigureAwait(false);
                        }
                        else
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(false, 0).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(true, 0).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    IllSingleton.State.InMatch = false;
                    await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
                if (champ.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_KDA_5");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    IllSingleton.State.InMatch = false;
                    IllSingleton.Game.NumGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumLosses++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    foreach (var champ in FinParticipants)
                    {
                        if (champ.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
                        await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    IllSingleton.State.InMatch = false;
                    await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
                if (champ.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG_5");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.State.InMatch = false;
                        IllSingleton.Game.NumGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.Game.NumWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.Game.NumLosses++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
                            await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        IllSingleton.State.InMatch = false;
                        await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG_2");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    IllSingleton.State.InMatch = false;
                    IllSingleton.Game.NumGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumWins++;
                        await UpdateDailyStats(true).ConfigureAwait(false);
                    }
                    else
                    {
                        IllSingleton.Game.NumLosses++;
                        await UpdateDailyStats(false).ConfigureAwait(false);
                    }
                    IllCommands.SaveGameStats();
                    List<PlayersObject> Players = new List<PlayersObject>();
                    var FinParticipants = onMatch.Info.Participants.ToArray();
                    int TeamID = 0;
                    string getPosition = "";
                    foreach (var champGetData in FinParticipants)
                    {
                        if (champGetData.SummonerName.Equals(IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
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
                        await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else if (Players.Count > 2)
                    {
                        await TtvIRCClient.SendMessage("Ошибка распознавания роли! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                    else
                    {
                        if (Players[0].Flag > Players[1].Flag)
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(true, 0).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(false, 0).ConfigureAwait(false);
                        }
                        else
                        {
                            if (Players[0].teamID == TeamID)
                                await TtvAPI.End_WinLoose_Prediction(false, 0).ConfigureAwait(false);
                            else
                                await TtvAPI.End_WinLoose_Prediction(true, 0).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    IllSingleton.State.InMatch = false;
                    await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
            while (IllSingleton.State.InMatch)
            {
                try
                {
                    onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "Prediction_MAX_FLAG");
                    IllSingleton.State.InMatch = false;
                    break;
                }
                if (onMatch == null)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    continue;
                }
                if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                {
                    IllSingleton.State.InMatch = false;
                    IllSingleton.Game.NumGames++;
                    if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumWins++;
                        var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                        if (buffdata != null)
                        {
                            int bufflp = int.Parse(buffdata[1]);
                            if (buffdata[0] != IllSingleton.Game.Elo & buffdata[2] != "MASTER")
                            {
                                IllSingleton.Game.EarnedLP += 100 - IllSingleton.Game.StartLP;
                                IllSingleton.Game.StartLP = 0;
                                IllSingleton.Game.Elo = buffdata[0];
                            }
                            IllSingleton.Game.EarnedLP += bufflp - IllSingleton.Game.StartLP;
                            IllSingleton.Game.StartLP = bufflp;
                        }
                    }
                    if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                    {
                        IllSingleton.Game.NumLosses++;
                        var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                        if (buffdata != null)
                        {
                            int bufflp = int.Parse(buffdata[1]);
                            if (buffdata[0] != IllSingleton.Game.Elo & buffdata[2] != "MASTER")
                            {
                                IllSingleton.Game.StartLP = 100;
                                IllSingleton.Game.Elo = buffdata[0];
                                IllSingleton.Game.Tier = buffdata[2];
                            }
                            IllSingleton.Game.EarnedLP -= IllSingleton.Game.StartLP - bufflp;
                            IllSingleton.Game.StartLP = bufflp;
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
                        await TtvIRCClient.SendMessage("Спорный исход! Ставка будет отменена PoroSad");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                else
                {
                    IllSingleton.State.InMatch = false;
                    await TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
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
            int numberOfRankedPlayers = 0;
            int numberOfRankedEnemyPlayers = 0;
            long teamid = 0;
            foreach (var champ in champs)
            {
                if (string.Equals(champ.SummonerName, IllSingleton.Game.SummonerName, StringComparison.OrdinalIgnoreCase))
                {
                    teamid = champ.TeamId;
                    break;
                }
            }

            foreach (var champ in champs)
            {
                var data = await GettInfo(champ.SummonerName).ConfigureAwait(false);
                if (data == null) return;
                if (champ.TeamId == teamid)
                {
                    if (data[0] != 0 || data[1] != 0)
                        numberOfRankedPlayers++;
                    teamWr += data[0];
                    teamElo += data[1];
                }
                else
                {
                    if (data[0] != 0 || data[1] != 0)
                        numberOfRankedEnemyPlayers++;
                    enemyWr += data[0];
                    enemyElo += data[1];
                }
            }
            teamWr = numberOfRankedPlayers > 0 ? (int)Math.Ceiling((double)teamWr / numberOfRankedPlayers) : 0;
            enemyWr = numberOfRankedEnemyPlayers > 0 ? (int)Math.Ceiling((double)enemyWr / numberOfRankedEnemyPlayers) : 0;
            teamElo = numberOfRankedPlayers > 0 ? (int)Math.Ceiling((double)teamElo / numberOfRankedPlayers) : 0;
            enemyElo = numberOfRankedEnemyPlayers > 0 ? (int)Math.Ceiling((double)enemyElo / numberOfRankedEnemyPlayers) : 0;
            string elo = StringUtil.ConvertRank(teamElo.ToString(), false);
            string elo2 = StringUtil.ConvertRank(enemyElo.ToString(), false);
            await TtvIRCClient.SendMessage($"Среднее ило команды союзников: {elo}, средний WR {teamWr}%. Среднее ило команды противников {elo2}, средний WR {enemyWr}%");
        }
        
        private static async Task<int[]> GettInfo(string summonerName)
        {
            int[] data = new int[2];
            data[0] = 0;
            data[1] = 0;
            int numtryes = 0;
            var summoner = await RiotAPI.GetSummonerByNameAsync(summonerName).ConfigureAwait(false);
            while (summoner == null && numtryes < 5)
            {
                summoner = await RiotAPI.GetSummonerByNameAsync(summonerName).ConfigureAwait(false);
                numtryes++;
                Thread.Sleep(2000);
            }
            if (summoner == null)
                return data;
            var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync(summoner.Id).ConfigureAwait(false);
            if (rank == null) return data;
            var rankedSoloQueue = rank.FirstOrDefault(Queues => Queues.QueueType == "RANKED_SOLO_5x5");
            if (rankedSoloQueue != null)
            {
                data[0] = (int)Math.Round((double)rankedSoloQueue.Wins * 100 / (rankedSoloQueue.Wins + rankedSoloQueue.Losses), MidpointRounding.AwayFromZero);
                var sRank = $"{rankedSoloQueue.Tier} {rankedSoloQueue.Rank}";
                data[1] = int.Parse(StringUtil.ConvertRank(sRank, true));
            }
            return data;
        }    */

        private static async Task UpdateDailyStats(bool won)
        {
            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            const int LowEloMaxLP = 100;
            if (buffdata == null) return;
            if (int.TryParse(buffdata[1], out int bufflp))
            {
                bool isHighElo = buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) ||
                                 buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) ||
                                 buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase);

                if (won)
                {
                    if (!isHighElo)
                    {
                        if (buffdata[0] != IllSingleton.Game.Elo || buffdata[2] != IllSingleton.Game.Tier) // Promoted
                        {
                            IllSingleton.Game.EarnedLP += LowEloMaxLP - IllSingleton.Game.StartLP + bufflp;
                            IllSingleton.Game.StartLP = bufflp; // StartLP in the new division
                        }
                        else // Regular win
                        {
                            IllSingleton.Game.EarnedLP += bufflp - IllSingleton.Game.StartLP;
                            IllSingleton.Game.StartLP = bufflp;
                        }
                    }
                    else // High Elo win
                    {
                        if (IllSingleton.Game.Tier.Equals("diamond", StringComparison.OrdinalIgnoreCase)) // Promoted to Master
                            IllSingleton.Game.EarnedLP += LowEloMaxLP - IllSingleton.Game.StartLP + bufflp;
                        else
                            IllSingleton.Game.EarnedLP += bufflp - IllSingleton.Game.StartLP;
                        IllSingleton.Game.StartLP = bufflp;
                    }
                }
                else // Lost
                {
                    if (!isHighElo)
                    {
                        if (buffdata[0] != IllSingleton.Game.Elo || buffdata[2] != IllSingleton.Game.Tier) // Demoted
                        {
                            IllSingleton.Game.EarnedLP -= IllSingleton.Game.StartLP + (LowEloMaxLP - bufflp);
                            IllSingleton.Game.StartLP = bufflp;
                        }
                        else // Regular loss
                        {
                            IllSingleton.Game.EarnedLP -= IllSingleton.Game.StartLP - bufflp;
                            IllSingleton.Game.StartLP = bufflp;
                        }
                    }
                    else // High Elo loss
                    {
                        IllSingleton.Game.EarnedLP -= IllSingleton.Game.StartLP - bufflp;
                        IllSingleton.Game.StartLP = bufflp;
                    }
                }
                IllSingleton.Game.Elo = buffdata[0];
                IllSingleton.Game.Tier = buffdata[2];
            }
            else
            {
                _logger.LogError("UpdateDailyStats() -> cant convert LP to int. buffdata: {data}", string.Join(" ", buffdata));
            }
        }
    }
}