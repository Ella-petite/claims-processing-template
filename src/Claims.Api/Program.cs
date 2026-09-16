using System.Text.Json;
using Claims.Api.Data;
using Claims.Api.Services;
using Claims.Contracts.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<ClaimsDbContext>(o => o.UseSqlite("Data Source=claims.db"));
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    db.Database.EnsureCreated();
    SeedDemoClaims(db);
}

app.MapGet("/api/claims", async (ClaimsDbContext db, CancellationToken ct) =>
{
    var claims = await db.Claims.Include(x => x.History).OrderBy(x => x.ClaimReference).ToListAsync(ct);
    return Results.Ok(claims.Select(ToDto));
});

app.MapPost("/api/claims", async (CreateClaimRequest request, ClaimsDbContext db, IEventPublisher publisher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.PolicyId) || request.Amount <= 0)
        return Results.BadRequest(new { error = "ClientId, PolicyId and a positive Amount are required." });

    var now = DateTimeOffset.UtcNow;
    var nextReference = await db.Claims.CountAsync(ct) + 1;
    var claim = new ClaimEntity
    {
        Id = Guid.NewGuid(), ClaimReference = $"CL-{nextReference:000}", ClientId = request.ClientId, PolicyId = request.PolicyId, ClaimType = request.ClaimType,
        Amount = request.Amount, IncidentDate = request.IncidentDate, Description = request.Description,
        Status = ClaimStatus.Submitted, DocumentReferencesJson = JsonSerializer.Serialize(request.DocumentReferences ?? [])
    };
    claim.History.Add(new ClaimHistoryEntity { ClaimId = claim.Id, At = now, Status = ClaimStatus.Submitted, Source = "ClaimsApi", Note = "Claim created" });
    db.Claims.Add(claim);
    await db.SaveChangesAsync(ct);

    await publisher.PublishAsync("claim.submitted", new ClaimSubmittedEvent(claim.Id, claim.ClientId, claim.PolicyId, claim.ClaimType, claim.Amount, now), ct);
    return Results.Created($"/api/claims/{claim.Id}", ToDto(claim));
});

app.MapGet("/api/claims/{id:guid}", async (Guid id, ClaimsDbContext db, CancellationToken ct) =>
{
    var claim = await db.Claims.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == id, ct);
    return claim is null ? Results.NotFound() : Results.Ok(ToDto(claim));
});

app.MapPost("/api/claims/{id:guid}/status", async (Guid id, UpdateStatusRequest request, ClaimsDbContext db, CancellationToken ct) =>
{
    var claim = await db.Claims.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (claim is null) return Results.NotFound();
    claim.Status = request.Status;
    claim.PaymentReference = request.PaymentReference ?? claim.PaymentReference;
    claim.History.Add(new ClaimHistoryEntity { ClaimId = id, At = DateTimeOffset.UtcNow, Status = request.Status, Source = request.Source, Note = request.Note });
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToDto(claim));
});

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dev/workflow/{id:guid}", async (Guid id, IHttpClientFactory factory, IConfiguration config, ClaimsDbContext db, CancellationToken ct) =>
    {
        var claim = await db.Claims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (claim is null) return Results.NotFound();
        var url = $"{config["Workflow:BaseUrl"] ?? "http://localhost:5103"}/api/workflows/start";
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(url, new ClaimSubmittedEvent(claim.Id, claim.ClientId, claim.PolicyId, claim.ClaimType, claim.Amount, DateTimeOffset.UtcNow), ct);
        return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
    });
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Claims Backend API",
    description = "Backend claim management and persistence service.",
    swagger = "/swagger",
    health = "/health"
}));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

static ClaimDto ToDto(ClaimEntity x) => new(
    x.Id, x.ClientId, x.PolicyId, x.ClaimType, x.Amount, x.IncidentDate, x.Description, x.Status, x.PaymentReference,
    x.History.OrderByDescending(h => h.At).Select(h => new ClaimHistoryDto(h.At, h.Status, h.Source, h.Note)).ToList(),
    JsonSerializer.Deserialize<List<string>>(x.DocumentReferencesJson) ?? [], x.ClaimReference);

static void SeedDemoClaims(ClaimsDbContext db)
{
    if (db.Claims.Any()) return;

    var now = DateTimeOffset.UtcNow;
    var claims = new[]
    {
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), ClaimReference = "CL-001", ClientId = "CLIENT-001", PolicyId = "POL-001",
            ClaimType = "Accidental damage", Amount = 18500m, IncidentDate = DateTime.UtcNow.AddDays(-8),
            Description = "Vehicle damage following a collision on the M4.", Status = ClaimStatus.Paid,
            PaymentReference = "PAY-1001", DocumentReferencesJson = JsonSerializer.Serialize(new[] { "Incident photos.pdf", "Repair estimate.pdf" }),
            History = [
                new ClaimHistoryEntity { At = now.AddHours(-30), Status = ClaimStatus.Submitted, Source = "ClaimsApi", Note = "Claim received" },
                new ClaimHistoryEntity { At = now.AddHours(-28), Status = ClaimStatus.Approved, Source = "Workflow", Note = "All automated checks passed" },
                new ClaimHistoryEntity { At = now.AddHours(-24), Status = ClaimStatus.PaymentPending, Source = "PaymentGateway", Note = "Payment submitted" },
                new ClaimHistoryEntity { At = now.AddHours(-20), Status = ClaimStatus.Paid, Source = "PaymentGateway", Note = "Payment completed" }
            ]
        },
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), ClaimReference = "CL-002", ClientId = "CLIENT-002", PolicyId = "POL-002",
            ClaimType = "Death", Amount = 250000m, IncidentDate = DateTime.UtcNow.AddDays(-3),
            Description = "Life cover claim submitted following the policyholder's passing.", Status = ClaimStatus.UnderReview,
            DocumentReferencesJson = JsonSerializer.Serialize(new[] { "Death Certificate.pdf", "Supporting Document.pdf" }),
            History = [
                new ClaimHistoryEntity { At = now.AddHours(-6), Status = ClaimStatus.Submitted, Source = "ClaimsApi", Note = "Claim received" },
                new ClaimHistoryEntity { At = now.AddHours(-5), Status = ClaimStatus.ValidatingCustomer, Source = "RegistrationSystem", Note = "Customer validated" },
                new ClaimHistoryEntity { At = now.AddHours(-4), Status = ClaimStatus.ValidatingPolicy, Source = "PolicyManager", Note = "Policy validated" },
                new ClaimHistoryEntity { At = now.AddHours(-3), Status = ClaimStatus.UnderReview, Source = "Rules/Fraud", Note = "Manual review required" }
            ]
        },
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), ClaimReference = "CL-003", ClientId = "CLIENT-003", PolicyId = "POL-003",
            ClaimType = "Hospitalisation", Amount = 42000m, IncidentDate = DateTime.UtcNow.AddDays(-1),
            Description = "Hospitalisation benefit claim awaiting validation.", Status = ClaimStatus.Submitted,
            DocumentReferencesJson = JsonSerializer.Serialize(new[] { "Hospital invoice.pdf" }),
            History = [new ClaimHistoryEntity { At = now.AddMinutes(-45), Status = ClaimStatus.Submitted, Source = "ClaimsApi", Note = "Claim received" }]
        }
    };

    db.Claims.AddRange(claims);
    db.SaveChanges();
}

public sealed record UpdateStatusRequest(ClaimStatus Status, string Source, string? Note = null, string? PaymentReference = null);
