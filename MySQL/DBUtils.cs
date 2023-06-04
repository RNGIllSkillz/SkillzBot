using MySql.Data.MySqlClient;
using SkillzBot.Singleton;

namespace SkillzBot.MYSQL
{
    class DBUtils
    {
        private readonly static IllSingleton _singleton = IllSingleton.GetInstance(); 
        public static MySqlConnection GetDBConnection(string database, string username, string password)
        {
            string host = _singleton.MySQL_IP;
            int port = _singleton.MySQL_Port;
            return DBMySQLUtils.GetDBConnection(host, port, database, username, password);
        }
        public static MySqlConnection GetDBConnection(string username, string password)
        {
            string host = _singleton.MySQL_IP;
            int port = _singleton.MySQL_Port;
            return DBMySQLUtils.GetDBConnection(host, port, username, password);
        }
    }
}
