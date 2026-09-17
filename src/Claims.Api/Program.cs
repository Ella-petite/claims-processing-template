using System.Text.Json;
using Azure.Storage.Blobs;
using Claims.Api.Data;
using Claims.Api.Services;
using Claims.Contracts.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60 * 1024 * 1024);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ClaimsDb") ?? "Data Source=claims.db"));
builder.Services.AddSingleton<IEventPublisher, ServiceBusEventPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHttpClient("DocumentProcessing", client =>
    client.BaseAddress = new Uri(builder.Configuration["DocumentProcessing:BaseUrl"] ?? "http://localhost:5107"));

var blobConnection = builder.Configuration["BlobStorage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(blobConnection))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnection));
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

await InitialiseDatabaseAsync(app.Services);

app.MapGet("/api/claims", async (ClaimsDbContext db, CancellationToken ct) =>
{
    var claims = await db.Claims
        .Include(x => x.History)
        .Include(x => x.Documents)
        .OrderByDescending(x => x.ClaimReference)
        .ToListAsync(ct);
    return Results.Ok(claims.Select(ToDto));
});

app.MapGet("/api/claims/{id:guid}", async (Guid id, ClaimsDbContext db, CancellationToken ct) =>
{
    var claim = await db.Claims
        .Include(x => x.History)
        .Include(x => x.Documents)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    return claim is null ? Results.NotFound(new { error = "Claim not found." }) : Results.Ok(ToDto(claim));
});

app.MapPost("/api/claims", async (
    CreateClaimRequest request,
    ClaimsDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.ClientId) ||
        string.IsNullOrWhiteSpace(request.PolicyId) ||
        string.IsNullOrWhiteSpace(request.ClaimType) ||
        request.Amount <= 0)
    {
        return Results.BadRequest(new { error = "ClientId, PolicyId, ClaimType and a positive Amount are required." });
    }

    var now = DateTimeOffset.UtcNow;
    var nextNumber = (await db.Claims.CountAsync(ct)) + 1;
    var claim = new ClaimEntity
    {
        Id = Guid.NewGuid(),
        ClaimReference = $"CL-{nextNumber:000}",
        ClientId = request.ClientId.Trim(),
        PolicyId = request.PolicyId.Trim(),
        ClaimType = request.ClaimType.Trim(),
        Amount = request.Amount,
        IncidentDate = request.IncidentDate,
        Description = request.Description?.Trim() ?? string.Empty,
        Status = ClaimStatus.Submitted
    };

    var correlationId = Guid.NewGuid().ToString("N");
    claim.History.Add(new ClaimHistoryEntity
    {
        ClaimId = claim.Id,
        At = now,
        Status = ClaimStatus.Submitted,
        Source = "Claims.Api",
        Note = "Claim received from submission channel.",
        CorrelationId = correlationId
    });

    db.Claims.Add(claim);
    db.OutboxMessages.Add(CreateOutboxMessage(
        "claim.submitted",
        new ClaimSubmittedEvent(claim.Id, claim.ClientId, claim.PolicyId, claim.ClaimType, claim.Amount, now, correlationId),
        now));
    await db.SaveChangesAsync(ct);

    var saved = await db.Claims.Include(x => x.History).Include(x => x.Documents).SingleAsync(x => x.Id == claim.Id, ct);
    return Results.Created($"/api/claims/{claim.Id}", ToDto(saved));
});

app.MapPost("/api/claims/{id:guid}/requeue", async (
    Guid id,
    ClaimsDbContext db,
    CancellationToken ct) =>
{
    var claim = await db.Claims
        .Include(x => x.History)
        .Include(x => x.Documents)
        .SingleOrDefaultAsync(x => x.Id == id, ct);

    if (claim is null) return Results.NotFound(new { error = "Claim not found." });
    if (claim.Status != ClaimStatus.Submitted)
        return Results.BadRequest(new { error = "Only a submitted claim can be requeued in the demo." });

    var now = DateTimeOffset.UtcNow;
    var correlationId = Guid.NewGuid().ToString("N");
    claim.History.Add(new ClaimHistoryEntity
    {
        ClaimId = claim.Id,
        At = now,
        Status = claim.Status,
        Source = "Demo",
        Note = "claim.submitted event requeued for workflow demonstration.",
        CorrelationId = correlationId
    });
    db.OutboxMessages.Add(CreateOutboxMessage(
        "claim.submitted",
        new ClaimSubmittedEvent(claim.Id, claim.ClientId, claim.PolicyId, claim.ClaimType, claim.Amount, now, correlationId),
        now));
    await db.SaveChangesAsync(ct);

    return Results.Ok(ToDto(claim));
});

app.MapPost("/api/claims/{id:guid}/status", async (
    Guid id,
    UpdateClaimStatusRequest request,
    ClaimsDbContext db,
    CancellationToken ct) =>
{
    var claim = await db.Claims
        .Include(x => x.History)
        .Include(x => x.Documents)
        .SingleOrDefaultAsync(x => x.Id == id, ct);

    if (claim is null) return Results.NotFound(new { error = "Claim not found." });
    if (string.IsNullOrWhiteSpace(request.Source)) return Results.BadRequest(new { error = "Source is required." });

    var now = DateTimeOffset.UtcNow;
    var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
        ? Guid.NewGuid().ToString("N")
        : request.CorrelationId;

    claim.Status = request.Status;
    claim.PaymentReference = request.PaymentReference ?? claim.PaymentReference;
    claim.WorkflowInstanceId = request.WorkflowInstanceId ?? claim.WorkflowInstanceId;
    claim.History.Add(new ClaimHistoryEntity
    {
        ClaimId = id,
        At = now,
        Status = request.Status,
        Source = request.Source,
        Note = request.Note,
        CorrelationId = correlationId
    });
    db.OutboxMessages.Add(CreateOutboxMessage(
        "claim.updated",
        new ClaimUpdatedEvent(claim.Id, claim.ClaimReference, claim.Status, request.Source, request.Note, now, correlationId, claim.PaymentReference),
        now));
    await db.SaveChangesAsync(ct);

    return Results.Ok(ToDto(claim));
});

app.MapPost("/api/claims/{id:guid}/documents", async (
    Guid id,
    HttpRequest request,
    ClaimsDbContext db,
    IHttpClientFactory httpClientFactory,
    BlobServiceClient? blobServiceClient,
    CancellationToken ct) =>
{
    var claim = await db.Claims.Include(x => x.Documents).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (claim is null) return Results.NotFound(new { error = "Claim not found." });
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Multipart form-data is required." });

    var form = await request.ReadFormAsync(ct);
    if (form.Files.Count == 0) return Results.BadRequest(new { error = "At least one file is required." });

    var uploaded = new List<ClaimDocumentDto>();
    foreach (var file in form.Files)
    {
        if (file.Length <= 0) continue;
        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { error = $"{file.FileName} exceeds the 10 MB document limit." });
        if (!IsAllowedDocument(file.FileName, file.ContentType))
            return Results.BadRequest(new { error = $"{file.FileName} is not a supported document type. Use PDF, JPG or PNG." });

        var documentId = Guid.NewGuid();
        var blobName = $"{claim.Id:N}/{documentId:N}-{SanitiseFileName(file.FileName)}";

        if (blobServiceClient is not null)
        {
            var container = blobServiceClient.GetBlobContainerClient("claim-documents");
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient(blobName);
            await using var fileStream = file.OpenReadStream();
            await blob.UploadAsync(fileStream, overwrite: true, cancellationToken: ct);
        }
        else
        {
            var localPath = Path.Combine(AppContext.BaseDirectory, "App_Data", "documents", blobName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await using var destination = File.Create(localPath);
            await file.CopyToAsync(destination, ct);
        }

        var document = new ClaimDocumentEntity
        {
            Id = documentId,
            ClaimId = claim.Id,
            FileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            Size = file.Length,
            BlobName = blobName,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessingStatus = "Uploaded"
        };

        try
        {
            using var content = new MultipartFormDataContent();
            await using var fileStreamForExtraction = file.OpenReadStream();
            content.Add(new StreamContent(fileStreamForExtraction), "file", file.FileName);
            var extractionResponse = await httpClientFactory.CreateClient("DocumentProcessing").PostAsync("/api/document-intelligence/extract", content, ct);
            if (extractionResponse.IsSuccessStatusCode)
            {
                var extraction = await extractionResponse.Content.ReadFromJsonAsync<DocumentExtractionResult>(cancellationToken: ct);
                if (extraction is not null)
                {
                    document.ProcessingStatus = extraction.Success ? "Processed" : "ReviewRequired";
                    document.ExtractedFieldsJson = JsonSerializer.Serialize(extraction.ExtractedFields);
                }
            }
        }
        catch (Exception ex)
        {
            document.ProcessingStatus = "ProcessingFailed";
            app.Logger.LogWarning(ex, "Document processing failed for {FileName}", file.FileName);
        }

        db.ClaimDocuments.Add(document);
        claim.History.Add(new ClaimHistoryEntity
        {
            ClaimId = claim.Id,
            At = document.UploadedAt,
            Status = claim.Status,
            Source = "DocumentProcessing",
            Note = $"Document uploaded: {file.FileName}. Processing status: {document.ProcessingStatus}.",
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        db.OutboxMessages.Add(CreateOutboxMessage(
            "claim.updated",
            new ClaimUpdatedEvent(claim.Id, claim.ClaimReference, claim.Status, "DocumentProcessing",
                $"Document uploaded: {file.FileName}. Processing status: {document.ProcessingStatus}.",
                document.UploadedAt, Guid.NewGuid().ToString("N"), claim.PaymentReference),
            document.UploadedAt));
        await db.SaveChangesAsync(ct);
        uploaded.Add(ToDocumentDto(document));
    }

    return Results.Ok(uploaded);
});

app.MapGet("/api/claims/{id:guid}/documents/{documentId:guid}/download", async (
    Guid id,
    Guid documentId,
    ClaimsDbContext db,
    BlobServiceClient? blobServiceClient,
    CancellationToken ct) =>
{
    var document = await db.ClaimDocuments.SingleOrDefaultAsync(x => x.Id == documentId && x.ClaimId == id, ct);
    if (document is null) return Results.NotFound();

    if (blobServiceClient is not null)
    {
        var blob = blobServiceClient.GetBlobContainerClient("claim-documents").GetBlobClient(document.BlobName);
        if (!await blob.ExistsAsync(ct)) return Results.NotFound();
        var stream = await blob.OpenReadAsync(cancellationToken: ct);
        return Results.File(stream, document.ContentType, document.FileName);
    }

    var localPath = Path.Combine(AppContext.BaseDirectory, "App_Data", "documents", document.BlobName.Replace('/', Path.DirectorySeparatorChar));
    return File.Exists(localPath)
        ? Results.File(localPath, document.ContentType, document.FileName)
        : Results.NotFound();
});

app.MapGet("/api/claims/summary", async (ClaimsDbContext db, CancellationToken ct) =>
{
    var claims = await db.Claims.AsNoTracking().ToListAsync(ct);
    return Results.Ok(new
    {
        total = claims.Count,
        submitted = claims.Count(x => x.Status == ClaimStatus.Submitted),
        underReview = claims.Count(x => x.Status == ClaimStatus.UnderReview),
        approved = claims.Count(x => x.Status == ClaimStatus.Approved),
        paymentPending = claims.Count(x => x.Status == ClaimStatus.PaymentPending),
        paid = claims.Count(x => x.Status == ClaimStatus.Paid),
        rejected = claims.Count(x => x.Status == ClaimStatus.Rejected)
    });
});

app.MapGet("/health", async (ClaimsDbContext db, CancellationToken ct) =>
{
    var canRead = await db.Database.CanConnectAsync(ct);
    return canRead ? Results.Ok(new { status = "ok", service = "Claims.Api" }) : Results.StatusCode(503);
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Claims.Api",
    role = "Claims backend / system of record",
    swagger = "/swagger",
    health = "/health"
}));

app.Run();

static async Task InitialiseDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedDemoClaimsAsync(db);
}

static ClaimDto ToDto(ClaimEntity entity) => new(
    entity.Id,
    entity.ClaimReference,
    entity.ClientId,
    entity.PolicyId,
    entity.ClaimType,
    entity.Amount,
    entity.IncidentDate,
    entity.Description,
    entity.Status,
    entity.PaymentReference,
    entity.WorkflowInstanceId,
    entity.History.OrderBy(h => h.At).Select(h => new ClaimHistoryDto(h.At, h.Status, h.Source, h.Note)).ToList(),
    entity.Documents.OrderBy(d => d.UploadedAt).Select(ToDocumentDto).ToList());

static ClaimDocumentDto ToDocumentDto(ClaimDocumentEntity entity) => new(
    entity.Id,
    entity.FileName,
    entity.ContentType,
    entity.Size,
    entity.UploadedAt,
    entity.ProcessingStatus,
    JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ExtractedFieldsJson) ?? []);

static bool IsAllowedDocument(string fileName, string? contentType)
{
    var extension = Path.GetExtension(fileName);
    return (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
}

static string SanitiseFileName(string fileName)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch));
}

static async Task SeedDemoClaimsAsync(ClaimsDbContext db)
{
    if (await db.Claims.AnyAsync()) return;

    var now = DateTimeOffset.UtcNow;
    var claims = new[]
    {
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ClaimReference = "CL-001",
            ClientId = "CLIENT-001",
            PolicyId = "POL-001",
            ClaimType = "Accidental damage",
            Amount = 18500m,
            IncidentDate = DateTime.UtcNow.AddDays(-8),
            Description = "Vehicle damage following a collision.",
            Status = ClaimStatus.Paid,
            PaymentReference = "PAY-DEMO-001",
            History =
            [
                new ClaimHistoryEntity { At = now.AddHours(-30), Status = ClaimStatus.Submitted, Source = "Claims.Api", Note = "Claim received.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-29), Status = ClaimStatus.ValidatingCustomer, Source = "RegistrationSystem", Note = "Customer validated.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-28), Status = ClaimStatus.ValidatingPolicy, Source = "PolicyManager", Note = "Policy validated.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-27), Status = ClaimStatus.CheckingRules, Source = "RulesEngine", Note = "Rules passed.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-27), Status = ClaimStatus.CheckingFraud, Source = "FraudDetection", Note = "Low risk.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-25), Status = ClaimStatus.Approved, Source = "Workflow", Note = "Claim approved for payment.", CorrelationId = "demo-001" },
                new ClaimHistoryEntity { At = now.AddHours(-24), Status = ClaimStatus.PaymentPending, Source = "PaymentGateway", Note = "Payment submitted.", CorrelationId = "demo-001", },
                new ClaimHistoryEntity { At = now.AddHours(-23), Status = ClaimStatus.Paid, Source = "PaymentGateway", Note = "Payment completed.", CorrelationId = "demo-001" }
            ],
            Documents =
            [
                new ClaimDocumentEntity { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), FileName = "Incident photos.pdf", ContentType = "application/pdf", Size = 156000, BlobName = "demo/incident-photos.pdf", UploadedAt = now.AddDays(-8), ProcessingStatus = "Processed", ExtractedFieldsJson = "{\"documentType\":\"Incident evidence\"}" }
            ]
        },
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            ClaimReference = "CL-002",
            ClientId = "CLIENT-002",
            PolicyId = "POL-002",
            ClaimType = "Death",
            Amount = 250000m,
            IncidentDate = DateTime.UtcNow.AddDays(-3),
            Description = "Life cover claim submitted following the policyholder's passing.",
            Status = ClaimStatus.UnderReview,
            History =
            [
                new ClaimHistoryEntity { At = now.AddHours(-6), Status = ClaimStatus.Submitted, Source = "Claims.Api", Note = "Claim received.", CorrelationId = "demo-002" },
                new ClaimHistoryEntity { At = now.AddHours(-5), Status = ClaimStatus.ValidatingCustomer, Source = "RegistrationSystem", Note = "Customer validated.", CorrelationId = "demo-002" },
                new ClaimHistoryEntity { At = now.AddHours(-4), Status = ClaimStatus.ValidatingPolicy, Source = "PolicyManager", Note = "Policy validated.", CorrelationId = "demo-002" },
                new ClaimHistoryEntity { At = now.AddHours(-3), Status = ClaimStatus.CheckingRules, Source = "RulesEngine", Note = "High-value claim requires review.", CorrelationId = "demo-002" },
                new ClaimHistoryEntity { At = now.AddHours(-2), Status = ClaimStatus.CheckingFraud, Source = "FraudDetection", Note = "Review threshold reached.", CorrelationId = "demo-002" },
                new ClaimHistoryEntity { At = now.AddHours(-1), Status = ClaimStatus.UnderReview, Source = "Workflow", Note = "Claims Analyst review required.", CorrelationId = "demo-002" }
            ],
            Documents =
            [
                new ClaimDocumentEntity { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), FileName = "Death Certificate.pdf", ContentType = "application/pdf", Size = 284000, BlobName = "demo/death-certificate.pdf", UploadedAt = now.AddDays(-3), ProcessingStatus = "Processed", ExtractedFieldsJson = "{\"documentType\":\"Death Certificate\",\"deceasedName\":\"Demo Policyholder\"}" },
                new ClaimDocumentEntity { Id = Guid.Parse("20000000-0000-0000-0000-000000000003"), FileName = "Supporting Document.pdf", ContentType = "application/pdf", Size = 91000, BlobName = "demo/supporting.pdf", UploadedAt = now.AddDays(-3), ProcessingStatus = "Processed", ExtractedFieldsJson = "{\"documentType\":\"Supporting evidence\"}" }
            ]
        },
        new ClaimEntity
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            ClaimReference = "CL-003",
            ClientId = "CLIENT-003",
            PolicyId = "POL-003",
            ClaimType = "Hospitalisation",
            Amount = 42000m,
            IncidentDate = DateTime.UtcNow.AddDays(-1),
            Description = "Hospitalisation benefit claim awaiting processing.",
            Status = ClaimStatus.Submitted,
            History = [new ClaimHistoryEntity { At = now.AddMinutes(-45), Status = ClaimStatus.Submitted, Source = "Claims.Api", Note = "Claim received.", CorrelationId = "demo-003" }]
        }
    };

    db.Claims.AddRange(claims);
    await db.SaveChangesAsync();
}

static OutboxMessageEntity CreateOutboxMessage<T>(string subject, T payload, DateTimeOffset createdAt) => new()
{
    Id = Guid.NewGuid(),
    CreatedAt = createdAt,
    Subject = subject,
    Payload = JsonSerializer.Serialize(payload)
};

public sealed record UpdateClaimStatusRequest(
    ClaimStatus Status,
    string Source,
    string? Note = null,
    string? PaymentReference = null,
    string? WorkflowInstanceId = null,
    string? CorrelationId = null);
