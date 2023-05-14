using System;
using System.Threading.Tasks;

using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using IllPubSub.Events;
using TwitchLib.Communication.Events;

using SkillzBot.WRITERS;
using SkillzBot.MYSQL;
using SkillzBot.Singleton;
using SkillzBot.IllSkillzBot;
using SkillzBot.IllSTRINGS;

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
                var clientOptions = new ClientOptions
                {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(35)
                };
                WebSocketClient customClient = new WebSocketClient(clientOptions);
                client = new TwitchClient(customClient);
                ConnectionCredentials credentials = new ConnectionCredentials(singleton.BotTwitchName, singleton.BotTwitchAuth);
                client.Initialize(credentials, singleton.ChannelName);
                client.OnMessageReceived += Client_OnMessageReceived;
                client.OnUserTimedout += Client_OnUserTimedout;
                client.OnDisconnected += Client_OnDisconnected;
                client.Connect();
                Console.WriteLine("OK.");
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR.");
                Log.WriteLog(e, "TtvIRCClient()");
            }
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
        public static void OnStreamDown()
        {
            singleton.BroadcasterIsOnline = false;
            IllGames.ClearQuizzActiveUsers();
            singleton.FirstQuizzOfTheDay = true;             
            if (singleton.earnedLP <= 0)
                SendMessage(STRINGS.OnStreadDownLowLP);
            else
                SendMessage(STRINGS.OnStreadDownHighLP);
        }
        public static void OnStreamUp()
        {
            singleton.BroadcasterIsOnline = true;
            SendMessage(string.Format(STRINGS.OnStreamUP, singleton.ChannelName));
        }                        
        public static void OnUnban(OnUnbanArgs e)
        {
            SendMessage(string.Format(STRINGS.OnUnban, e.UnbannedBy, e.UnbannedUser));
        }  
        public static void SendMessage(string messageToSend)
        {
            const int MaxLength = 500;
            if (messageToSend.Length <= MaxLength)
            {
                try
                {
                    client.SendMessage(singleton.ChannelName, messageToSend);
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
                            client.SendMessage(singleton.ChannelName, STRINGS.SendMessageERROR);
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
                    client.SendMessage(singleton.ChannelName, messagePart);
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