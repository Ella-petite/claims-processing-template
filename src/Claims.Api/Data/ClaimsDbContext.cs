using Claims.Contracts.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

public sealed class ClaimEntity
{
    public Guid Id { get; set; }
    public string ClaimReference { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string PolicyId { get; set; } = "";
    public string ClaimType { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime IncidentDate { get; set; }
    public string Description { get; set; } = "";
    public ClaimStatus Status { get; set; }
    public string? PaymentReference { get; set; }
    public string DocumentReferencesJson { get; set; } = "[]";
    public List<ClaimHistoryEntity> History { get; set; } = [];
}

public sealed class ClaimHistoryEntity
{
    public long Id { get; set; }
    public Guid ClaimId { get; set; }
    public DateTimeOffset At { get; set; }
    public ClaimStatus Status { get; set; }
    public string Source { get; set; } = "";
    public string? Note { get; set; }
}

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<ClaimEntity> Claims => Set<ClaimEntity>();
    public DbSet<ClaimHistoryEntity> ClaimHistory => Set<ClaimHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClaimEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ClaimEntity>().HasMany(x => x.History).WithOne().HasForeignKey(x => x.ClaimId);
        modelBuilder.Entity<ClaimHistoryEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ClaimHistoryEntity>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ClaimEntity>().Property(x => x.Status).HasConversion<string>();
    }
}
