using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using TwitchLib.Client.Events;
using SkillzBot.API.Twitch;
using SkillzBot.MODELS;
using SkillzBot.MYSQL;
using SkillzBot.Singleton;
using SkillzBot.WRITERS;
using TwitchLib.Client.Models;
using F23.StringSimilarity;
using SkillzBot.IllSTRINGS;
using SkillzBot.IRC;
using SkillzBot.API.OpenAI;

namespace SkillzBot.IllSkillzBot
{
    internal class IllChatMessageHandler
    {
        private readonly static DataTable Messages = new DataTable("Messages");
        private readonly static List<MessageBuffer> messagesBuffer = new List<MessageBuffer>();
        private readonly static object _LockMessagesObject = new object();
        private readonly static object _LockBufferObject = new object();
        private static double ChatGPTCD = 0;
        static IllChatMessageHandler()
        {
            DataColumn idColumnMess = new DataColumn("Id", Type.GetType("System.Int32"))
            {
                Unique = true,
                AllowDBNull = false,
                AutoIncrement = true,
                AutoIncrementSeed = 0,
                AutoIncrementStep = 1
            };
            DataColumn nameMessColumn = new DataColumn("Name", Type.GetType("System.String"));
            DataColumn Message1 = new DataColumn("Message1", Type.GetType("System.String"));
            DataColumn Message2 = new DataColumn("Message2", Type.GetType("System.String"));
            DataColumn Message3 = new DataColumn("Message3", Type.GetType("System.String"));
            DataColumn Message4 = new DataColumn("Message4", Type.GetType("System.String"));
            DataColumn MessTime = new DataColumn("UvalTimer", Type.GetType("System.Double"));
            nameMessColumn.Unique = true;
            Message1.DefaultValue = "";
            Message2.DefaultValue = "";
            Message3.DefaultValue = "";
            Message4.DefaultValue = "";
            MessTime.DefaultValue = 0;
            Messages.Columns.Add(idColumnMess);
            Messages.Columns.Add(nameMessColumn);
            Messages.Columns.Add(Message1);
            Messages.Columns.Add(Message2);
            Messages.Columns.Add(Message3);
            Messages.Columns.Add(Message4);
            Messages.Columns.Add(MessTime);
            Messages.PrimaryKey = new DataColumn[] { Messages.Columns["Id"] };            
        }
        public static async Task<UserObject> MessageHandler(OnMessageReceivedArgs e)
        {
            SaveToBuffer(e);
            if (e.ChatMessage.Username.Equals("streamelements", StringComparison.OrdinalIgnoreCase)) return null;
            var user = await GetAddUser(e.ChatMessage).ConfigureAwait(false);
            AddMessage(e.ChatMessage.Username, e.ChatMessage.Message);
            user.messageCon++;
            await SaveBuffer(false).ConfigureAwait(false);
            if (IllChatFilters.ZapCheck(e.ChatMessage.Message, e.ChatMessage.DisplayName))
                return await IllCommands.IllBanUser(user).ConfigureAwait(false);
            await IllChatFilters.DeleteLinks(user, e).ConfigureAwait(false);
            if (IllChatFilters.CheckBooB(e.ChatMessage.Message))
                return await TtvAPI.TimeOutUser(user, 1200, STRINGS.TimeOutBadPic).ConfigureAwait(false);
            if (IllChatFilters.FilterASCII(e))
                return await TtvAPI.TimeOutUser(user, 600, STRINGS.TimeOutPic).ConfigureAwait(false);
            if (await CheckSpam(e.ChatMessage.Username, e.ChatMessage.Message))
                return await TtvAPI.TimeOutUser(user, 300, STRINGS.TimeOutSpam).ConfigureAwait(false);
            if (e.ChatMessage.Message.Contains("хохол", StringComparison.OrdinalIgnoreCase) || e.ChatMessage.Message.Contains("хахол", StringComparison.OrdinalIgnoreCase))
                return await TtvAPI.TimeOutUser(user, 600, STRINGS.TimeOut1wReason).ConfigureAwait(false);
            if (IllSingleton.GetInstance().QuizIsRunning)
                user = IllGames.UserGuessAnswer(user, e.ChatMessage.Message);
            else
                IllGames.QuizzActiveUser(user.TwitchID.ToString());
            if (e.ChatMessage.Message.StartsWith("!"))
                user = await IllCommandHandler.CommandHandler(user, e.ChatMessage.Message).ConfigureAwait(false);
            //if (!e.ChatMessage.Message.StartsWith("!") & !e.ChatMessage.Message.StartsWith("/"))
            //    TypeInChat(e.ChatMessage.Message);
            if (user.isMod != 1) return user;
            if (e.ChatMessage.Message.StartsWith("@bot_illskillz", StringComparison.OrdinalIgnoreCase))
            {                
                TtvIRCClient.SendMessage($"@{e.ChatMessage.DisplayName} {await IllCommands.GetGPTResponce(e.ChatMessage.DisplayName, e.ChatMessage.Message).ConfigureAwait(false)}");                
            }
            return user;
        }
        public static async Task SaveBuffer(bool IsForced)
        {
            if (messagesBuffer.Count < 100 && !IsForced) return;
            if (messagesBuffer.Count == 0) return;
            List<MessageBuffer> temp;
            lock (_LockBufferObject)
            {
                temp = new List<MessageBuffer>(messagesBuffer);
                messagesBuffer.Clear();
            }
            await MySQL.SaveMessages(temp).ConfigureAwait(false);
        }
        private static void SaveToBuffer(OnMessageReceivedArgs e)
        {
            lock (_LockBufferObject)
            {
                messagesBuffer.Add(new MessageBuffer()
                {
                    Message = e.ChatMessage.Message,
                    TtvID = e.ChatMessage.UserId,
                    Name = e.ChatMessage.Username,
                    TimeStamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString()
                });
            }
        }
        static void AddMessage(string Sender, string Message)
        {
            int id = FindUser2(Sender);
            if (id != -1)
            {
                lock (_LockMessagesObject)
                {
                    Messages.Rows[id][5] = Messages.Rows[id][4];
                    Messages.Rows[id][4] = Messages.Rows[id][3];
                    Messages.Rows[id][3] = Messages.Rows[id][2];
                    Messages.Rows[id][2] = Message;
                    Messages.Rows[id][6] = DateTimeOffset.Now.ToUnixTimeSeconds() - Convert.ToDouble(Messages.Rows[id][6]);
                    Messages.AcceptChanges();
                }
            }
            else
            {
                lock (_LockMessagesObject)
                {
                    DataRow NewRow = Messages.NewRow();
                    NewRow[1] = Sender;
                    NewRow[2] = Message;
                    NewRow[3] = "1";
                    NewRow[4] = "2";
                    NewRow[5] = "3";
                    NewRow[6] = DateTimeOffset.Now.ToUnixTimeSeconds();
                    Messages.Rows.Add(NewRow);
                    Messages.AcceptChanges();
                }
            }
        }
        static int FindUser2(string Name)
        {
            try
            {
                string expression = $"Name = '{Name.ToLower()}'";
                lock (_LockMessagesObject)
                {
                    var dRows = Messages.Select(expression);
                    if (dRows.Length > 1)
                    {
                        Log.WriteLog(null, $"findUser2() Douplicates detected - {Name}");
                        return -1;
                    }
                    if (dRows.Length == 0) return -1;                    
                    return Convert.ToInt32(dRows[0][0]);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "findUser2()");
            }
            return -1;
        }
        private static async Task<UserObject> GetAddUser(ChatMessage chatmessage)
        {
            if (int.TryParse(chatmessage.UserId, out int ttvid))
            {
                UserObject user = await MySQL.GetUser(ttvid).ConfigureAwait(false);
                if (user.dbID == -404)
                {
                    user.TwitchID = ttvid;
                    user.Name = chatmessage.Username;
                    user.isSub = Convert.ToInt32(chatmessage.IsSubscriber);
                    user.isVip = chatmessage.IsVip ? 1 : 0;
                    user.IsBroadcaster = chatmessage.IsBroadcaster ? 1 : 0;
                    user.isMod = chatmessage.IsModerator ? 1 : 0;
                    user.isPartner = chatmessage.IsPartner ? 1 : 0;
                    await MySQL.AddUser(user).ConfigureAwait(false);
                    return user;
                }
                else if (user.dbID == -500)
                {
                    Log.WriteLog(null, "GetUser Error 500! Duplicates???");
                    return user;
                }
                else if (user.dbID == -800)
                {
                    Log.WriteLog(null, "GetUser Error 800! ???????????????");
                    return user;
                }
                else
                {
                    user.Name = chatmessage.Username;
                    user.isSub = chatmessage.IsSubscriber ? 1 : 0;
                    user.isVip = chatmessage.IsVip ? 1 : 0;
                    user.IsBroadcaster = chatmessage.IsBroadcaster ? 1 : 0;
                    user.isMod = chatmessage.IsModerator ? 1 : 0;
                    user.isPartner = chatmessage.IsPartner ? 1 : 0;
                    return user;
                }
            }
            else
            {
                Log.WriteLog(null, "GetAddUser(): TtvID Conversion Error");
                return null;
            }
        }
        static async Task<bool> CheckSpam(string Sender, string Message)
        {
            var jw = new NormalizedLevenshtein();            
                int id = await Task.FromResult(FindUser2(Sender)).ConfigureAwait(false);
            lock (_LockMessagesObject)
            {
                var sim1 = (jw.Distance(Messages.Rows[id][2].ToString(), Messages.Rows[id][3].ToString()));
                if (sim1 < 0.4)
                {
                    var sim2 = (jw.Distance(Messages.Rows[id][3].ToString(), Messages.Rows[id][4].ToString()));
                    if (sim2 < 0.4)
                    {
                        if (Message.Length < 118)
                        {
                            var sim3 = (jw.Distance(Messages.Rows[id][4].ToString(), Messages.Rows[id][5].ToString()));
                            if (sim3 < 0.4)
                            {
                                if (Convert.ToDouble(Messages.Rows[id][6]) < 5)
                                {
                                    Messages.Rows[id][2] = "0";
                                    Messages.Rows[id][3] = "1";
                                    Messages.Rows[id][4] = "2";
                                    Messages.Rows[id][5] = "3";
                                    Messages.Rows[id][6] = DateTimeOffset.Now.ToUnixTimeSeconds();
                                    Messages.AcceptChanges();
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            if (Convert.ToDouble(Messages.Rows[id][6]) < 10)
                            {
                                Messages.Rows[id][2] = "0";
                                Messages.Rows[id][3] = "1";
                                Messages.Rows[id][4] = "2";
                                Messages.Rows[id][5] = "3";
                                Messages.Rows[id][6] = DateTimeOffset.Now.ToUnixTimeSeconds();
                                Messages.AcceptChanges();
                                return true;
                            }
                        }
                    }
                }
                Messages.Rows[id][6] = DateTimeOffset.Now.ToUnixTimeSeconds();
                Messages.AcceptChanges();
            }
            return false;
        }        
    }
}
