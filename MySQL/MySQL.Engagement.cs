using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.MYSQL
{
    /// <summary>Prediction and poll engagement history. Tables are created on first use.</summary>
    public sealed partial class MySqlDatabaseService : IEngagementRepository
    {
        private static readonly string[] EngagementSchemaSql =
        {
            @"CREATE TABLE IF NOT EXISTS dbPredictionTable (
                PredictionId VARCHAR(64) NOT NULL PRIMARY KEY,
                Kind VARCHAR(32) NOT NULL DEFAULT 'manual',
                Source VARCHAR(16) NOT NULL DEFAULT 'manual',
                Title VARCHAR(128) NULL,
                MatchId VARCHAR(32) NULL,
                StartedAt DATETIME NULL,
                EndedAt DATETIME NULL,
                Status VARCHAR(16) NULL,
                WinningOutcomeId VARCHAR(64) NULL,
                OutcomesCount INT NOT NULL DEFAULT 0,
                TotalUsers INT NOT NULL DEFAULT 0,
                TotalPoints BIGINT NOT NULL DEFAULT 0,
                INDEX idx_pred_ended (EndedAt)
            ) CHARACTER SET utf8mb4",
            @"CREATE TABLE IF NOT EXISTS dbPredictionOutcomeTable (
                PredictionId VARCHAR(64) NOT NULL,
                OutcomeId VARCHAR(64) NOT NULL,
                Title VARCHAR(64) NULL,
                Color VARCHAR(16) NULL,
                Users INT NOT NULL DEFAULT 0,
                ChannelPoints BIGINT NOT NULL DEFAULT 0,
                IsWinner TINYINT(1) NOT NULL DEFAULT 0,
                PRIMARY KEY (PredictionId, OutcomeId)
            ) CHARACTER SET utf8mb4",
            @"CREATE TABLE IF NOT EXISTS dbPredictionBetTable (
                PredictionId VARCHAR(64) NOT NULL,
                TwitchID BIGINT NOT NULL,
                OutcomeId VARCHAR(64) NULL,
                UserLogin VARCHAR(64) NULL,
                PointsUsed INT NOT NULL DEFAULT 0,
                PointsWon INT NULL,
                PRIMARY KEY (PredictionId, TwitchID),
                INDEX idx_bet_user (TwitchID)
            ) CHARACTER SET utf8mb4",
            @"CREATE TABLE IF NOT EXISTS dbPollTable (
                PollId VARCHAR(64) NOT NULL PRIMARY KEY,
                Source VARCHAR(16) NOT NULL DEFAULT 'manual',
                Title VARCHAR(128) NULL,
                StartedAt DATETIME NULL,
                EndedAt DATETIME NULL,
                Status VARCHAR(16) NULL,
                TotalVotes INT NOT NULL DEFAULT 0,
                WinnerTitle VARCHAR(64) NULL,
                INDEX idx_poll_ended (EndedAt)
            ) CHARACTER SET utf8mb4",
            @"CREATE TABLE IF NOT EXISTS dbPollChoiceTable (
                PollId VARCHAR(64) NOT NULL,
                Title VARCHAR(64) NOT NULL,
                KindKey VARCHAR(32) NULL,
                Votes INT NOT NULL DEFAULT 0,
                ChannelPointsVotes INT NOT NULL DEFAULT 0,
                BitsVotes INT NOT NULL DEFAULT 0,
                PRIMARY KEY (PollId, Title)
            ) CHARACTER SET utf8mb4",
        };
        private int _engagementSchemaReady;

        private async Task EnsureEngagementSchemaAsync(MySqlConnection connection)
        {
            if (Volatile.Read(ref _engagementSchemaReady) == 1) return;
            foreach (var sql in EngagementSchemaSql)
            {
                await using var cmd = new MySqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }
            Volatile.Write(ref _engagementSchemaReady, 1);
        }

        public async Task PredictionStartedAsync(string predictionId, string kind, string source, string title, string matchId, DateTime startedUtc)
        {
            CountQuery();
            const string sql = @"INSERT INTO dbPredictionTable (PredictionId, Kind, Source, Title, MatchId, StartedAt)
                VALUES (@id, @kind, @source, @title, @match, @started)
                ON DUPLICATE KEY UPDATE Kind = VALUES(Kind), Source = VALUES(Source), MatchId = VALUES(MatchId), StartedAt = COALESCE(StartedAt, VALUES(StartedAt))";
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", predictionId);
            cmd.Parameters.AddWithValue("@kind", kind ?? "manual");
            cmd.Parameters.AddWithValue("@source", source ?? "manual");
            cmd.Parameters.AddWithValue("@title", Truncate(title, 128));
            cmd.Parameters.AddWithValue("@match", (object)matchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@started", startedUtc);
            await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
        }

        public async Task PredictionEndedAsync(PredictionResultRecord r)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            await using var tx = await connection.BeginTransactionAsync().AsTask().WaitAsync(DbCallTimeout);

            const string predSql = @"INSERT INTO dbPredictionTable (PredictionId, Title, StartedAt, EndedAt, Status, WinningOutcomeId, OutcomesCount, TotalUsers, TotalPoints)
                VALUES (@id, @title, @started, @ended, @status, @win, @n, @users, @points)
                ON DUPLICATE KEY UPDATE Title = VALUES(Title), StartedAt = COALESCE(StartedAt, VALUES(StartedAt)), EndedAt = VALUES(EndedAt),
                    Status = VALUES(Status), WinningOutcomeId = VALUES(WinningOutcomeId), OutcomesCount = VALUES(OutcomesCount),
                    TotalUsers = VALUES(TotalUsers), TotalPoints = VALUES(TotalPoints)";
            await using (var cmd = new MySqlCommand(predSql, connection, tx))
            {
                cmd.Parameters.AddWithValue("@id", r.PredictionId);
                cmd.Parameters.AddWithValue("@title", Truncate(r.Title, 128));
                cmd.Parameters.AddWithValue("@started", (object)r.StartedUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ended", r.EndedUtc);
                cmd.Parameters.AddWithValue("@status", Truncate(r.Status?.ToLowerInvariant(), 16));
                cmd.Parameters.AddWithValue("@win", (object)r.WinningOutcomeId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@n", r.Outcomes.Count);
                cmd.Parameters.AddWithValue("@users", r.Outcomes.Sum(o => o.Users));
                cmd.Parameters.AddWithValue("@points", r.Outcomes.Sum(o => o.ChannelPoints));
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }

            const string outcomeSql = @"INSERT INTO dbPredictionOutcomeTable (PredictionId, OutcomeId, Title, Color, Users, ChannelPoints, IsWinner)
                VALUES (@pid, @oid, @title, @color, @users, @points, @winner)
                ON DUPLICATE KEY UPDATE Title = VALUES(Title), Color = VALUES(Color), Users = VALUES(Users), ChannelPoints = VALUES(ChannelPoints), IsWinner = VALUES(IsWinner)";
            const string betSql = @"INSERT INTO dbPredictionBetTable (PredictionId, TwitchID, OutcomeId, UserLogin, PointsUsed, PointsWon)
                VALUES (@pid, @uid, @oid, @login, @used, @won)
                ON DUPLICATE KEY UPDATE OutcomeId = VALUES(OutcomeId), UserLogin = VALUES(UserLogin), PointsUsed = VALUES(PointsUsed), PointsWon = VALUES(PointsWon)";
            foreach (var o in r.Outcomes)
            {
                await using (var cmd = new MySqlCommand(outcomeSql, connection, tx))
                {
                    cmd.Parameters.AddWithValue("@pid", r.PredictionId);
                    cmd.Parameters.AddWithValue("@oid", o.OutcomeId);
                    cmd.Parameters.AddWithValue("@title", Truncate(o.Title, 64));
                    cmd.Parameters.AddWithValue("@color", Truncate(o.Color, 16));
                    cmd.Parameters.AddWithValue("@users", o.Users);
                    cmd.Parameters.AddWithValue("@points", o.ChannelPoints);
                    cmd.Parameters.AddWithValue("@winner", o.IsWinner);
                    await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
                }
                foreach (var p in o.TopPredictors)
                {
                    await using var cmd = new MySqlCommand(betSql, connection, tx);
                    cmd.Parameters.AddWithValue("@pid", r.PredictionId);
                    cmd.Parameters.AddWithValue("@uid", p.TwitchId);
                    cmd.Parameters.AddWithValue("@oid", o.OutcomeId);
                    cmd.Parameters.AddWithValue("@login", Truncate(p.Login, 64));
                    cmd.Parameters.AddWithValue("@used", p.PointsUsed);
                    cmd.Parameters.AddWithValue("@won", (object)p.PointsWon ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
                }
            }
            await tx.CommitAsync().WaitAsync(DbCallTimeout);
        }

        public async Task PollStartedAsync(string pollId, string source, string title, DateTime startedUtc, IReadOnlyList<PollChoiceRecord> choices)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            const string pollSql = @"INSERT INTO dbPollTable (PollId, Source, Title, StartedAt) VALUES (@id, @source, @title, @started)
                ON DUPLICATE KEY UPDATE Source = VALUES(Source), StartedAt = COALESCE(StartedAt, VALUES(StartedAt))";
            await using (var cmd = new MySqlCommand(pollSql, connection))
            {
                cmd.Parameters.AddWithValue("@id", pollId);
                cmd.Parameters.AddWithValue("@source", source ?? "manual");
                cmd.Parameters.AddWithValue("@title", Truncate(title, 128));
                cmd.Parameters.AddWithValue("@started", startedUtc);
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }
            const string choiceSql = @"INSERT INTO dbPollChoiceTable (PollId, Title, KindKey) VALUES (@pid, @title, @kind)
                ON DUPLICATE KEY UPDATE KindKey = VALUES(KindKey)";
            foreach (var c in choices)
            {
                await using var cmd = new MySqlCommand(choiceSql, connection);
                cmd.Parameters.AddWithValue("@pid", pollId);
                cmd.Parameters.AddWithValue("@title", Truncate(c.Title, 64));
                cmd.Parameters.AddWithValue("@kind", (object)c.KindKey ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }
        }

        public async Task PollEndedAsync(PollResultRecord r)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);
            var winner = r.Choices.OrderByDescending(c => c.Votes).FirstOrDefault();
            const string pollSql = @"INSERT INTO dbPollTable (PollId, Title, StartedAt, EndedAt, Status, TotalVotes, WinnerTitle)
                VALUES (@id, @title, @started, @ended, @status, @votes, @winner)
                ON DUPLICATE KEY UPDATE Title = VALUES(Title), StartedAt = COALESCE(StartedAt, VALUES(StartedAt)), EndedAt = VALUES(EndedAt),
                    Status = VALUES(Status), TotalVotes = VALUES(TotalVotes), WinnerTitle = VALUES(WinnerTitle)";
            await using (var cmd = new MySqlCommand(pollSql, connection))
            {
                cmd.Parameters.AddWithValue("@id", r.PollId);
                cmd.Parameters.AddWithValue("@title", Truncate(r.Title, 128));
                cmd.Parameters.AddWithValue("@started", (object)r.StartedUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ended", r.EndedUtc);
                cmd.Parameters.AddWithValue("@status", Truncate(r.Status?.ToLowerInvariant(), 16));
                cmd.Parameters.AddWithValue("@votes", r.Choices.Sum(c => c.Votes));
                cmd.Parameters.AddWithValue("@winner", Truncate(winner?.Title, 64));
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }
            const string choiceSql = @"INSERT INTO dbPollChoiceTable (PollId, Title, Votes, ChannelPointsVotes, BitsVotes) VALUES (@pid, @title, @votes, @cp, @bits)
                ON DUPLICATE KEY UPDATE Votes = VALUES(Votes), ChannelPointsVotes = VALUES(ChannelPointsVotes), BitsVotes = VALUES(BitsVotes)";
            foreach (var c in r.Choices)
            {
                await using var cmd = new MySqlCommand(choiceSql, connection);
                cmd.Parameters.AddWithValue("@pid", r.PollId);
                cmd.Parameters.AddWithValue("@title", Truncate(c.Title, 64));
                cmd.Parameters.AddWithValue("@votes", c.Votes);
                cmd.Parameters.AddWithValue("@cp", c.ChannelPointsVotes);
                cmd.Parameters.AddWithValue("@bits", c.BitsVotes);
                await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            }
        }

        public async Task<EngagementSummary> GetEngagementSummaryAsync(int days)
        {
            CountQuery();
            var s = new EngagementSummary { Days = days };
            await using var connection = await OpenConnectionAsync();
            await EnsureEngagementSchemaAsync(connection);

            const string byKindSql = @"SELECT Kind, Source, COUNT(*), AVG(TotalUsers), MAX(TotalUsers), AVG(TotalPoints), SUM(TotalPoints)
                FROM dbPredictionTable WHERE Status = 'resolved' AND EndedAt >= UTC_TIMESTAMP() - INTERVAL @days DAY GROUP BY Kind, Source";
            await using (var cmd = new MySqlCommand(byKindSql, connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                var kinds = new Dictionary<string, (int Count, double Users, double Points)>();
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                {
                    string kind = reader.GetString(0), source = reader.GetString(1);
                    int count = reader.GetInt32(2);
                    double avgUsers = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                    int maxUsers = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                    double avgPoints = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);
                    long sumPoints = reader.IsDBNull(6) ? 0 : Convert.ToInt64(reader.GetValue(6));
                    s.Predictions += count;
                    s.TotalPoints += sumPoints;
                    if (source == "manual") s.ManualPredictions += count;
                    if (kind == "winlose")
                    {
                        s.WinLoseAvgUsers = (s.WinLoseAvgUsers * s.WinLose + avgUsers * count) / (s.WinLose + count);
                        s.WinLoseAvgPoints = (s.WinLoseAvgPoints * s.WinLose + avgPoints * count) / (s.WinLose + count);
                        s.WinLose += count;
                        s.WinLoseMaxUsers = Math.Max(s.WinLoseMaxUsers, maxUsers);
                    }
                    else
                    {
                        s.OtherAvgUsers = (s.OtherAvgUsers * s.Other + avgUsers * count) / (s.Other + count);
                        s.Other += count;
                    }
                    var prev = kinds.TryGetValue(kind, out var k) ? k : (0, 0, 0);
                    kinds[kind] = (prev.Count + count, (prev.Users * prev.Count + avgUsers * count) / (prev.Count + count), (prev.Points * prev.Count + avgPoints * count) / (prev.Count + count));
                }
                s.ByKind = kinds.Select(kv => new KindStat(kv.Key, kv.Value.Count, kv.Value.Users, kv.Value.Points)).OrderByDescending(k => k.Count).ToList();
            }

            const string outcomesSql = @"SELECT o.Title, AVG(o.Users), AVG(o.ChannelPoints), SUM(o.IsWinner) FROM dbPredictionOutcomeTable o
                JOIN dbPredictionTable p ON p.PredictionId = o.PredictionId
                WHERE p.Kind = 'winlose' AND p.Status = 'resolved' AND p.EndedAt >= UTC_TIMESTAMP() - INTERVAL @days DAY GROUP BY o.Title";
            await using (var cmd = new MySqlCommand(outcomesSql, connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                    s.WinLoseOutcomes.Add(new OutcomeStat(reader.GetString(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1), reader.IsDBNull(2) ? 0 : reader.GetDouble(2), reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))));
            }

            const string bettorsSql = @"SELECT COUNT(DISTINCT b.TwitchID) FROM dbPredictionBetTable b JOIN dbPredictionTable p ON p.PredictionId = b.PredictionId
                WHERE p.EndedAt >= UTC_TIMESTAMP() - INTERVAL @days DAY";
            await using (var cmd = new MySqlCommand(bettorsSql, connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                s.KnownBettors = Convert.ToInt32(await cmd.ExecuteScalarAsync().WaitAsync(DbCallTimeout));
            }

            const string pollsSql = @"SELECT COUNT(*), AVG(TotalVotes), MAX(TotalVotes) FROM dbPollTable
                WHERE Status = 'completed' AND EndedAt >= UTC_TIMESTAMP() - INTERVAL @days DAY";
            await using (var cmd = new MySqlCommand(pollsSql, connection))
            {
                cmd.Parameters.AddWithValue("@days", days);
                await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
                if (await reader.ReadAsync().WaitAsync(DbCallTimeout))
                {
                    s.Polls = reader.GetInt32(0);
                    s.PollAvgVotes = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    s.PollMaxVotes = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                }
            }
            return s;
        }

        private static object Truncate(string value, int max) =>
            value == null ? DBNull.Value : (value.Length <= max ? value : value.Substring(0, max));
    }
}
