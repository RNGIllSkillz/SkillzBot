using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using SkillzBot.WRITERS;
using System.Threading.Tasks;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using System.Linq;
using SkillzBot.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillzBot.MySQL;
using System.Data;

namespace SkillzBot.MYSQL
{
    public sealed class MySqlDatabaseService : IDatabaseService, IDisposable
    {
        private readonly DatabaseConfiguration _config;
        private readonly ILogger<MySqlDatabaseService> _logger;
        private readonly string _connectionString;
        private bool _isInitialized = false;
        private bool _disposed = false;

        public MySqlDatabaseService(IOptions<DatabaseConfiguration> config, ILogger<MySqlDatabaseService> logger)
        {
            _config = config.Value ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = BuildConnectionString();
        }

        private string BuildConnectionString()
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = _config.Host,
                Port = (uint)_config.Port,
                Database = _config.DatabaseName,
                UserID = _config.Username,
                Password = _config.Password,
                CharacterSet = _config.CharacterSet,
                ConnectionTimeout = (uint)_config.ConnectionTimeout,
                DefaultCommandTimeout = (uint)_config.CommandTimeout,
                Pooling = _config.Pooling,
                MaximumPoolSize = (uint)_config.MaxPoolSize,
                MinimumPoolSize = (uint)_config.MinPoolSize,
                ConvertZeroDateTime = true,
                AllowZeroDateTime = true,
                SslMode = MySqlSslMode.Preferred
            };

            return builder.ConnectionString;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                _logger.LogInformation("Database already initialized");
                return;
            }

            _logger.LogInformation("Initializing MySQL database...");

            try
            {
                await CreateDatabaseIfNotExistsAsync();
                await CreateTablesAsync();
                await CreateIndexesAsync();

                _isInitialized = true;
                _logger.LogInformation("MySQL database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize MySQL database");
                throw;
            }
        }

        private async Task CreateDatabaseIfNotExistsAsync()
        {
            var connectionStringWithoutDb = BuildConnectionStringWithoutDatabase();

            const string sql = "CREATE DATABASE IF NOT EXISTS `@database` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";

            await using var connection = new MySqlConnection(connectionStringWithoutDb);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql.Replace("@database", _config.DatabaseName), connection);
            await command.ExecuteNonQueryAsync();
        }

        private string BuildConnectionStringWithoutDatabase()
        {
            var builder = new MySqlConnectionStringBuilder(_connectionString);
            builder.Database = "";
            return builder.ConnectionString;
        }

        private async Task CreateTablesAsync()
        {
            var tables = new Dictionary<string, string>
            {
                ["dbUserTable"] = @"
                    CREATE TABLE IF NOT EXISTS `dbUserTable` (
                        `dbID` INT NOT NULL AUTO_INCREMENT,
                        `TwitchID` BIGINT NOT NULL,
                        `Name` VARCHAR(30) NOT NULL,
                        `isSub` TINYINT(1) DEFAULT 0,
                        `isVip` TINYINT(1) DEFAULT 0,
                        `isMod` TINYINT(1) DEFAULT 0,
                        `IsBroadcaster` TINYINT(1) DEFAULT 0,
                        `UvalCon` INT DEFAULT 0,
                        `messageCon` INT DEFAULT 0,
                        `roulettCon` INT DEFAULT 0,
                        `roulettCD` DOUBLE DEFAULT 0,
                        `UvalTimer` DOUBLE DEFAULT 0,
                        `banCount` INT DEFAULT 0,
                        `Points` DOUBLE DEFAULT 0,
                        `IsOnline` TINYINT(1) DEFAULT 0,
                        `QuizPoints` INT DEFAULT 0,
                        `QuizTotal` INT DEFAULT 0,
                        `IsPartner` TINYINT(1) DEFAULT 0,
                        `CreatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        `UpdatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                        PRIMARY KEY (`dbID`),
                        UNIQUE KEY `uk_twitchid` (`TwitchID`),
                        UNIQUE KEY `uk_name` (`Name`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

                ["dbUserMessageTable"] = @"
                    CREATE TABLE IF NOT EXISTS `dbUserMessageTable` (
                        `dbID` INT NOT NULL AUTO_INCREMENT,
                        `TwitchID` BIGINT NOT NULL,
                        `Name` VARCHAR(30) NOT NULL,
                        `Message` TEXT,
                        `TimeStamp` DOUBLE NOT NULL,
                        `CreatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (`dbID`),
                        KEY `idx_twitchid` (`TwitchID`),
                        KEY `idx_name` (`Name`),
                        KEY `idx_timestamp` (`TimeStamp`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

                ["dbQuiz"] = @"
                    CREATE TABLE IF NOT EXISTS `dbQuiz` (
                        `dbID` INT NOT NULL AUTO_INCREMENT,
                        `Question` TEXT NOT NULL,
                        `Answer` TEXT NOT NULL,
                        `Prize` INT DEFAULT 0,
                        `CreatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        `UpdatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                        PRIMARY KEY (`dbID`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"
            };

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var (tableName, createSql) in tables)
            {
                try
                {
                    await using var command = new MySqlCommand(createSql, connection);
                    await command.ExecuteNonQueryAsync();
                    _logger.LogDebug("Created/verified table: {TableName}", tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create table: {TableName}", tableName);
                    throw;
                }
            }
        }

        private async Task CreateIndexesAsync()
        {
            // Additional indexes for performance
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS `idx_user_points` ON `dbUserTable` (`Points` DESC)",
                "CREATE INDEX IF NOT EXISTS `idx_user_online` ON `dbUserTable` (`IsOnline`)",
                "CREATE INDEX IF NOT EXISTS `idx_user_messagecon` ON `dbUserTable` (`messageCon` DESC)",
                "CREATE INDEX IF NOT EXISTS `idx_user_roulettcon` ON `dbUserTable` (`roulettCon` DESC)"
            };

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var indexSql in indexes)
            {
                try
                {
                    await using var command = new MySqlCommand(indexSql, connection);
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create index: {IndexSql}", indexSql);
                    // Continue with other indexes
                }
            }
        }

        public async Task<UserObject> GetUserAsync(int twitchId)
        {
            const string sql = @"
                SELECT dbID, TwitchID, Name, isSub, isVip, isMod, IsBroadcaster, 
                       UvalCon, messageCon, roulettCon, roulettCD, UvalTimer, 
                       banCount, Points, IsOnline, QuizPoints, QuizTotal, IsPartner
                FROM dbUserTable 
                WHERE TwitchID = @TwitchID";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TwitchID", twitchId);

                await using MySqlDataReader reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return MapUserFromReader(reader);
                }

                return new UserObject { dbID = -404 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user by TwitchID: {TwitchID}", twitchId);
                return new UserObject { dbID = -404 };
            }
        }

        public async Task<UserObject> GetUserAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new UserObject { dbID = -404 };
            }

            const string sql = @"
                SELECT dbID, TwitchID, Name, isSub, isVip, isMod, IsBroadcaster, 
                       UvalCon, messageCon, roulettCon, roulettCD, UvalTimer, 
                       banCount, Points, IsOnline, QuizPoints, QuizTotal, IsPartner
                FROM dbUserTable 
                WHERE Name = @Name COLLATE utf8mb4_unicode_ci";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Name", name.Trim());

                await using MySqlDataReader reader = (MySqlDataReader)await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return MapUserFromReader(reader);
                }

                return new UserObject { dbID = -404 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user by name: {Name}", name);
                return new UserObject { dbID = -404 };
            }
        }

        private static UserObject MapUserFromReader(MySqlDataReader reader)
        {
            return new UserObject
            {
                dbID = reader.GetInt32("dbID"),
                TwitchID = reader.GetInt32("TwitchID"),
                Name = reader.GetString("Name"),
                isSub = reader.GetInt32("isSub"),
                isVip = reader.GetInt32("isVip"),
                isMod = reader.GetInt32("isMod"),
                IsBroadcaster = reader.GetInt32("IsBroadcaster"),
                UvalCon = reader.GetInt32("UvalCon"),
                messageCon = reader.GetInt32("messageCon"),
                roulettCon = reader.GetInt32("roulettCon"),
                roulettCD = reader.GetDouble("roulettCD"),
                UvalTimer = reader.GetDouble("UvalTimer"),
                banCount = reader.GetInt32("banCount"),
                Points = reader.GetInt32("Points"),
                IsOnline = reader.GetInt32("IsOnline"),
                QuizPoints = reader.GetInt32("QuizPoints"),
                QuizTotal = reader.GetInt32("QuizTotal"),
                isPartner = reader.GetInt32("IsPartner")
            };
        }

        public async Task AddOrUpdateUserAsync(UserObject user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            const string sql = @"
                INSERT INTO dbUserTable 
                    (TwitchID, Name, isSub, isVip, isMod, IsBroadcaster, UvalCon, messageCon, 
                     roulettCon, roulettCD, UvalTimer, banCount, Points, IsOnline, QuizPoints, QuizTotal, IsPartner)
                VALUES
                    (@TwitchID, @Name, @isSub, @isVip, @isMod, @IsBroadcaster, @UvalCon, @messageCon,
                     @roulettCon, @roulettCD, @UvalTimer, @banCount, @Points, @IsOnline, @QuizPoints, @QuizTotal, @IsPartner)
                ON DUPLICATE KEY UPDATE
                    Name = VALUES(Name),
                    isSub = VALUES(isSub),
                    isVip = VALUES(isVip),
                    isMod = VALUES(isMod),
                    IsBroadcaster = VALUES(IsBroadcaster),
                    UvalCon = VALUES(UvalCon),
                    messageCon = VALUES(messageCon),
                    roulettCon = VALUES(roulettCon),
                    roulettCD = VALUES(roulettCD),
                    UvalTimer = VALUES(UvalTimer),
                    banCount = VALUES(banCount),
                    Points = VALUES(Points),
                    IsOnline = VALUES(IsOnline),
                    QuizPoints = VALUES(QuizPoints),
                    QuizTotal = VALUES(QuizTotal),
                    IsPartner = VALUES(IsPartner)";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                AddUserParametersToCommand(command, user);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add/update user: TwitchID={TwitchID}, Name={Name}",
                    user.TwitchID, user.Name);
                throw;
            }
        }

        public async Task UpdateUserAsync(UserObject user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            const string sql = @"
                UPDATE dbUserTable SET 
                    Name = @Name, isSub = @isSub, isVip = @isVip, isMod = @isMod, 
                    IsBroadcaster = @IsBroadcaster, UvalCon = @UvalCon, messageCon = @messageCon, 
                    roulettCon = @roulettCon, roulettCD = @roulettCD, UvalTimer = @UvalTimer, 
                    banCount = @banCount, QuizPoints = @QuizPoints, QuizTotal = @QuizTotal, 
                    IsPartner = @IsPartner, Points = @Points
                WHERE TwitchID = @TwitchID";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                AddUserParametersToCommand(command, user);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    _logger.LogWarning("No user found to update with TwitchID: {TwitchID}", user.TwitchID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update user: TwitchID={TwitchID}", user.TwitchID);
                throw;
            }
        }

        private static void AddUserParametersToCommand(MySqlCommand command, UserObject user)
        {
            command.Parameters.AddWithValue("@TwitchID", user.TwitchID);
            command.Parameters.AddWithValue("@Name", user.Name ?? string.Empty);
            command.Parameters.AddWithValue("@isSub", user.isSub);
            command.Parameters.AddWithValue("@isVip", user.isVip);
            command.Parameters.AddWithValue("@isMod", user.isMod);
            command.Parameters.AddWithValue("@IsBroadcaster", user.IsBroadcaster);
            command.Parameters.AddWithValue("@UvalCon", user.UvalCon);
            command.Parameters.AddWithValue("@messageCon", user.messageCon);
            command.Parameters.AddWithValue("@roulettCon", user.roulettCon);
            command.Parameters.AddWithValue("@roulettCD", user.roulettCD);
            command.Parameters.AddWithValue("@UvalTimer", user.UvalTimer);
            command.Parameters.AddWithValue("@banCount", user.banCount);
            command.Parameters.AddWithValue("@Points", user.Points);
            command.Parameters.AddWithValue("@IsOnline", user.IsOnline);
            command.Parameters.AddWithValue("@QuizPoints", user.QuizPoints);
            command.Parameters.AddWithValue("@QuizTotal", user.QuizTotal);
            command.Parameters.AddWithValue("@IsPartner", user.isPartner);
        }

        public async Task SaveMessageAsync(int twitchId, string name, string message, double timestamp)
        {
            const string sql = @"
                INSERT INTO dbUserMessageTable (TwitchID, Name, Message, TimeStamp) 
                VALUES (@TwitchID, @Name, @Message, @TimeStamp)";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TwitchID", twitchId);
                command.Parameters.AddWithValue("@Name", name ?? string.Empty);
                command.Parameters.AddWithValue("@Message", message ?? string.Empty);
                command.Parameters.AddWithValue("@TimeStamp", timestamp);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save message for user: {Name}", name);
                throw;
            }
        }

        public async Task SaveMessagesAsync(List<MessageBuffer> messages)
        {
            if (messages == null || !messages.Any())
            {
                return;
            }

            const string sql = @"
                INSERT INTO dbUserMessageTable (TwitchID, Name, Message, TimeStamp) 
                VALUES (@TwitchID, @Name, @Message, @TimeStamp)";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = new MySqlCommand(sql, connection, transaction);

                var twitchIdParam = command.Parameters.Add("@TwitchID", MySqlDbType.Int32);
                var nameParam = command.Parameters.Add("@Name", MySqlDbType.VarChar);
                var messageParam = command.Parameters.Add("@Message", MySqlDbType.Text);
                var timestampParam = command.Parameters.Add("@TimeStamp", MySqlDbType.Double);

                foreach (var msg in messages)
                {
                    twitchIdParam.Value = int.Parse(msg.TtvID);
                    nameParam.Value = msg.Name ?? string.Empty;
                    messageParam.Value = msg.Message ?? string.Empty;
                    timestampParam.Value = Convert.ToDouble(msg.TimeStamp);

                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch messages");
                throw;
            }
        }

        public async Task<List<UserObject>> GetTopUsersAsync(string flag, int limit = 3)
        {
            var columnName = flag switch
            {
                "rtop" => "roulettCon",
                "top" => "messageCon",
                _ => throw new ArgumentException($"Invalid flag: {flag}", nameof(flag))
            };

            var sql = $@"
                SELECT Name, {columnName} 
                FROM dbUserTable 
                WHERE {columnName} > 0
                ORDER BY {columnName} DESC 
                LIMIT @Limit";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Limit", limit);

                await using var reader = await command.ExecuteReaderAsync();

                var users = new List<UserObject>();

                while (await reader.ReadAsync())
                {
                    var user = new UserObject
                    {
                        Name = reader.GetString("Name")
                    };

                    if (columnName == "messageCon")
                        user.messageCon = reader.GetInt32(columnName);
                    else if (columnName == "roulettCon")
                        user.roulettCon = reader.GetInt32(columnName);

                    users.Add(user);
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get top users for flag: {Flag}", flag);
                throw;
            }
        }

        public async Task<int[]> GetUserPositionAsync(string userName, string columnName)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(columnName))
            {
                return new[] { 0, 0 };
            }

            var validColumns = new[] {
                "messageCon",
                "roulettCon",
                "Points",
                "QuizPoints",
                "UvalCon",
                "banCount",
                "QuizTotal"
                };
            if (!validColumns.Contains(columnName))
            {
                _logger.LogError("SECURITY: Invalid column access attempt - Column: {ColumnName}, User: {UserName}, Method: GetUserPositionAsync", columnName, userName);
                return new[] { 0, 0 };
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get user's value
                var valueSql = $"SELECT {columnName} FROM dbUserTable WHERE Name = @UserName";
                await using var valueCommand = new MySqlCommand(valueSql, connection);
                valueCommand.Parameters.AddWithValue("@UserName", userName);

                var userValue = await valueCommand.ExecuteScalarAsync();
                if (userValue == null || Convert.ToInt32(userValue) < 1)
                {
                    return new[] { 0, 0 };
                }

                // Get user's position
                var positionSql = $@"
                    SELECT COUNT(*) FROM dbUserTable 
                    WHERE {columnName} >= (SELECT {columnName} FROM dbUserTable WHERE Name = @UserName)";

                await using var positionCommand = new MySqlCommand(positionSql, connection);
                positionCommand.Parameters.AddWithValue("@UserName", userName);

                var position = Convert.ToInt32(await positionCommand.ExecuteScalarAsync());

                // Get total users with value > 0
                var totalSql = $"SELECT COUNT(*) FROM dbUserTable WHERE {columnName} > 0";
                await using var totalCommand = new MySqlCommand(totalSql, connection);

                var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync());

                return new[] { position, total };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user position for {UserName} in column {ColumnName}",
                    userName, columnName);
                throw;
            }
        }

        public async Task DeleteUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentException("Username cannot be null or empty", nameof(userName));
            }

            const string sql = "DELETE FROM dbUserTable WHERE Name = @Name";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Name", userName);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    _logger.LogWarning("No user found to delete with name: {UserName}", userName);
                }
                else
                {
                    _logger.LogInformation("Deleted user: {UserName}", userName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user: {UserName}", userName);
                throw;
            }
        }

        public async Task AddPointsAsync(int amount, int? twitchId = null)
        {
            string sql;

            if (twitchId.HasValue)
            {
                sql = "UPDATE dbUserTable SET Points = Points + @Amount WHERE TwitchID = @TwitchID";
            }
            else
            {
                sql = "UPDATE dbUserTable SET Points = Points + @Amount WHERE IsOnline = 1";
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Amount", amount);

                if (twitchId.HasValue)
                {
                    command.Parameters.AddWithValue("@TwitchID", twitchId.Value);
                }

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add points: Amount={Amount}, TwitchID={TwitchID}",
                    amount, twitchId);
                throw;
            }
        }

        public async Task<QuizzObject> GetQuizAsync(int id)
        {
            const string sql = "SELECT Question, Answer, Prize FROM dbQuiz WHERE dbID = @ID";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ID", id);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new QuizzObject
                    {
                        QuizzQuestion = reader.GetString("Question"),
                        QuizzAnswer = reader.GetString("Answer"),
                        QuizzCost = reader.GetInt32("Prize")
                    };
                }

                return new QuizzObject();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get quiz with ID: {ID}", id);
                throw;
            }
        }

        public async Task AddQuizPointsAsync(int amount, int twitchId)
        {
            const string sql = @"
                UPDATE dbUserTable 
                SET QuizPoints = QuizPoints + @Amount, QuizTotal = QuizTotal + @Amount 
                WHERE TwitchID = @TwitchID";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@TwitchID", twitchId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add quiz points: Amount={Amount}, TwitchID={TwitchID}",
                    amount, twitchId);
                throw;
            }
        }

        public async Task SpendQuizPointsAsync(int amount, int twitchId)
        {
            const string sql = @"
                UPDATE dbUserTable 
                SET QuizPoints = GREATEST(0, QuizPoints - @Amount) 
                WHERE TwitchID = @TwitchID";

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@TwitchID", twitchId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to spend quiz points: Amount={Amount}, TwitchID={TwitchID}",
                    amount, twitchId);
                throw;
            }
        }

        public async Task UpdateOnlineStatusAsync(List<string> chatters)
        {
            if (chatters == null || !chatters.Any())
            {
                // Set all users offline if no chatters
                const string offlineAllSql = "UPDATE dbUserTable SET IsOnline = 0";

                try
                {
                    await using var connection = new MySqlConnection(_connectionString);
                    await connection.OpenAsync();

                    await using var command = new MySqlCommand(offlineAllSql, connection);
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to set all users offline");
                    throw;
                }
                return;
            }

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync();

                // Create temporary table for chatters
                const string createTempSql = @"
                    CREATE TEMPORARY TABLE temp_online_users (
                        Name VARCHAR(30) NOT NULL,
                        PRIMARY KEY (Name)
                    ) ENGINE=MEMORY";

                await using var createCommand = new MySqlCommand(createTempSql, connection, transaction);
                await createCommand.ExecuteNonQueryAsync();

                // Insert chatters into temp table
                const string insertSql = "INSERT IGNORE INTO temp_online_users (Name) VALUES (@Name)";
                await using var insertCommand = new MySqlCommand(insertSql, connection, transaction);
                var nameParam = insertCommand.Parameters.Add("@Name", MySqlDbType.VarChar);

                foreach (var chatter in chatters.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    nameParam.Value = chatter.Trim();
                    await insertCommand.ExecuteNonQueryAsync();
                }

                // Update online status in single query
                const string updateSql = @"
                    UPDATE dbUserTable u 
                    LEFT JOIN temp_online_users t ON u.Name = t.Name 
                    SET u.IsOnline = CASE WHEN t.Name IS NOT NULL THEN 1 ELSE 0 END";

                await using var updateCommand = new MySqlCommand(updateSql, connection, transaction);
                await updateCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                _logger.LogDebug("Updated online status for {ChatterCount} chatters", chatters.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update online status");
                throw;
            }
        }

        public async Task<TrackUser> TrackUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentException("Username cannot be null or empty", nameof(userName));
            }

            var trackInfo = new TrackUser
            {
                Count = 0,
                DBName = new List<string>()
            };

            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get all databases that have the dbUserTable
                const string databasesSql = @"
                    SELECT DISTINCT TABLE_SCHEMA 
                    FROM information_schema.TABLES 
                    WHERE TABLE_NAME = 'dbUserTable' 
                    AND TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')";

                var allSchemas = new List<string>();
                await using (var dbCommand = new MySqlCommand(databasesSql, connection))
                {
                    await using var dbReader = await dbCommand.ExecuteReaderAsync();
                    while (await dbReader.ReadAsync())
                    {
                        allSchemas.Add(dbReader.GetString(0));
                    }
                }

                // Check which databases contain the user
                foreach (var schema in allSchemas)
                {
                    var checkUserSql = $"SELECT COUNT(*) FROM `{schema}`.dbUserTable WHERE Name = @UserName";
                    await using var checkCommand = new MySqlCommand(checkUserSql, connection);
                    checkCommand.Parameters.AddWithValue("@UserName", userName);

                    var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    if (count > 0)
                    {
                        trackInfo.Count++;
                        trackInfo.DBName.Add(schema);
                    }
                }

                // Get messages from all databases where user exists
                var allMessages = new List<TrackedMessages>();

                foreach (var schema in trackInfo.DBName)
                {
                    var messagesSql = $@"
                        SELECT TwitchID, Name, Message, TimeStamp 
                        FROM `{schema}`.dbUserMessageTable 
                        WHERE Name = @UserName 
                        ORDER BY TimeStamp";

                    await using var messagesCommand = new MySqlCommand(messagesSql, connection);
                    messagesCommand.Parameters.AddWithValue("@UserName", userName);

                    await using var messagesReader = await messagesCommand.ExecuteReaderAsync();

                    while (await messagesReader.ReadAsync())
                    {
                        allMessages.Add(new TrackedMessages
                        {
                            TimeStamp = Convert.ToInt64(messagesReader.GetDouble("TimeStamp")),
                            ChannelName = schema,
                            Message = messagesReader.GetString("Message")
                        });
                    }
                }

                // Sort messages by timestamp and process them
                var sortedMessages = allMessages.OrderBy(m => m.TimeStamp).ToList();

                var extractMessage = new ExtractMessage();
                foreach (var message in sortedMessages)
                {
                    var formattedMessage = $"{IntUtil.UnixTimeStampToDateTime(message.TimeStamp)}, Channel: {message.ChannelName}, Message: {message.Message}";
                    extractMessage.ExtractMessageTask(formattedMessage);
                }

                return trackInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track user: {UserName}", userName);
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clean up any resources if needed
                _disposed = true;
            }
        }
    }
}