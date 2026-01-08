using MySql.Data.MySqlClient;
using SkillzBot.Singleton;

namespace SkillzBot.MYSQL
{
    class DBUtils
    {
        public static MySqlConnection GetDBConnection(string database, string username, string password)
        {
            string host = IllSingleton.Config.Database.Host;
            int port = IllSingleton.Config.Database.Port;
            return DBMySQLUtils.GetDBConnection(host, port, database, username, password);
        }
        public static MySqlConnection GetDBConnection(string username, string password)
        {
            string host = IllSingleton.Config.Database.Host;
            int port = IllSingleton.Config.Database.Port;
            return DBMySQLUtils.GetDBConnection(host, port, username, password);
        }
    }
}