using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.MYSQL
{
    /// <summary>VIP registry storage. The table is created on first use (the database is not initialized at startup).</summary>
    public sealed partial class MySqlDatabaseService : IVipRepository
    {
        private const string VipSchemaSql = @"
            CREATE TABLE IF NOT EXISTS dbVipTable (
                TwitchID BIGINT NOT NULL PRIMARY KEY,
                Login VARCHAR(64) NOT NULL,
                DisplayName VARCHAR(64) NULL,
                VipSince DATETIME NULL,
                Source VARCHAR(16) NULL,
                DbId INT NULL,
                FirstSeen DATETIME NOT NULL,
                Pinned TINYINT(1) NOT NULL DEFAULT 0
            ) CHARACTER SET utf8mb4";
        private int _vipSchemaReady;

        private async Task EnsureVipSchemaAsync(MySqlConnection connection)
        {
            if (Volatile.Read(ref _vipSchemaReady) == 1) return;
            await using var cmd = new MySqlCommand(VipSchemaSql, connection);
            await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
            Volatile.Write(ref _vipSchemaReady, 1);
        }

        public async Task<List<VipRecord>> GetVipsAsync()
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureVipSchemaAsync(connection);
            await using var cmd = new MySqlCommand("SELECT TwitchID, Login, DisplayName, VipSince, Source, DbId, FirstSeen, Pinned FROM dbVipTable", connection);
            await using var reader = await cmd.ExecuteReaderAsync().WaitAsync(DbCallTimeout);
            var list = new List<VipRecord>();
            while (await reader.ReadAsync().WaitAsync(DbCallTimeout))
            {
                list.Add(new VipRecord
                {
                    TwitchId = reader.GetInt64(0),
                    Login = reader.GetString(1),
                    DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Since = reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                    Source = reader.IsDBNull(4) ? null : reader.GetString(4),
                    DbId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    FirstSeenUtc = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                    Pinned = reader.GetBoolean(7),
                });
            }
            return list;
        }

        public async Task UpsertVipAsync(VipRecord r)
        {
            CountQuery();
            const string sql = @"INSERT INTO dbVipTable (TwitchID, Login, DisplayName, VipSince, Source, DbId, FirstSeen, Pinned)
                VALUES (@id, @login, @name, @since, @source, @dbid, @first, @pinned)
                ON DUPLICATE KEY UPDATE Login = VALUES(Login), DisplayName = VALUES(DisplayName), VipSince = VALUES(VipSince),
                    Source = VALUES(Source), DbId = VALUES(DbId), FirstSeen = VALUES(FirstSeen), Pinned = VALUES(Pinned)";
            await using var connection = await OpenConnectionAsync();
            await EnsureVipSchemaAsync(connection);
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", r.TwitchId);
            cmd.Parameters.AddWithValue("@login", r.Login);
            cmd.Parameters.AddWithValue("@name", (object)r.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@since", (object)r.Since ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source", (object)r.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dbid", (object)r.DbId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@first", r.FirstSeenUtc);
            cmd.Parameters.AddWithValue("@pinned", r.Pinned);
            await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout);
        }

        public async Task<bool> DeleteVipAsync(long twitchId)
        {
            CountQuery();
            await using var connection = await OpenConnectionAsync();
            await EnsureVipSchemaAsync(connection);
            await using var cmd = new MySqlCommand("DELETE FROM dbVipTable WHERE TwitchID = @id", connection);
            cmd.Parameters.AddWithValue("@id", twitchId);
            return await cmd.ExecuteNonQueryAsync().WaitAsync(DbCallTimeout) > 0;
        }
    }
}
