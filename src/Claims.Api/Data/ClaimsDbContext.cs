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
    public string? WorkflowInstanceId { get; set; }
    public List<ClaimHistoryEntity> History { get; set; } = [];
    public List<ClaimDocumentEntity> Documents { get; set; } = [];
}

public sealed class ClaimHistoryEntity
{
    public long Id { get; set; }
    public Guid ClaimId { get; set; }
    public DateTimeOffset At { get; set; }
    public ClaimStatus Status { get; set; }
    public string Source { get; set; } = "";
    public string? Note { get; set; }
    public string CorrelationId { get; set; } = "";
}

public sealed class ClaimDocumentEntity
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string BlobName { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
    public string ProcessingStatus { get; set; } = "Uploaded";
    public string ExtractedFieldsJson { get; set; } = "{}";
}


public sealed class OutboxMessageEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Subject { get; set; } = "";
    public string Payload { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<ClaimEntity> Claims => Set<ClaimEntity>();
    public DbSet<ClaimHistoryEntity> ClaimHistory => Set<ClaimHistoryEntity>();
    public DbSet<ClaimDocumentEntity> ClaimDocuments => Set<ClaimDocumentEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClaimEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ClaimEntity>().HasMany(x => x.History).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClaimEntity>().HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ClaimHistoryEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ClaimHistoryEntity>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ClaimEntity>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ClaimDocumentEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OutboxMessageEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OutboxMessageEntity>().HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
    }
}
