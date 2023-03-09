using MySql.Data.MySqlClient;

namespace SkillzBot.MYSQL
{
    class DBUtils
    {
        public static MySqlConnection GetDBConnection(string database, string username, string password)
        {
            string host = "127.0.0.1";
            int port = 3306;
            return DBMySQLUtils.GetDBConnection(host, port, database, username, password);
        }
        public static MySqlConnection GetDBConnection(string username, string password)
        {
            string host = "127.0.0.1";
            int port = 3306;
            return DBMySQLUtils.GetDBConnection(host, port, username, password);
        }
    }
}
