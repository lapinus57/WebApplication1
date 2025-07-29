using Microsoft.EntityFrameworkCore;

namespace ChatServeur
{
    public class ChatDbContext : DbContext
    {
        public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
        {
        }

        public DbSet<ChatMessage> Messages => Set<ChatMessage>();
        public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
        public DbSet<SecureGroup> SecureGroups => Set<SecureGroup>();
        public DbSet<ServerConfig> ServerConfigs => Set<ServerConfig>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    }
}
