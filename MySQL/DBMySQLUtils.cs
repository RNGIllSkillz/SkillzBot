using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace SkillzBot.MYSQL
{
    class DBMySQLUtils
    {
        public static MySqlConnection GetDBConnection(string host, int port, string database, string username, string password)
        {
            String connString = "Server=" + host + ";Database=" + database
                + ";port=" + port + ";User Id=" + username + ";password=" + password + ";CharSet=utf8mb4";

            MySqlConnection conn = new MySqlConnection(connString);

            return conn;
        }
        public static MySqlConnection GetDBConnection(string host, int port, string username, string password)
        {
            String connString = "Server=" + host + ";port=" + port + ";User Id=" + username + ";password=" + password + ";CharSet=utf8mb4";

            MySqlConnection conn = new MySqlConnection(connString);

            return conn;
        }
    }
}
