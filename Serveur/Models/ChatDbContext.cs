using Microsoft.EntityFrameworkCore;
using ChatServeur.Fundus;

namespace ChatServeur
{
    public class ChatDbContext : DbContext
    {
        public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
        {
        }

        public DbSet<FoSettings> FoSettings => Set<FoSettings>();
        public DbSet<FoAlert> FoAlerts => Set<FoAlert>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FoAlert>().HasIndex(a => new { a.SourceKey, a.PatientId }).IsUnique();
            modelBuilder.Entity<FoAlert>().HasIndex(a => a.NextAppointment);
            modelBuilder.Entity<FoAlert>().Ignore(a => a.EffectiveStatus);
        }

        public DbSet<ChatMessage> Messages => Set<ChatMessage>();
        public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
        public DbSet<SecureGroup> SecureGroups => Set<SecureGroup>();
        public DbSet<ServerConfig> ServerConfigs => Set<ServerConfig>();
        public DbSet<PatientLog> PatientLogs => Set<PatientLog>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<UserSetting> UserSettings => Set<UserSetting>();
        public DbSet<KnownUser> KnownUsers => Set<KnownUser>();
    }
}
