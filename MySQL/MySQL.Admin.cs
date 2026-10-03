using MySql.Data.MySqlClient;
using SkillzBot.Api;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.MYSQL
{
    /// <summary>Web panel queries. Paging and search always go through indexed columns or a bounded time range.</summary>
    public sealed partial class MySqlDatabaseService : IAdminRepository
    {
        private const string EditorSchemaSql = @"
            CREATE TABLE IF NOT EXISTS dbBotEditorTable (
                TwitchID BIGINT NOT NULL PRIMARY KEY,
                Login VARCHAR(64) NOT NULL,
                AddedBy VARCHAR(64) NULL,
                AddedAt DATETIME NOT NULL
            ) CHARACTER SET utf8mb4";
        private int _editorSchemaReady;
        private static readonly HashSet<string> UserSortColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "messageCon", "Points", "QuizPoints", "QuizTotal", "roulettCon", "banCount", "UvalCon", "UpdatedAt", "CreatedAt", "Name" };

        private async Task EnsureEditorSchemaAsync(MySqlConnection connection)
        {
            if (Volatile.Read(ref _editorSchemaReady) == 1) return;
            await using var cmd = new MySqlCommand(EditorSchemaSql, connection);
            await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            Volatile.Write(ref _editorSchemaReady, 1);
        }

        #region Editors

        public async Task<bool> IsEditorAsync(long twitchId)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEditorSchemaAsync(connection);
            await using var cmd = new MySqlCommand("SELECT 1 FROM dbBotEditorTable WHERE TwitchID = @id", connection);
            cmd.Parameters.AddWithValue("@id", twitchId);
            return await cmd.ExecuteScalarAsync().WaitAsync(DbCallTimeout) != null;
        }

        public async Task<List<EditorRow>> GetEditorsAsync()
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEditorSchemaAsync(connection);
            await using var cmd = new MySqlCommand("SELECT TwitchID, Login, AddedBy, AddedAt FROM dbBotEditorTable ORDER BY AddedAt", connection);
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var list = new List<EditorRow>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                list.Add(new EditorRow(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
            return list;
        }

        public async Task AddEditorAsync(long twitchId, string login, string addedBy)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEditorSchemaAsync(connection);
            await using var cmd = new MySqlCommand("INSERT INTO dbBotEditorTable (TwitchID, Login, AddedBy, AddedAt) VALUES (@id, @login, @by, @at) ON DUPLICATE KEY UPDATE Login = VALUES(Login)", connection);
            cmd.Parameters.AddWithValue("@id", twitchId);
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@by", (object)addedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
        }

        public async Task<bool> RemoveEditorAsync(long twitchId)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEditorSchemaAsync(connection);
            await using var cmd = new MySqlCommand("DELETE FROM dbBotEditorTable WHERE TwitchID = @id", connection);
            cmd.Parameters.AddWithValue("@id", twitchId);
            return await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout) > 0;
        }

        #endregion

        #region Users and messages

        public async Task<PagedResult<UserObject>> SearchUsersAsync(string query, string sort, bool descending, int page, int pageSize)
        {
            CountQuery();
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            string sortColumn = UserSortColumns.Contains(sort ?? "") ? UserSortColumns.First(c => c.Equals(sort, StringComparison.OrdinalIgnoreCase)) : "messageCon";
            string where = string.IsNullOrWhiteSpace(query) ? "" : "WHERE Name LIKE CONCAT(@q, '%')";
            await using var connection = await OpenConnectionAsync();

            long total;
            await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM dbUserTable {where}", connection))
            {
                if (where.Length > 0) count.Parameters.AddWithValue("@q", query.Trim().TrimStart('@'));
                total = Convert.ToInt64(await count.ExecuteScalarAsync().WaitAsync(DbCallTimeout));
            }

            var sql = $"SELECT * FROM dbUserTable {where} ORDER BY `{sortColumn}` {(descending ? "DESC" : "ASC")} LIMIT @limit OFFSET @offset";
            await using var cmd = new MySqlCommand(sql, connection);
            if (where.Length > 0) cmd.Parameters.AddWithValue("@q", query.Trim().TrimStart('@'));
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var items = new List<UserObject>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout)) items.Add(MapUserFromReader(reader));
            return new PagedResult<UserObject>(items, page, pageSize, total);
        }

        public async Task<PagedResult<MessageRow>> SearchMessagesAsync(string user, string text, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
        {
            CountQuery();
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            // Without a user or a start time the search is bounded to the last 7 days so the message table is never scanned.
            if (string.IsNullOrWhiteSpace(user) && !fromUtc.HasValue) fromUtc = DateTime.UtcNow.AddDays(-7);

            var where = new List<string>();
            var parameters = new List<MySqlParameter>();
            if (!string.IsNullOrWhiteSpace(user)) { where.Add("Name = @user"); parameters.Add(new MySqlParameter("@user", user.Trim().TrimStart('@'))); }
            if (fromUtc.HasValue) { where.Add("TimeStamp >= @from"); parameters.Add(new MySqlParameter("@from", new DateTimeOffset(fromUtc.Value).ToUnixTimeSeconds())); }
            if (toUtc.HasValue) { where.Add("TimeStamp <= @to"); parameters.Add(new MySqlParameter("@to", new DateTimeOffset(toUtc.Value).ToUnixTimeSeconds())); }
            if (!string.IsNullOrWhiteSpace(text)) { where.Add("Message LIKE CONCAT('%', @text, '%')"); parameters.Add(new MySqlParameter("@text", text.Trim())); }
            string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            await using var connection = await OpenConnectionAsync();
            long total;
            await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM dbUserMessageTable {whereSql}", connection))
            {
                foreach (var p in parameters) count.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                total = Convert.ToInt64(await count.ExecuteScalarAsync().WaitAsync(DbCallTimeout));
            }
            await using var cmd = new MySqlCommand($"SELECT dbID, TwitchID, Name, Message, TimeStamp FROM dbUserMessageTable {whereSql} ORDER BY dbID DESC LIMIT @limit OFFSET @offset", connection);
            foreach (var p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var items = new List<MessageRow>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
            {
                double ts = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                items.Add(new MessageRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DateTimeOffset.FromUnixTimeSeconds((long)ts).UtcDateTime));
            }
            return new PagedResult<MessageRow>(items, page, pageSize, total);
        }

        public async Task<List<ActivityBucket>> GetActivityAsync(DateTime fromUtc, int bucketSeconds)
        {
            CountQuery();
            bucketSeconds = Math.Max(60, bucketSeconds);
            await using var connection = await OpenConnectionAsync();
            await using var cmd = new MySqlCommand(@"SELECT FLOOR(TimeStamp / @bucket) * @bucket AS b, COUNT(*), COUNT(DISTINCT TwitchID)
                FROM dbUserMessageTable WHERE TimeStamp >= @from GROUP BY b ORDER BY b", connection);
            cmd.Parameters.AddWithValue("@bucket", bucketSeconds);
            cmd.Parameters.AddWithValue("@from", new DateTimeOffset(fromUtc).ToUnixTimeSeconds());
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var list = new List<ActivityBucket>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                list.Add(new ActivityBucket(DateTimeOffset.FromUnixTimeSeconds((long)reader.GetDouble(0)).UtcDateTime, reader.GetInt32(1), reader.GetInt32(2)));
            return list;
        }

        #endregion

        #region Quiz

        public async Task<List<QuizRow>> GetQuizzesAsync()
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await using var cmd = new MySqlCommand("SELECT dbID, Question, Answer, Prize FROM dbQuiz ORDER BY dbID", connection);
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var list = new List<QuizRow>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                list.Add(new QuizRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? 0 : reader.GetInt32(3)));
            return list;
        }

        public async Task<int> AddQuizAsync(string question, string answer, int prize)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await using var cmd = new MySqlCommand("INSERT INTO dbQuiz (Question, Answer, Prize) VALUES (@q, @a, @p); SELECT LAST_INSERT_ID();", connection);
            cmd.Parameters.AddWithValue("@q", question);
            cmd.Parameters.AddWithValue("@a", answer);
            cmd.Parameters.AddWithValue("@p", prize);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync().WaitAsync(DbCallTimeout));
        }

        public async Task<bool> UpdateQuizAsync(int id, string question, string answer, int prize)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await using var cmd = new MySqlCommand("UPDATE dbQuiz SET Question = @q, Answer = @a, Prize = @p WHERE dbID = @id", connection);
            cmd.Parameters.AddWithValue("@q", question);
            cmd.Parameters.AddWithValue("@a", answer);
            cmd.Parameters.AddWithValue("@p", prize);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout) > 0;
        }

        public async Task<bool> DeleteQuizAsync(int id)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await using var cmd = new MySqlCommand("DELETE FROM dbQuiz WHERE dbID = @id", connection);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout) > 0;
        }

        #endregion

        #region Engagement rows

        public async Task<List<PredictionRow>> GetPredictionsAsync(int days, int limit)
        {
            CountQuery();
            limit = Math.Clamp(limit, 1, 500);
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            var rows = new List<PredictionRow>();
            await using (var cmd = new MySqlCommand(@"SELECT PredictionId, Kind, Source, Title, MatchId, StartedAt, EndedAt, Status, TotalUsers, TotalPoints
                FROM dbPredictionTable WHERE COALESCE(EndedAt, StartedAt) >= UTC_TIMESTAMP() - INTERVAL @days DAY ORDER BY COALESCE(EndedAt, StartedAt) DESC LIMIT @limit", connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                cmd.Parameters.AddWithValue("@limit", limit);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                    rows.Add(new PredictionRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4), UtcOrNull(reader, 5), UtcOrNull(reader, 6), reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.GetInt32(8), reader.GetInt64(9), new List<PredictionOutcomeRow>()));
            }
            if (rows.Count == 0) return rows;
            var byId = rows.ToDictionary(r => r.PredictionId);
            var ids = string.Join(",", rows.Select((r, i) => $"@p{i}"));
            await using (var cmd = new MySqlCommand($"SELECT PredictionId, Title, Color, Users, ChannelPoints, IsWinner FROM dbPredictionOutcomeTable WHERE PredictionId IN ({ids})", connection))
            {
                for (int i = 0; i < rows.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", rows[i].PredictionId);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                    if (byId.TryGetValue(reader.GetString(0), out var row))
                        row.Outcomes.Add(new PredictionOutcomeRow(reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetBoolean(5)));
            }
            return rows;
        }

        public async Task<List<PollRow>> GetPollsAsync(int days, int limit)
        {
            CountQuery();
            limit = Math.Clamp(limit, 1, 500);
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            var rows = new List<PollRow>();
            await using (var cmd = new MySqlCommand(@"SELECT PollId, Source, Title, StartedAt, EndedAt, Status, TotalVotes, WinnerTitle FROM dbPollTable
                WHERE COALESCE(EndedAt, StartedAt) >= UTC_TIMESTAMP() - INTERVAL @days DAY ORDER BY COALESCE(EndedAt, StartedAt) DESC LIMIT @limit", connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                cmd.Parameters.AddWithValue("@limit", limit);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                    rows.Add(new PollRow(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), UtcOrNull(reader, 3), UtcOrNull(reader, 4),
                        reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7), new List<PollChoiceRow>()));
            }
            if (rows.Count == 0) return rows;
            var byId = rows.ToDictionary(r => r.PollId);
            var ids = string.Join(",", rows.Select((r, i) => $"@p{i}"));
            await using (var cmd = new MySqlCommand($"SELECT PollId, Title, KindKey, Votes, ChannelPointsVotes FROM dbPollChoiceTable WHERE PollId IN ({ids})", connection))
            {
                for (int i = 0; i < rows.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", rows[i].PollId);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                    if (byId.TryGetValue(reader.GetString(0), out var row))
                        row.Choices.Add(new PollChoiceRow(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)));
            }
            return rows;
        }

        private static DateTime? UtcOrNull(System.Data.Common.DbDataReader reader, int i) =>
            reader.IsDBNull(i) ? null : DateTime.SpecifyKind(reader.GetDateTime(i), DateTimeKind.Utc);

        #endregion
    }
}
