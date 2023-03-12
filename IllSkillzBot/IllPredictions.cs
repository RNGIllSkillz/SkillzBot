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

namespace SkillzBot.IllSkillzBot
{
    internal sealed class IllPredictions
    {
        private readonly static string tChannel = IllSingleton.GetInstance().ChannelName;
        private readonly static string englishWis = IllSingleton.GetInstance().EnglishWis;
        private static string lastErrorMessage = null;

        static public async Task GetCurrentMatchTask()
        {
            if (IllSingleton.GetInstance().inAmatch) return;                       
            try
            {
                await EnableRewardAsync().ConfigureAwait(false);
                var CurrentGame = await RiotAPI.GetCurrentGameAsync().ConfigureAwait(false);
                double mLength = CurrentGame.GameLength.TotalMilliseconds;
                if (mLength < 30) 
                {
                    await DisableRewardAsync().ConfigureAwait(false);
                    IllSingleton.GetInstance().inAmatch = true;
                    await CalculateGameStats(CurrentGame).ConfigureAwait(false);
                    if (IllSingleton.GetInstance().debug)
                        Log.WriteLog(null, "Матч начался!");
                    string currentGameID = "EUW1_" + Convert.ToString(CurrentGame.GameId);
                    if (IllSingleton.GetInstance().autoPred)
                    {
                        bool procked = false;
                        var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
                        int wchance;
                        bool ranked = false;
                        foreach (var mType in rank)
                        {
                            if (mType.QueueType == "RANKED_SOLO_5x5")
                            {
                                ranked = true;
                            }
                        }
                        if (!ranked)
                            wchance = 65;
                        else
                            wchance = 93;
                        while (!procked)
                        {
                            if (!procked && IntUtil.GetChance(wchance))
                            {
                                await Prediction_WIN_LOOSE(currentGameID, "Вин или луз?", "вин", "луз", 120).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(15))
                            {
                                await Prediction_MAX_FLAG_2(currentGameID, "У кого будет больше убийств", tChannel, "Оппонент", p => p.Kills, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(20))
                            {
                                await Prediction_MAX_FLAG_2(currentGameID, "У кого будет больше CS", tChannel, "Оппонент", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(20))
                            {
                                await Prediction_MAX_FLAG_2(currentGameID, "Кто заработает больше золота", tChannel, "Оппонент", p => p.GoldEarned, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(20))
                            {
                                await Prediction_MAX_FLAG_2(currentGameID, "Чей урон будет выше", tChannel, "Оппонент", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(30))
                            {
                                await Prediction_MAX_KDA_2(currentGameID, "Чей KDA будет больше", tChannel, "Оппонент", 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(25))
                            {
                                await Prediction_MAX_FLAG_5(CurrentGame, currentGameID, "У кого будет больше всего убийств", p => p.Kills, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(20))
                            {
                                await Prediction_MAX_FLAG_5(CurrentGame, currentGameID, "У кого будет самый большой CS", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(15))
                            {
                                await Prediction_MAX_FLAG_5(CurrentGame, currentGameID, "Кто заработает больше золота", p => p.GoldEarned, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(18))
                            {
                                await Prediction_MAX_FLAG_5(CurrentGame, currentGameID, "У кого будет самый высокий урон", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(20))
                            {
                                await Prediction_MAX_KDA_5(CurrentGame, currentGameID, "У кого будет самый высокий KDA", 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(5))
                            {
                                await Prediction_MAX_FLAG(CurrentGame, currentGameID, "У кого будет больше всего убийств", p => p.Kills, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(5))
                            {
                                await Prediction_MAX_FLAG(CurrentGame, currentGameID, "У кого будет самый большой CS", p => p.TotalMinionsKilled, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(4))
                            {
                                await Prediction_MAX_FLAG(CurrentGame, currentGameID, "Кто заработает больше золота", p => p.GoldEarned, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(4))
                            {
                                await Prediction_MAX_FLAG(CurrentGame, currentGameID, "У кого будет самый высокий урон", p => p.TotalDamageDealtToChampions, 300).ConfigureAwait(false);
                                procked = true;
                            }
                            if (!procked && IntUtil.GetChance(5))
                            {
                                await Prediction_MAX_KDA(CurrentGame, currentGameID, "У кого будет самый высокий KDA", 300).ConfigureAwait(false);
                                procked = true;
                            }
                        }
                        IllSingleton.GetInstance().inAmatch = false;
                    }
                }
                lastErrorMessage = null;
            }
            catch (Exception ex)
            {
                IllSingleton.GetInstance().inAmatch = false;
                if (ex.InnerException != null)
                {
                    if (lastErrorMessage != ex.Message)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                            lastErrorMessage = ex.Message;
                        }
                    }
                }
                else
                {
                    if (lastErrorMessage != ex.Message)
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                            lastErrorMessage = ex.Message;
                        }
                    }
                }
            }

        }      
        static private async Task Prediction_WIN_LOOSE(string currentGameID, string Title, string blue, string red, int sec)
        {
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {                    
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    var Participant = RiotAPI.GetParticipantByMatch(onMatch);
                    if (Participant != null)
                    {
                        if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            IllSingleton.GetInstance().numGames++;
                            if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                            {
                                if (IllSingleton.GetInstance().autoPred)
                                {
                                    await TtvAPI.End_WinLoose_Prediction(true).ConfigureAwait(false);
                                    if (IllSingleton.GetInstance().debug)
                                        Log.WriteLog(null, $"Матч завершен {RiotAPI.GetParticipantByMatch(onMatch).Winner}");
                                }
                                IllSingleton.GetInstance().numWins++;
                                await UpdateDailyStats(true).ConfigureAwait(false);                                
                            }
                            if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                            {
                                if (IllSingleton.GetInstance().autoPred)
                                {
                                    await TtvAPI.End_WinLoose_Prediction(false).ConfigureAwait(false);
                                    if (IllSingleton.GetInstance().debug)
                                        Log.WriteLog(null, $"Матч завершен {RiotAPI.GetParticipantByMatch(onMatch).Winner}");
                                }
                                IllSingleton.GetInstance().numLoose++;
                                await UpdateDailyStats(false).ConfigureAwait(false);
                            }
                            IllCommands.SaveGameStats();
                        }
                        else
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                            await TtvAPI.CencelePrediction().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        IllSingleton.GetInstance().autoPred = false;
                        IllSingleton.GetInstance().inAmatch = false;
                        Log.WriteLog(null, "(Prediction_WIN_LOOSE) Критическая ошибка в методе GetOutcome(RioTtvAPI.Endpoints.MatchEndpoint.Participant), Participant не может быть null.");
                        TtvIRCClient.SendMessage("Критическая ошибка в методе GetOutCome(RiotAPI.Endpoints.MatchEndpoint.Participant), Participant не может быть null. Автоставки выключены");
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.                             
                        {
                            Log.WriteLog(ex, "Prediction_WIN_LOOSE_1");
                            IllSingleton.GetInstance().inAmatch = false;
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            Log.WriteLog(ex, "Prediction_WIN_LOOSE_2");
                            IllSingleton.GetInstance().inAmatch = false;
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_KDA(RiotSharp.Endpoints.SpectatorEndpoint.CurrentGame CurrentGame, string currentGameID, string Title, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in Participants)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);                
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_10_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
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
                            long vFlag = 0;
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }

                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "Prediction_MAX_KDA_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "Prediction_MAX_KDA_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_KDA_2(string currentGameID, string Title, string blue, string red, int sec)
        {
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        int TeamID = 0;
                        string getPosition = "";
                        foreach (var champGetData in FinParticipants)
                        {
                            if (champGetData.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
                            {
                                TeamID = champGetData.TeamId;
                                getPosition = champGetData.IndividualPosition;
                            }
                        }
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.TeamPosition == getPosition)
                            {
                                long vFlag = 0;
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_KDA_5(RiotSharp.Endpoints.SpectatorEndpoint.CurrentGame CurrentGame, string currentGameID, string Title, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            long teamid = 0;
            foreach (var champ in Participants)
            {
                if (champ.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
                {
                    teamid = champ.TeamId;
                }
            }
            RiotSharp.Endpoints.SpectatorEndpoint.CurrentGameParticipant[] teammates = new RiotSharp.Endpoints.SpectatorEndpoint.CurrentGameParticipant[5];
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
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {                    
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
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
                            long vFlag = 0;
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_FLAG_5(RiotSharp.Endpoints.SpectatorEndpoint.CurrentGame CurrentGame, string currentGameID, string Title, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            long teamid = 0;
            foreach (var champ in Participants)
            {
                if (champ.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
                {
                    teamid = champ.TeamId;
                }
            }
            RiotSharp.Endpoints.SpectatorEndpoint.CurrentGameParticipant[] teammates = new RiotSharp.Endpoints.SpectatorEndpoint.CurrentGameParticipant[5];
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
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        foreach (var champ in FinParticipants)
                        {
                            if (champ.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_FLAG_2(string currentGameID, string Title, string blue, string red, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int sec)
        {
            await TtvAPI.Start_2_Prediction(Title, blue, red, sec).ConfigureAwait(false);
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {                    
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            await UpdateDailyStats(true).ConfigureAwait(false);
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            await UpdateDailyStats(false).ConfigureAwait(false);
                        }
                        IllCommands.SaveGameStats();
                        List<PlayersObject> Players = new List<PlayersObject>();
                        var FinParticipants = onMatch.Info.Participants.ToArray();
                        int TeamID = 0;
                        string getPosition = "";
                        foreach (var champGetData in FinParticipants)
                        {
                            if (champGetData.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task Prediction_MAX_FLAG(RiotSharp.Endpoints.SpectatorEndpoint.CurrentGame CurrentGame, string currentGameID, string Title, Func<RiotSharp.Endpoints.MatchEndpoint.Participant, long> func, int windowSec)
        {
            var Participants = CurrentGame.Participants.ToArray();
            List<string> SelectedChamps = new List<string>();
            foreach (var Participant in Participants)
            {
                var ChampName = await RiotAPI.GetChampByIdAsync(Convert.ToInt32(Participant.ChampionId)).ConfigureAwait(false);
                SelectedChamps.Add(ChampName.Name);
            }
            await TtvAPI.Start_10_Prediction(SelectedChamps, Title, windowSec).ConfigureAwait(false);
            if (IllSingleton.GetInstance().debug)
                Log.WriteLog(null, "Ставка запущена");
            while (IllSingleton.GetInstance().inAmatch)
            {
                try
                {                    
                    var onMatch = await RiotAPI.GetMatchAsync(currentGameID).ConfigureAwait(false);
                    if (onMatch.Info.GameDuration.TotalMilliseconds > 300)
                    {
                        IllSingleton.GetInstance().inAmatch = false;
                        IllSingleton.GetInstance().numGames++;
                        if (RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numWins++;
                            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                            if (buffdata != null)
                            {
                                int bufflp = int.Parse(buffdata[1]);
                                if (buffdata[0] != IllSingleton.GetInstance().elo & buffdata[2] != "MASTER")
                                {
                                    IllSingleton.GetInstance().earnedLP += 100 - IllSingleton.GetInstance().startLP;
                                    IllSingleton.GetInstance().startLP = 0;
                                    IllSingleton.GetInstance().elo = buffdata[0];
                                }
                                IllSingleton.GetInstance().earnedLP += bufflp - IllSingleton.GetInstance().startLP;
                                IllSingleton.GetInstance().startLP = bufflp;
                            }
                        }
                        if (!RiotAPI.GetParticipantByMatch(onMatch).Winner)
                        {
                            IllSingleton.GetInstance().numLoose++;
                            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
                            if (buffdata != null)
                            {
                                int bufflp = int.Parse(buffdata[1]);
                                if (buffdata[0] != IllSingleton.GetInstance().elo & buffdata[2] != "MASTER")
                                {
                                    IllSingleton.GetInstance().startLP = 100;
                                    IllSingleton.GetInstance().elo = buffdata[0];
                                    IllSingleton.GetInstance().tier = buffdata[2];
                                }
                                IllSingleton.GetInstance().earnedLP -= IllSingleton.GetInstance().startLP - bufflp;
                                IllSingleton.GetInstance().startLP = bufflp;
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
                        IllSingleton.GetInstance().inAmatch = false;
                        TtvIRCClient.SendMessage("Матч отменен. Ставка будет отменена.");
                        await TtvAPI.CencelePrediction().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        if (!ex.InnerException.Message.Contains("Data not found")) //Ожидаемо. Мы еще не в игре.
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_1");
                        }
                    }
                    else
                    {
                        if (!ex.Message.Contains("Data not found"))
                        {
                            IllSingleton.GetInstance().inAmatch = false;
                            Log.WriteLog(ex, "GetCurrentMatchTask_2");
                        }
                    }
                }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }
        static private async Task CalculateGameStats(RiotSharp.Endpoints.SpectatorEndpoint.CurrentGame CurrentGame)
        {
            var champs = CurrentGame.Participants;
            int teamWr = 0;
            int enemyWr = 0;
            int teamElo = 0;
            int enemyElo = 0;
            long teamid = 0;
            foreach (var champ in champs)
            {
                if (champ.SummonerName.ToLower() == IllSingleton.GetInstance().SUMMONER_NAME.ToLower())
                {
                    teamid = champ.TeamId;
                }
            }

            foreach (var champ in champs)
            {
                int[] data = await GettInfo();
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
        static private async Task<int[]> GettInfo()
        {
            int[] data = new int[2];
            try
            {
                bool isRanked = false;
                string sRank = "0";
                var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
                foreach (var mType in rank)
                {
                    if (mType.QueueType == "RANKED_SOLO_5x5")
                    {
                        data[0] = mType.Wins * 100 / (mType.Wins + mType.Losses);
                        sRank = $"{mType.Tier} {mType.Rank}";
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
        private static async Task DisableRewardAsync()
        {
            if (!IllSingleton.GetInstance().wisEnabled) return;
            var reward = await TtvAPI.getReward(englishWis).ConfigureAwait(false);
            if (reward != null)
                if (reward[0] != "400" || reward[0] != "500")
                {
                    await TtvAPI.updateReward(reward[0], reward[1], int.Parse(reward[2]), reward[3], false, Convert.ToBoolean(reward[4])).ConfigureAwait(false);
                    IllSingleton.GetInstance().wisEnabled = false;
                }
                else
                    Log.WriteLog(null, $"DisableRewardAsync -> {reward[0]}");
            else
                Log.WriteLog(null, $"DisableRewardAsync -> null. Id: {englishWis}");
        }
        private static async Task EnableRewardAsync()
        {
            if (IllSingleton.GetInstance().wisEnabled) return;
            if (DateTimeOffset.Now.ToUnixTimeSeconds() - IllSingleton.GetInstance().WisCD >= 300)
            {
                var reward = await TtvAPI.getReward(englishWis).ConfigureAwait(false);
                if (reward[0] != "Error 404" && reward[0] != "Error 500")
                {
                    await TtvAPI.updateReward(reward[0], reward[1], int.Parse(reward[2]), reward[3], true, Convert.ToBoolean(reward[4])).ConfigureAwait(false);
                    IllSingleton.GetInstance().wisEnabled = true;
                }
            }
        }
        private static async Task UpdateDaylyStatsOld(bool won)
        {
            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            if (buffdata == null) return;
            var singleton = IllSingleton.GetInstance();
            if (won)
            {
                int bufflp = int.Parse(buffdata[1]);
                if (buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) || buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) || buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase))
                {
                    singleton.earnedLP += bufflp - singleton.startLP;
                    singleton.startLP = bufflp;
                }
                else
                {
                    if (buffdata[0] != singleton.elo)
                    {
                        singleton.earnedLP += 100 - singleton.startLP;
                        singleton.startLP = 0;
                        singleton.elo = buffdata[0];
                    }
                    singleton.earnedLP += bufflp - singleton.startLP;
                    singleton.startLP = bufflp;
                }
            }
            else
            {
                int bufflp = int.Parse(buffdata[1]);
                if (buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) || buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) || buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase))
                {
                    singleton.earnedLP += bufflp - singleton.startLP;
                    singleton.startLP = bufflp;
                }
                else 
                {
                    if (buffdata[0] != singleton.elo)
                    {
                        singleton.startLP = 100;
                        singleton.elo = buffdata[0];
                        singleton.tier = buffdata[2];
                    }
                    singleton.earnedLP -= singleton.startLP - bufflp;
                    singleton.startLP = bufflp;
                }
            }
        }
        private static async Task UpdateDailyStats(bool won)
        {
            var buffdata = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
            if (buffdata == null) return;
            var singleton = IllSingleton.GetInstance();
            int bufflp = int.Parse(buffdata[1]);
            if (buffdata[2].Equals("master", StringComparison.OrdinalIgnoreCase) || 
                buffdata[2].Equals("grandmaster", StringComparison.OrdinalIgnoreCase) || 
                buffdata[2].Equals("challenger", StringComparison.OrdinalIgnoreCase))
            {
                singleton.earnedLP += bufflp - singleton.startLP;
                singleton.startLP = bufflp;
            }
            else
            {
                if (buffdata[0] != singleton.elo)
                {
                    singleton.startLP = 100;
                    singleton.elo = buffdata[0];
                    singleton.tier = buffdata[2];
                    singleton.earnedLP += won ? 0 : singleton.startLP;
                }
                else
                {
                    singleton.earnedLP += won ? bufflp - singleton.startLP : singleton.startLP - bufflp;
                }
                singleton.startLP = bufflp;
            }
        }
    }
}