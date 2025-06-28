using System;
using System.Threading.Tasks;

using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Events;

using SkillzBot.WRITERS;
using SkillzBot.MYSQL;
using SkillzBot.Singleton;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;
using SkillzBot.API.StreamElements;
using SkillzBot.Discord;
using SkillzBot.API.Twitch;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using MySqlX.XDevAPI;

namespace SkillzBot.IRC
{
    sealed class TtvIRCClient
    {
        private static readonly TwitchClient client;
        private static readonly IllSingleton singleton = IllSingleton.GetInstance();
        static TtvIRCClient()
        {
            Console.Write("Initializing Ttv IRC Client... ");
            try
            {              
                ConnectionCredentials credentials = new ConnectionCredentials(singleton.BotTwitchName, singleton.BotTwitchAuth);
                client = new TwitchClient(); 
                client.OnMessageReceived += Client_OnMessageReceived;
                client.OnUserTimedout += Client_OnUserTimedout;
                client.OnDisconnected += Client_OnDisconnected;
                client.OnConnected += client_Onconnected;
                client.Initialize(credentials, singleton.ChannelName);                
                client.Connect();
                Console.WriteLine("OK.");
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR.");
                Log.WriteLog(e, "TtvIRCClient()");
            }
        }
        private static async void client_Onconnected(object sender, OnConnectedArgs e)
        {
            await Task.CompletedTask.ConfigureAwait(false);
        }
        private static async void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            var user = await IllChatMessageHandler.MessageHandler(e).ConfigureAwait(false);
            if (user != null)
                await MySQL.UpdateUser(user).ConfigureAwait(false);
        }                      
        private static async void Client_OnUserTimedout(object sender, OnUserTimedoutArgs e)
        {
            await UserTimedoutEventTask(e).ConfigureAwait(false);
            if (e.UserTimeout.TimeoutDuration > 50000)            
                SendMessage($"o7");            
        }
        private static void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            Log.WriteLog(null, "IRC client has been disconnected!");
        }
        private static async Task UserTimedoutEventTask(OnUserTimedoutArgs e)
        {
            try
            {
                var user = await MySQL.GetUser(e.UserTimeout.Username).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    Log.WriteLog(null, $"UserTimedoutEventTask id = -1 username:{e.UserTimeout.Username}");
                }
                else
                {
                    user.UvalTimer = e.UserTimeout.TimeoutDuration + DateTimeOffset.Now.ToUnixTimeSeconds();
                    user.UvalCon++;                    
                    await MySQL.UpdateUser(user).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "");
            }
        }  
        public static async Task OnStreamDown()
        {
            singleton.BroadcasterIsOnline = false;
            IllGames.ClearQuizzActiveUsers();
            var lastStats = IllCommands.GetLpAsync().GetAwaiter().GetResult();
            string msg = $"Cыграно {singleton.numGames} игр, из них побед {singleton.numWins} / поражений {singleton.numLoose}. Заработано {singleton.earnedLP} LP";
            singleton.FirstQuizzOfTheDay = true;
            if (singleton.earnedLP < 0)
            {
                SendMessage(STRINGS.OnStreadDownLowLP);
                await DiscordClient.SendEmbedMsg("С позором!", "", singleton.SUMMONER_NAME, lastStats.RANK, lastStats.LPoints, null, false, msg).ConfigureAwait(false);
            }
            else if (singleton.earnedLP > 0)
            {
                SendMessage(STRINGS.OnStreadDownHighLP);
                await DiscordClient.SendEmbedMsg("Героем!", "", singleton.SUMMONER_NAME, lastStats.RANK, lastStats.LPoints, null, false, msg).ConfigureAwait(false);
            }
            else
            {
                SendMessage("Стример офнул PoroSad");             
                await DiscordClient.SendEmbedMsg("", "", singleton.SUMMONER_NAME, lastStats.RANK, lastStats.LPoints, null, false, msg).ConfigureAwait(false);
            }
        }
        public static async Task OnStreamUp()
        {
            singleton.BroadcasterIsOnline = true;
            SendMessage(string.Format(STRINGS.OnStreamUP, singleton.ChannelName));
            var info = await TtvAPI.GetStreamInfo().ConfigureAwait(false);
            var lp = await IllCommands.GetLpAsync().ConfigureAwait(false);
            if (info != null)
                DiscordClient.SendEmbedMsg(info.Title, info.ThumbnailUrl, singleton.SUMMONER_NAME,lp.RANK,lp.LPoints).GetAwaiter().GetResult();
            else
            {
                var cInfo = await TtvAPI.GetChannelInformationAsync().ConfigureAwait(false);
                if (cInfo != null)
                    DiscordClient.SendEmbedMsg(cInfo.Title, null, singleton.SUMMONER_NAME, lp.RANK, lp.LPoints).GetAwaiter().GetResult();
            }
        }                        
        public static void OnUnban(ChannelUnbanArgs e)
        {            
            SendMessage(string.Format(STRINGS.OnUnban, e.Notification.Payload.Event.ModeratorUserLogin, e.Notification.Payload.Event.UserName));
        }  
        public static void SendMessage(string messageToSend)
        {
            if (singleton.IsSilent) return;            
            const int MaxLength = 500;
            if (messageToSend.Length <= MaxLength)
            {
                try
                {
                    StreamElementsAPI.SendChatMessage(messageToSend).GetAwaiter().GetResult();
                    //client.SendMessage(singleton.ChannelName, messageToSend);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "client.SendMessage");
                    return;
                }
                return;
            }
            int startIndex = 0;
            while (startIndex < messageToSend.Length)
            {
                int length = Math.Min(MaxLength, messageToSend.Length - startIndex);
                if (length == MaxLength && messageToSend[startIndex + length - 1] != ' ')
                {
                    int lastSpaceIndex = messageToSend.LastIndexOf(' ', startIndex + length - 1, length);
                    if (lastSpaceIndex != -1)
                    {
                        length = lastSpaceIndex - startIndex;
                    }
                    else
                    {
                        try
                        {
                            StreamElementsAPI.SendChatMessage(STRINGS.SendMessageERROR).GetAwaiter().GetResult();
                            //client.SendMessage(singleton.ChannelName, STRINGS.SendMessageERROR);
                            
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog(ex, "client.SendMessage");
                            return;
                        }
                        return;
                    }
                }
                string messagePart = messageToSend.Substring(startIndex, length);
                try
                {
                    StreamElementsAPI.SendChatMessage(messagePart).GetAwaiter().GetResult();
                    //client.SendMessage(singleton.ChannelName, messagePart);
                }
                catch (Exception ex)
                {
                    Log.WriteLog(ex, "client.SendMessage");
                    return;
                }
                startIndex += length;
            }
        }
    }
}