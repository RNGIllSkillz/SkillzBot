using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using SkillzBot.WRITERS;
using System.Threading.Tasks;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System.Linq;
using SkillzBot.Singleton;

namespace SkillzBot.MYSQL
{
    internal sealed class MySQL
    {
        private readonly static string _DbName = IllSingleton.GetInstance().ChannelName;
        private readonly static string _DbUserName = IllSingleton.GetInstance().MySQL_User;
        private readonly static string _DbPassword = IllSingleton.GetInstance().MySQL_password;
        public MySQL()
        {
            MySQLInitAsync().GetAwaiter().GetResult();
        }
        public async Task MySQLInitAsync()
        {
            Console.Write("Initializing MySQL... ");
            try
            {
                string iName = "`" + _DbName + "`";
                string SQL = $"CREATE DATABASE IF NOT EXISTS {iName} DEFAULT CHARACTER SET utf8mb4";
                using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbUserName, _DbPassword))
                {
                    await Connect.OpenAsync().ConfigureAwait(false);
                    using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                    await Command.ExecuteNonQueryAsync().ConfigureAwait(false);                    
                }

                SQL = $"CREATE TABLE IF NOT EXISTS dbUserTable ( dbID INTEGER NOT NULL AUTO_INCREMENT, TwitchID INTEGER NOT NULL UNIQUE, Name VARCHAR(30), " +
                "isSub INTEGER, isVip INTEGER, isMod INTEGER, IsBroadcaster INTEGER, UvalCon INTEGER, messageCon INTEGER, roulettCon INTEGER, roulettCD DOUBLE, UvalTimer DOUBLE, banCount INTEGER, Points DOUBLE, IsOnline INTEGER, QuizPoints INTEGER, QuizTotal INTEGER, IsPartner INTEGER, PRIMARY KEY(dbID))";
                using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword))
                {
                    await Connect.OpenAsync().ConfigureAwait(false);
                    using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                    await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                SQL = $"CREATE TABLE IF NOT EXISTS dbUserMessageTable ( dbID INTEGER NOT NULL AUTO_INCREMENT, TwitchID INTEGER NOT NULL, Name VARCHAR(30), Message VARCHAR(600), TimeStamp DOUBLE, PRIMARY KEY(dbID))";
                using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword))
                {
                    await Connect.OpenAsync().ConfigureAwait(false);
                    using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                    await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                SQL = $"CREATE TABLE IF NOT EXISTS dbQuiz ( dbID INTEGER NOT NULL AUTO_INCREMENT, Question VARCHAR(600), Answer VARCHAR(600), Prize INTEGER, PRIMARY KEY(dbID))";
                using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword))
                {
                    await Connect.OpenAsync().ConfigureAwait(false);
                    using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                    await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                SQL = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS WHERE table_schema = @Name AND table_name = 'dbUserTable' AND index_name = 'index_Name'";
                int i = 0;
                using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword))
                {
                    await Connect.OpenAsync();
                    using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                    Command.Parameters.AddWithValue("@Name", _DbName);
                    using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                        i = Convert.ToInt32(sqlReader[0]);
                }
                if (i == 0)
                {
                    SQL = $"CREATE UNIQUE INDEX index_Name ON dbUserTable (Name)";
                    using (MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword))
                    {
                        await Connect.OpenAsync();
                        using MySqlCommand Command = new MySqlCommand(SQL, Connect);                        
                        await Command.ExecuteNonQueryAsync().ConfigureAwait(false);                       
                    }
                }
                Console.WriteLine("OK.");
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR.");
                Log.WriteLog(e, "CreateTable()");
            }
        }

        public static async Task AddUser(UserObject User)
        {

            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string commandText = "INSERT INTO dbUserTable (TwitchID, Name, isSub, isVip, isMod, IsBroadcaster, UvalCon, messageCon, roulettCon, roulettCD, UvalTimer, banCount, Points, IsOnline, QuizPoints, QuizTotal, IsPartner) " +
                                 "VALUES(@TwitchID, @Name, @isSub, @isVip, @UvalCon, @isMod, @IsBroadcaster, @messageCon, @roulettCon, @roulettCD, @UvalTimer, @banCount, @Points, @IsOnline, @QuizPoints, @QuizTotal, @IsPartner)";
            using MySqlCommand Command = new MySqlCommand(commandText, Connect);
            Command.Parameters.AddWithValue("@TwitchID", User.TwitchID);
            Command.Parameters.AddWithValue("@Name", User.Name);
            Command.Parameters.AddWithValue("@isSub", User.isSub);
            Command.Parameters.AddWithValue("@isVip", User.isVip);
            Command.Parameters.AddWithValue("@isMod", User.isMod);
            Command.Parameters.AddWithValue("@IsBroadcaster", User.IsBroadcaster);
            Command.Parameters.AddWithValue("@UvalCon", User.UvalCon);
            Command.Parameters.AddWithValue("@messageCon", User.messageCon);
            Command.Parameters.AddWithValue("@roulettCon", User.roulettCon);
            Command.Parameters.AddWithValue("@roulettCD", User.roulettCD);
            Command.Parameters.AddWithValue("@UvalTimer", User.UvalTimer);
            Command.Parameters.AddWithValue("@banCount", User.banCount);
            Command.Parameters.AddWithValue("@Points", User.Points);
            Command.Parameters.AddWithValue("@IsOnline", User.IsOnline);
            Command.Parameters.AddWithValue("@QuizPoints", User.QuizPoints);
            Command.Parameters.AddWithValue("@QuizTotal", User.QuizTotal);
            Command.Parameters.AddWithValue("@IsPartner", User.isPartner);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, $"Dupe ADD Exception? TwitchID = {User.TwitchID}, Name = {User.Name}");
            }
        }
        public static async Task SaveMessage(int TwitchID, string Name, string Message, double Timestamp)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string commandText = $"INSERT INTO dbUserMessageTable (TwitchID, Name, Message, TimeStamp) " +
                                 "VALUES(@TwitchID, @Name, @Message, @TimeStamp)";
            using MySqlCommand Command = new MySqlCommand(commandText, Connect);
            Command.Parameters.AddWithValue("@TwitchID", TwitchID);
            Command.Parameters.AddWithValue("@Name", Name);
            Command.Parameters.AddWithValue("@Message", Message);
            Command.Parameters.AddWithValue("@TimeStamp", Timestamp);
            await Connect.OpenAsync().ConfigureAwait(false);
            await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        public static async Task SaveMessages(List<MessageBuffer> Messages)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string commandText = $"INSERT INTO dbUserMessageTable (TwitchID, Name, Message, TimeStamp) " +
                                 "VALUES(@TwitchID, @Name, @Message, @TimeStamp)";
            using MySqlCommand Command = new MySqlCommand(commandText, Connect);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                using var con = await Connect.BeginTransactionAsync().ConfigureAwait(false);
                var IDParam = Command.Parameters.Add("TwitchID", MySqlDbType.Int32);
                var nameParam = Command.Parameters.Add("Name", MySqlDbType.VarChar);
                var MessageParam = Command.Parameters.Add("Message", MySqlDbType.VarChar);
                var TimeParam = Command.Parameters.Add("TimeStamp", MySqlDbType.Double);
                for (int i = 0; i < Messages.Count; i++)
                {
                    IDParam.Value = int.Parse(Messages[i].TtvID);
                    nameParam.Value = Messages[i].Name;
                    MessageParam.Value = Messages[i].Message;
                    TimeParam.Value = Convert.ToDouble(Messages[i].TimeStamp);
                    await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                await con.CommitAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.WriteLog(e, $"SaveMessages()");
            }
        }
        public static async Task UpdateUser(UserObject User)
        {
            string SQL = "UPDATE dbUserTable SET Name = @Name, isSub = @isSub, isVip = @isVip, isMod = @isMod, IsBroadcaster = @IsBroadcaster, UvalCon = @UvalCon, messageCon = @messageCon, roulettCon = @roulettCon, roulettCD = @roulettCD, UvalTimer = @UvalTimer, banCount = @banCount, QuizPoints = @QuizPoints, QuizTotal = @QuizTotal, IsPartner = @IsPartner  WHERE TwitchID = @TwitchID";
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);            
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Name", User.Name);
            Command.Parameters.AddWithValue("@isSub", User.isSub);
            Command.Parameters.AddWithValue("@isVip", User.isVip);
            Command.Parameters.AddWithValue("@isMod", User.isMod);
            Command.Parameters.AddWithValue("@IsBroadcaster", User.IsBroadcaster);
            Command.Parameters.AddWithValue("@UvalCon", User.UvalCon);
            Command.Parameters.AddWithValue("@messageCon", User.messageCon);
            Command.Parameters.AddWithValue("@roulettCon", User.roulettCon);
            Command.Parameters.AddWithValue("@roulettCD", User.roulettCD);
            Command.Parameters.AddWithValue("@UvalTimer", User.UvalTimer);
            Command.Parameters.AddWithValue("@banCount", User.banCount);
            Command.Parameters.AddWithValue("@TwitchID", User.TwitchID);
            Command.Parameters.AddWithValue("@QuizPoints", User.QuizPoints);
            Command.Parameters.AddWithValue("@QuizTotal", User.QuizTotal);
            Command.Parameters.AddWithValue("@IsPartner", User.isPartner);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.WriteLog(e, $"UpdateUser()");
            }
        }
        public static async Task<UserObject> GetUser(int ttvUserID)
        {
            string SQL = "SELECT * FROM dbUserTable WHERE TwitchID = @ID";
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                Command.Parameters.AddWithValue("@ID", ttvUserID);
                using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                if (await sqlReader.ReadAsync().ConfigureAwait(false))
                {
                    return new UserObject()
                    {
                        dbID = await sqlReader.GetFieldValueAsync<int>(0).ConfigureAwait(false),
                        TwitchID = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false),
                        Name = await sqlReader.GetFieldValueAsync<string>(2).ConfigureAwait(false),
                        isSub = await sqlReader.GetFieldValueAsync<int>(3).ConfigureAwait(false),
                        isVip = await sqlReader.GetFieldValueAsync<int>(4).ConfigureAwait(false),
                        isMod = await sqlReader.GetFieldValueAsync<int>(5).ConfigureAwait(false),
                        IsBroadcaster = await sqlReader.GetFieldValueAsync<int>(6).ConfigureAwait(false),
                        UvalCon = await sqlReader.GetFieldValueAsync<int>(7).ConfigureAwait(false),
                        messageCon = await sqlReader.GetFieldValueAsync<int>(8).ConfigureAwait(false),
                        roulettCon = await sqlReader.GetFieldValueAsync<int>(9).ConfigureAwait(false),
                        roulettCD = await sqlReader.GetFieldValueAsync<double>(10).ConfigureAwait(false),
                        UvalTimer = await sqlReader.GetFieldValueAsync<double>(11).ConfigureAwait(false),
                        banCount = await sqlReader.GetFieldValueAsync<int>(12).ConfigureAwait(false),
                        Points = await sqlReader.GetFieldValueAsync<int>(13).ConfigureAwait(false),
                        IsOnline = await sqlReader.GetFieldValueAsync<int>(14).ConfigureAwait(false),
                        QuizPoints = await sqlReader.GetFieldValueAsync<int>(15).ConfigureAwait(false),
                        QuizTotal = await sqlReader.GetFieldValueAsync<int>(16).ConfigureAwait(false),
                        isPartner = await sqlReader.GetFieldValueAsync<int>(17).ConfigureAwait(false)
                    };
                }
                else
                {
                    return new UserObject()
                    {
                        dbID = -404
                    };
                }
            }
            catch (Exception e)
            {
                Log.WriteLog(e, $"GetUser(ID)");
                return new UserObject()
                {
                    dbID = -404
                };
            }
        }
        public static async Task<UserObject> GetUser(string name)
        {
            if (string.IsNullOrEmpty(name))
                return new UserObject { dbID = -404 }; // Early exit

            await using MySqlConnection connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string sql = "SELECT * FROM dbUserTable WHERE Name = @Name COLLATE utf8mb4_unicode_ci";
            await using MySqlCommand command = new MySqlCommand(sql, connect);
            command.Parameters.AddWithValue("@Name", name);
            try
            {
                await connect.OpenAsync().ConfigureAwait(false);
                await using var sqlReader = await command.ExecuteReaderAsync().ConfigureAwait(false);

                if (!await sqlReader.ReadAsync().ConfigureAwait(false))
                    return new UserObject { dbID = -404 };

                return new UserObject()
                {
                    dbID = await sqlReader.GetFieldValueAsync<int>(0).ConfigureAwait(false),
                    TwitchID = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false),
                    Name = await sqlReader.GetFieldValueAsync<string>(2).ConfigureAwait(false),
                    isSub = await sqlReader.GetFieldValueAsync<int>(3).ConfigureAwait(false),
                    isVip = await sqlReader.GetFieldValueAsync<int>(4).ConfigureAwait(false),
                    isMod = await sqlReader.GetFieldValueAsync<int>(5).ConfigureAwait(false),
                    IsBroadcaster = await sqlReader.GetFieldValueAsync<int>(6).ConfigureAwait(false),
                    UvalCon = await sqlReader.GetFieldValueAsync<int>(7).ConfigureAwait(false),
                    messageCon = await sqlReader.GetFieldValueAsync<int>(8).ConfigureAwait(false),
                    roulettCon = await sqlReader.GetFieldValueAsync<int>(9).ConfigureAwait(false),
                    roulettCD = await sqlReader.GetFieldValueAsync<double>(10).ConfigureAwait(false),
                    UvalTimer = await sqlReader.GetFieldValueAsync<double>(11).ConfigureAwait(false),
                    banCount = await sqlReader.GetFieldValueAsync<int>(12).ConfigureAwait(false),
                    Points = await sqlReader.GetFieldValueAsync<int>(13).ConfigureAwait(false),
                    IsOnline = await sqlReader.GetFieldValueAsync<int>(14).ConfigureAwait(false),
                    QuizPoints = await sqlReader.GetFieldValueAsync<int>(15).ConfigureAwait(false),
                    QuizTotal = await sqlReader.GetFieldValueAsync<int>(16).ConfigureAwait(false),
                    isPartner = await sqlReader.GetFieldValueAsync<int>(17).ConfigureAwait(false)
                };                
            }
            catch (Exception e)
            {
                Log.WriteLog(e, $"GetUser failed for name: '{name}'");
                return new UserObject() {dbID = -404};
            }
        }        
        public static async Task<List<UserObject>> TOP(string Flag)
        {
            string SQL;
            switch (Flag)
            {
                case "rtop":
                    Flag = "roulettCon";
                    SQL = "SELECT Name, roulettCon FROM dbUserTable ORDER BY roulettCon DESC LIMIT 3";
                    break;
                case "top":
                    Flag = "messageCon";
                    SQL = "SELECT Name, messageCon FROM dbUserTable ORDER BY messageCon DESC LIMIT 3";
                    break;
                default:
                    Log.WriteLog(null,"invalid column name");
                    return null;
            }
            List<UserObject> Users = new List<UserObject>();            
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                int i = 1;
                while (i <= 3 && await sqlReader.ReadAsync().ConfigureAwait(false))
                {
                    i++;
                    UserObject userObject = new UserObject()
                    {
                        Name = await sqlReader.GetFieldValueAsync<string>(0).ConfigureAwait(false)
                    };
                    switch (Flag)
                    {
                        case "messageCon":
                            userObject.messageCon = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false);
                            break;
                        case "roulettCon":
                            userObject.roulettCon = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false);
                            break;
                        default:
                            Log.WriteLog(null, "invalid column name");
                            return null;
                    }
                    Users.Add(userObject);
                }
                return Users;
            }
            catch (Exception e)
            {
                Log.WriteLog(e, $"TOP({Flag})");
                return null;
            }
        }
        //Get user position current/all
        public static async Task<int[]> GetTopPos(string UserName, string ColumnName)
        {
            int[] userPos = new int[2];
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);

            string SQL = $"SELECT {ColumnName} FROM dbUserTable WHERE Name = @UserName";
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                {
                    Command.Parameters.AddWithValue("@UserName", UserName);
                    using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                    int value = 0;
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                        value = Convert.ToInt32(sqlReader[0]);
                    if (value < 1)
                    {
                        userPos[0] = 0;
                        userPos[1] = 0;
                        return userPos;
                    }
                }

                SQL = $"SELECT COUNT(*) FROM dbUserTable WHERE {ColumnName} >= (SELECT {ColumnName} FROM dbUserTable WHERE Name = @UserName)";
                using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                {
                    Command.Parameters.AddWithValue("@UserName", UserName);
                    using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                        userPos[0] = Convert.ToInt32(sqlReader[0]);
                }

                SQL = $"SELECT COUNT(*) FROM dbUserTable WHERE {ColumnName} > 0";
                using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                {
                    using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                        userPos[1] = Convert.ToInt32(sqlReader[0]);
                }
                return userPos;
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "GetTopPos");
                return null;
            }
        }
        public static async Task<int> DeleteUser(string UserName)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string SQL = @"DELETE FROM [dbUserTable] WHERE [Name] = @Name";
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Name", UserName);
            await Connect.OpenAsync().ConfigureAwait(false);
            var i = await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
            return i;
        }
        public static async Task<List<UserObject>> SudoSQLReader(string SQL)
        {
            List<UserObject> Users = new List<UserObject>();
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            await Connect.OpenAsync().ConfigureAwait(false);
            using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await sqlReader.ReadAsync().ConfigureAwait(false))
            {
                if (sqlReader.FieldCount > 1)
                {
                    Users.Add(new UserObject()
                    {
                        dbID = await sqlReader.GetFieldValueAsync<int>(0).ConfigureAwait(false),
                        TwitchID = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false),
                        Name = await sqlReader.GetFieldValueAsync<string>(2).ConfigureAwait(false),
                        isSub = await sqlReader.GetFieldValueAsync<int>(3).ConfigureAwait(false),
                        isVip = await sqlReader.GetFieldValueAsync<int>(4).ConfigureAwait(false),
                        isMod = await sqlReader.GetFieldValueAsync<int>(5).ConfigureAwait(false),
                        IsBroadcaster = await sqlReader.GetFieldValueAsync<int>(6).ConfigureAwait(false),
                        UvalCon = await sqlReader.GetFieldValueAsync<int>(7).ConfigureAwait(false),
                        messageCon = await sqlReader.GetFieldValueAsync<int>(8).ConfigureAwait(false),
                        roulettCon = await sqlReader.GetFieldValueAsync<int>(9).ConfigureAwait(false),
                        roulettCD = await sqlReader.GetFieldValueAsync<double>(10).ConfigureAwait(false),
                        UvalTimer = await sqlReader.GetFieldValueAsync<double>(11).ConfigureAwait(false),
                        banCount = await sqlReader.GetFieldValueAsync<int>(12).ConfigureAwait(false),
                        Points = await sqlReader.GetFieldValueAsync<int>(13).ConfigureAwait(false),
                        IsOnline = await sqlReader.GetFieldValueAsync<int>(14).ConfigureAwait(false)
                    });
                }
                else
                {
                    Users.Add(new UserObject()
                    {
                        dbID = await sqlReader.GetFieldValueAsync<int>(0).ConfigureAwait(false)
                    });
                }
            }
            await Connect.CloseAsync().ConfigureAwait(false);
            return Users;
        }
        public static async Task<int> SudoSQLNonQuery(string SQL)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            await Connect.OpenAsync().ConfigureAwait(false);
            var output = await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
            return output;
        }
        public static async Task AddPoints(int Amount)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string SQL = $"UPDATE dbUserTable SET Points = Points + @Amount WHERE IsOnline = 1";
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Amount", Amount);
            await Connect.OpenAsync().ConfigureAwait(false);
            await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task AddPoints(int Amount, int TwitchID)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string SQL = $"UPDATE dbUserTable SET Points = Points + @Amount WHERE TwitchID = @TwitchID";
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Amount", Amount);
            Command.Parameters.AddWithValue("@TwitchID", TwitchID);
            await Connect.OpenAsync().ConfigureAwait(false);
            await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task<QuizzObject> GetQuiz(int ID)
        {
            QuizzObject Quiz = new QuizzObject();
            string SQL = $"SELECT * FROM dbQuiz WHERE dbID = @ID";
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            await Connect.OpenAsync().ConfigureAwait(false);
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@ID", ID);
            using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await sqlReader.ReadAsync().ConfigureAwait(false))
            {
                Quiz.QuizzQuestion = sqlReader[1].ToString();
                Quiz.QuizzAnswer = sqlReader[2].ToString();
                Quiz.QuizzCost = Convert.ToInt32(sqlReader[3]);
            }
            await Connect.CloseAsync().ConfigureAwait(false);
            return Quiz;        
        }
        public static async Task AddQuizPoints(int Amount, int TwitchID)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string SQL = $"UPDATE dbUserTable SET QuizPoints = QuizPoints + @Amount, QuizTotal = QuizTotal + @Amount WHERE TwitchID = @TwitchID";
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Amount", Amount);
            Command.Parameters.AddWithValue("@TwitchID", TwitchID);
            await Connect.OpenAsync().ConfigureAwait(false);
            await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task SpendQuizPoints(int Amount, int TwitchID)
        {
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            string SQL = $"UPDATE dbUserTable SET QuizPoints = QuizPoints - @Amount, WHERE TwitchID = @TwitchID";
            using MySqlCommand Command = new MySqlCommand(SQL, Connect);
            Command.Parameters.AddWithValue("@Amount", Amount);
            Command.Parameters.AddWithValue("@TwitchID", TwitchID);
            await Connect.OpenAsync().ConfigureAwait(false);
            await Command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await Connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task UpdateOnlineStatus(List<string> chatters)
        {
            using var connection = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            await connection.OpenAsync().ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                using var command = new MySqlCommand(null, connection, transaction);
                command.CommandText = "DROP TEMPORARY TABLE IF EXISTS params";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                command.CommandText = $"CREATE TEMPORARY TABLE params (Name VARCHAR(30))";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                command.CommandText = $"INSERT INTO params (Name) VALUES (@name)";
                var nameParam = command.Parameters.Add("@name", MySqlDbType.VarChar);
                foreach (var chatter in chatters)
                {
                    nameParam.Value = chatter;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                command.CommandText = $"CREATE UNIQUE INDEX index_Name ON params (Name)";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                command.CommandText = $"UPDATE dbUserTable SET IsOnline = IF(Name IN (SELECT Name FROM params), 1, 0)";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                await transaction.CommitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                throw ex;
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        public static async Task UpdateOnlineStatusOld(List<string> Chatters)
        {
            using var connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            await connect.OpenAsync().ConfigureAwait(false);

            string drop = $"DROP TEMPORARY TABLE IF EXISTS params";
            using (var dropCmd = new MySqlCommand(drop, connect))
            {
                await dropCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var createSql = $"CREATE TEMPORARY TABLE params (Name VARCHAR(30))";
            using (var createCmd = new MySqlCommand(createSql, connect))
            {
                await createCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var insertSql = $"INSERT INTO params (Name) values (@name)";
            using (var insertCmd = new MySqlCommand(insertSql, connect))
            {
                var nameParam = insertCmd.Parameters.Add("name", MySqlDbType.VarChar);
                using var tran = await connect.BeginTransactionAsync().ConfigureAwait(false);
                foreach (var chatter in Chatters)
                {
                    nameParam.Value = chatter;
                    await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                await tran.CommitAsync().ConfigureAwait(false);
            }

            var createIndex = $"CREATE UNIQUE INDEX index_Name ON params (Name)";
            using (var createIndexCmd = new MySqlCommand(createIndex, connect))
            {
                await createIndexCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var updateSql = $"UPDATE dbUserTable SET IsOnline = 1 WHERE Name IN (SELECT Name FROM params)";
            using (var updateCmd = new MySqlCommand(updateSql, connect))
            {
                await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            updateSql = $"UPDATE dbUserTable SET IsOnline = 0 WHERE Name NOT IN (SELECT Name FROM params)";
            using (var updateCmd = new MySqlCommand(updateSql, connect))
            {
                await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task UpdateOnlineStatus2(List<string> Chatters)
        {
            List<string> getNames = new List<string>();
            List<string> IsOnline = new List<string>();
            List<string> IsOffline = new List<string>();
            using var connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            await connect.OpenAsync().ConfigureAwait(false);

            Console.WriteLine("Forming Lists");

            string getCmd = $"SELECT Name FROM dbUserTable";
            using (MySqlCommand Command = new MySqlCommand(getCmd, connect))
            {
                using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await sqlReader.ReadAsync().ConfigureAwait(false))
                {
                    getNames.Add(sqlReader[0].ToString());
                }                
                bool gotem;
                foreach (var name in getNames)
                {
                    gotem = false;
                    foreach (var chatter in Chatters)
                    {
                        if (name == chatter)
                        {
                            IsOnline.Add(name);
                            gotem = true;
                        }
                    }
                    if (!gotem)
                    {
                        IsOffline.Add(name);
                    }
                }
            }

            Console.WriteLine("BEGIN IsOnline = 1");

            var UpdateIsOnline = $"UPDATE dbUserTable SET IsOnline = 1 WHERE Name = @onlineName";
            using (var insertCmd = new MySqlCommand(UpdateIsOnline, connect))
            {
                var nameParam = insertCmd.Parameters.Add("onlineName", MySqlDbType.VarChar);
                using var tran = await connect.BeginTransactionAsync().ConfigureAwait(false);
                foreach (string onlineName in IsOnline)
                {
                    nameParam.Value = onlineName;
                    await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                await tran.CommitAsync().ConfigureAwait(false);
            }

            Console.WriteLine("BEGIN IsOnline = 0");

            var UpdateIsOffline = $"UPDATE dbUserTable SET IsOnline = 0 WHERE Name = @offlineName";
            using (var insertCmd = new MySqlCommand(UpdateIsOnline, connect))
            {
                var nameParam = insertCmd.Parameters.Add("offlineName", MySqlDbType.VarChar);
                using var tran = await connect.BeginTransactionAsync().ConfigureAwait(false);
                foreach (string offlineName in IsOffline)
                {
                    nameParam.Value = offlineName;
                    await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                await tran.CommitAsync().ConfigureAwait(false);
            }
            Console.WriteLine("END");
            await connect.CloseAsync().ConfigureAwait(false);
        }
        public static async Task<TrackUser> TrackUser_new(string UserName)
        {
            TrackUser info = new TrackUser();
            List<string> AllSchemas = new List<string>();
            try
            {
                using MySqlConnection Connect = DBUtils.GetDBConnection(_DbUserName, _DbPassword);
                await Connect.OpenAsync().ConfigureAwait(false);

                string sql = "SELECT DISTINCT(TABLE_SCHEMA) FROM information_schema.TABLES WHERE TABLE_NAME = 'dbUserTable'";
                using MySqlCommand command = new MySqlCommand(sql, Connect);
                using (var sqlReader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                    {
                        AllSchemas.Add(sqlReader[0].ToString());
                    }
                }
                ExtractMessage extractMessage = new ExtractMessage();
                List<TrackedMessages> Messages = new List<TrackedMessages>();
                foreach (string schema in AllSchemas)
                {
                    sql = $"SELECT * FROM {schema}.dbUserMessageTable WHERE Name = @UserName ORDER BY TimeStamp";
                    using MySqlCommand Command = new MySqlCommand(sql, Connect);
                    Command.Parameters.AddWithValue("@UserName", UserName);
                    using (var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await sqlReader.ReadAsync().ConfigureAwait(false))
                        {
                            Messages.Add(new TrackedMessages()
                            {
                                TimeStamp = Convert.ToInt64(sqlReader[4]),
                                ChannelName = schema,
                                Message = sqlReader[3].ToString()
                            });
                        }
                    }
                }

                foreach (var message in Messages.OrderBy(m => m.TimeStamp))
                {
                    extractMessage.ExtractMessageTask(UnixTimeStampToDateTime(message.TimeStamp) + ", Channel: " + message.ChannelName + ", Message: " + message.Message);
                }

                info.Count = AllSchemas.Count;
                info.DBName = AllSchemas;
                return info;
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "TrackUser()");
                return null;
            }
        }
        public static async Task<TrackUser> TrackUser(string UserName)
        {
            TrackUser info = new TrackUser();
            List<string> AllSchemas = new List<string>();
            try
            {
                List<UserObject> Users = new List<UserObject>();
                using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
                await Connect.OpenAsync().ConfigureAwait(false);
                string SQL = $"show databases";
                info.Count = 0;
                info.DBName = new List<string>();
                using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                {                    
                    using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await sqlReader.ReadAsync().ConfigureAwait(false))
                    {
                        if (sqlReader[0].ToString() != "information_schema")
                            if (sqlReader[0].ToString() != "mysql")
                                if (sqlReader[0].ToString() != "performance_schema")
                                    if (sqlReader[0].ToString() != "sys")
                                        AllSchemas.Add(sqlReader[0].ToString());
                    }
                }

                foreach (string schema in AllSchemas)
                {
                    SQL = $"SELECT COUNT(*) FROM {schema}.dbUserTable WHERE Name = @UserName";                    
                    using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                    {
                        Command.Parameters.AddWithValue("@UserName", UserName);
                        using (var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            while (await sqlReader.ReadAsync().ConfigureAwait(false))
                            {                               
                                if (Convert.ToInt32(sqlReader[0]) != 0)
                                {
                                    info.Count++;
                                    info.DBName.Add(schema);
                                    break;
                                }
                            }
                        }
                    }
                }
                ExtractMessage extractMessage = new ExtractMessage();
                List<TrackedMessages> Messages = new List<TrackedMessages>();
                foreach (string cheman in info.DBName)
                {
                    SQL = $"SELECT * FROM {cheman}.dbUserMessageTable WHERE Name = @UserName";
                    
                    using (MySqlCommand Command = new MySqlCommand(SQL, Connect))
                    {
                        Command.Parameters.AddWithValue("@UserName", UserName);
                        using (var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            while (await sqlReader.ReadAsync().ConfigureAwait(false))
                            {
                                Messages.Add(new TrackedMessages()
                                {
                                    TimeStamp = Convert.ToInt64(sqlReader[4]),
                                    ChannelName = cheman,
                                    Message = sqlReader[3].ToString()
                                    //var date = UnixTimeStampToDateTime(Convert.ToDouble(sqlReader[4]));
                                    //await extractMessage.ExtractMessageTask(date + " Channel : " + cheman + " Message : " + sqlReader[3].ToString()).ConfigureAwait(false);                                  
                                });
                            }
                        }
                    }
                }

                QuickSort qs = new QuickSort();
                Messages = qs.SortArray(Messages, 0, Messages.Count-1);
                foreach (var message in Messages)
                {
                    extractMessage.ExtractMessageTask(UnixTimeStampToDateTime(message.TimeStamp) + ", Channel: " + message.ChannelName + ", Message: " + message.Message);
                }
                return info;                
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "TrackUser()");
                return null;
            }
        }
        /*
        public static async Task<List<UserMessage>> GetUserMEssages()
        {
            List<UserMessage> userMessages = new List<UserMessage>();
            var SQL = "SELECT * FROM dbUserMessageTable";
            using MySqlConnection Connect = DBUtils.GetDBConnection(_DbName, _DbUserName, _DbPassword);
            try
            {
                await Connect.OpenAsync().ConfigureAwait(false);
                using MySqlCommand Command = new MySqlCommand(SQL, Connect);
                using var sqlReader = await Command.ExecuteReaderAsync().ConfigureAwait(false);
                if (await sqlReader.ReadAsync().ConfigureAwait(false))
                {
                    userMessages.Add(new UserMessage()
                    {
                        DbID = await sqlReader.GetFieldValueAsync<int>(0).ConfigureAwait(false),
                        TwitchID = await sqlReader.GetFieldValueAsync<int>(1).ConfigureAwait(false),
                        Name = await sqlReader.GetFieldValueAsync<string>(2).ConfigureAwait(false),
                        Message = await sqlReader.GetFieldValueAsync<string>(3).ConfigureAwait(false),
                        TimeStamp = await sqlReader.GetFieldValueAsync<double>(4).ConfigureAwait(false)
                    });
                }
                return userMessages;
            }
            catch (Exception e)
            {
                Log.WriteLog(e, "GetUserMEssages");
                return null;
            }

        }        
         */   
        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }        
    } 
}