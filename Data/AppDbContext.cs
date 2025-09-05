using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Play929Backend.Models;

namespace Play929Backend.Data
{
    public class AppDbContext : DbContext
    {
        private IDbContextTransaction _currentTransaction;

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public IDbContextTransaction GetCurrentTransaction() => _currentTransaction;

        public bool HasActiveTransaction => _currentTransaction != null;

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            if (_currentTransaction != null) return null;

            _currentTransaction = await Database.BeginTransactionAsync();
            return _currentTransaction;
        }

        public async Task CommitTransactionAsync()
        {
            try
            {
                await SaveChangesAsync();
                await _currentTransaction?.CommitAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.DisposeAsync();
                    _currentTransaction = null;
                }
            }
        }

        public async Task RollbackTransactionAsync()
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync();
                }
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.DisposeAsync();
                    _currentTransaction = null;
                }
            }
        }

        // DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<FinancialAudit> FinancialAudits { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }
        public DbSet<SecurityLog> SecurityLogs { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<GameLaunchToken> GameLaunchTokens { get; set; }
        public DbSet<AccountVerificationToken> AccountVerificationTokens { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Decimal precision
            modelBuilder.Entity<Transaction>().Property(t => t.Amount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Transaction>().Property(t => t.BeforeBalance).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Transaction>().Property(t => t.AfterBalance).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Wallet>().Property(w => w.Balance).HasColumnType("decimal(18,2)");

            // Unique indexes
            modelBuilder.Entity<Wallet>().HasIndex(w => w.WalletAddress).IsUnique();
            modelBuilder.Entity<UserRole>().HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();

            // Relationships
            modelBuilder.Entity<Wallet>()
                .HasOne(w => w.User)
                .WithOne(u => u.Wallet)
                .HasForeignKey<Wallet>(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Wallet)
                .WithMany()
                .HasForeignKey(t => t.WalletId)
                .OnDelete(DeleteBehavior.Restrict);

             modelBuilder.Entity<AccountVerificationToken>()
                .HasOne(t => t.User)
                .WithMany(u => u.AccountVerificationTokens) 
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FinancialAudit>()
                .HasOne(f => f.Wallet)
                .WithMany()
                .HasForeignKey(f => f.WalletId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GameSession>()
                .HasMany(s => s.LaunchTokens)
                .WithOne(t => t.GameSession)
                .HasForeignKey(t => t.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Default timestamps
        modelBuilder.Entity<User>().Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        modelBuilder.Entity<User>().Property(u => u.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        modelBuilder.Entity<Wallet>().Property(w => w.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        modelBuilder.Entity<Wallet>().Property(w => w.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Role name lowercase
            modelBuilder.Entity<Role>()
                .Property(r => r.Name)
                .HasConversion(
                    v => v.ToLower(),
                    v => v
                );
        }
    }
}
