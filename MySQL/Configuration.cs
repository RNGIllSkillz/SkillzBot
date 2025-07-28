namespace SkillzBot.MySQL
{
    public class DatabaseConfiguration
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 3306;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public int ConnectionTimeout { get; set; } = 30;
        public int CommandTimeout { get; set; } = 30;
        public int MaxPoolSize { get; set; } = 100;
        public int MinPoolSize { get; set; } = 0;
        public bool Pooling { get; set; } = true;
        public string CharacterSet { get; set; } = "utf8mb4";
    }
}
