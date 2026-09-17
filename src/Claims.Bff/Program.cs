using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Claims.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60 * 1024 * 1024);
builder.Services.AddHttpClient("ClaimsApi", client =>
    client.BaseAddress = new Uri(builder.Configuration["ClaimsApi:BaseUrl"] ?? "http://localhost:5101"));
builder.Services.AddHttpClient("Workflow", client =>
    client.BaseAddress = new Uri(builder.Configuration["Workflow:BaseUrl"] ?? "http://localhost:5103"));
builder.Services.AddHttpClient("PaymentGateway", client =>
    client.BaseAddress = new Uri(builder.Configuration["PaymentGateway:BaseUrl"] ?? "http://localhost:5104"));
builder.Services.AddHttpClient("Notifications", client =>
    client.BaseAddress = new Uri(builder.Configuration["NotificationService:BaseUrl"] ?? "http://localhost:5106"));
builder.Services.AddHttpClient("Registration", client => client.BaseAddress = new Uri(builder.Configuration["Registration:BaseUrl"] ?? "http://localhost:5108"));
builder.Services.AddHttpClient("Policy", client => client.BaseAddress = new Uri(builder.Configuration["Policy:BaseUrl"] ?? "http://localhost:5109"));
builder.Services.AddHttpClient("Rules", client => client.BaseAddress = new Uri(builder.Configuration["Rules:BaseUrl"] ?? "http://localhost:5110"));
builder.Services.AddHttpClient("Fraud", client => client.BaseAddress = new Uri(builder.Configuration["Fraud:BaseUrl"] ?? "http://localhost:5111"));
builder.Services.AddHttpClient("PaymentProvider", client => client.BaseAddress = new Uri(builder.Configuration["PaymentProvider:BaseUrl"] ?? "http://localhost:5112"));
builder.Services.AddHttpClient("DocumentProcessing", client => client.BaseAddress = new Uri(builder.Configuration["DocumentProcessing:BaseUrl"] ?? "http://localhost:5107"));
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5100", "https://localhost:5100")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("Frontend");

app.MapGet("/api/client/claims", async (IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Get, "/api/claims", null, ct));

app.MapGet("/api/client/claims/{id:guid}", async (Guid id, IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Get, $"/api/claims/{id}", null, ct));

app.MapGet("/api/client/summary", async (IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Get, "/api/claims/summary", null, ct));

app.MapGet("/api/client/reference-data", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var registrationTask = factory.CreateClient("Registration")
        .GetFromJsonAsync<List<ClientReferenceDto>>("/api/mock/clients", ct);
    var policyTask = factory.CreateClient("Policy")
        .GetFromJsonAsync<List<PolicyReferenceDto>>("/api/mock/policies", ct);

    await Task.WhenAll(registrationTask, policyTask);
    return Results.Ok(new ReferenceDataDto(
        registrationTask.Result ?? [],
        policyTask.Result ?? []));
});

app.MapPost("/api/client/claims", async (CreateClaimRequest model, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(model.ClientId) ||
        string.IsNullOrWhiteSpace(model.PolicyId) ||
        string.IsNullOrWhiteSpace(model.ClaimType) ||
        model.Amount <= 0)
    {
        return Results.BadRequest(new { error = "ClientId, PolicyId, ClaimType and a positive Amount are required." });
    }

    var client = factory.CreateClient("ClaimsApi");
    var response = await client.PostAsJsonAsync("/api/claims", model, ct);
    return await ForwardResponseAsync(response, ct);
});

app.MapPost("/api/client/claims/{id:guid}/documents", async (Guid id, HttpRequest request, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Multipart form-data is required." });

    var form = await request.ReadFormAsync(ct);
    if (form.Files.Count == 0)
        return Results.BadRequest(new { error = "At least one document is required." });

    using var content = new MultipartFormDataContent();
    foreach (var file in form.Files)
    {
        if (file.Length <= 0) continue;
        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(new { error = $"{file.FileName} exceeds the 10 MB per-file limit." });
        if (!IsAllowedDocument(file.FileName, file.ContentType))
            return Results.BadRequest(new { error = $"{file.FileName} is not a supported document type. Use PDF, JPG or PNG." });

        var stream = file.OpenReadStream();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        content.Add(streamContent, "files", file.FileName);
    }

    var response = await factory.CreateClient("ClaimsApi")
        .PostAsync($"/api/claims/{id}/documents", content, ct);
    return await ForwardResponseAsync(response, ct);
});

app.MapGet("/api/client/claims/{id:guid}/documents/{documentId:guid}/download", async (Guid id, Guid documentId, IHttpClientFactory factory, CancellationToken ct) =>
{
    using var response = await factory.CreateClient("ClaimsApi")
        .GetAsync($"/api/claims/{id}/documents/{documentId}/download", ct);

    if (!response.IsSuccessStatusCode)
        return await ForwardResponseAsync(response, ct);

    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
    var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
        ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
        ?? $"document-{documentId}";

    return Results.File(bytes, contentType, fileName);
});

app.MapPost("/api/client/claims/{id:guid}/start-workflow", async (Guid id, IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Post, $"/api/claims/{id}/requeue", null, ct));

app.MapPost("/api/client/claims/{id:guid}/decision", async (Guid id, AnalystDecisionRequest decision, IHttpClientFactory factory, CancellationToken ct) =>
{
    var workflowClient = factory.CreateClient("Workflow");
    var claimResponse = await factory.CreateClient("ClaimsApi").GetAsync($"/api/claims/{id}", ct);
    if (!claimResponse.IsSuccessStatusCode) return await ForwardResponseAsync(claimResponse, ct);
    var claim = await claimResponse.Content.ReadFromJsonAsync<ClaimDto>(cancellationToken: ct);
    if (claim is null) return Results.Problem("Claim not found.");

    if (!string.IsNullOrWhiteSpace(claim.WorkflowInstanceId))
    {
        var eventResponse = await workflowClient.PostAsJsonAsync(
            $"/api/workflows/{claim.WorkflowInstanceId}/analyst-decision",
            decision,
            ct);
        return await ForwardResponseAsync(eventResponse, ct);
    }

    // Seeded demo claims have no active Durable instance; keep the demo record operable by updating its status.
    var status = decision.Approved ? ClaimStatus.Approved : ClaimStatus.Rejected;
    var update = new
    {
        Status = status,
        Source = "ClaimsAnalyst",
        Note = decision.Note ?? (decision.Approved ? "Claim approved after manual review." : "Claim rejected after manual review.")
    };
    return await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Post, $"/api/claims/{id}/status", update, ct);
});

app.MapPost("/api/client/payments/{reference}/simulate-completion", async (string reference, IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("PaymentGateway"), HttpMethod.Post, $"/api/payments/{reference}/complete", null, ct));

app.MapGet("/api/client/notifications", async (IHttpClientFactory factory, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("Notifications"), HttpMethod.Get, "/api/notifications", null, ct));

app.MapGet("/api/client/services", async (IHttpClientFactory factory, IConfiguration config, CancellationToken ct) =>
{
    (string Name, HttpClient Client, string Path)[] checks =
    [
        ("Claims API", factory.CreateClient("ClaimsApi"), "/health"),
        ("Workflow", factory.CreateClient("Workflow"), "/api/health"),
        ("Payment Gateway", factory.CreateClient("PaymentGateway"), "/health"),
        ("Payment Provider", factory.CreateClient("PaymentProvider"), "/health"),
        ("Registration System", factory.CreateClient("Registration"), "/health"),
        ("Policy Manager", factory.CreateClient("Policy"), "/health"),
        ("Rules Engine", factory.CreateClient("Rules"), "/health"),
        ("Fraud Detection", factory.CreateClient("Fraud"), "/health"),
        ("Document Intelligence", factory.CreateClient("DocumentProcessing"), "/health"),
        ("Notifications", factory.CreateClient("Notifications"), "/api/health")
    ];

    var result = new List<object>();
    foreach (var (name, client, path) in checks)
    {
        try
        {
            using var response = await client.GetAsync(path, ct);
            result.Add(new { name, status = response.IsSuccessStatusCode ? "Healthy" : "Unavailable" });
        }
        catch
        {
            result.Add(new { name, status = "Unavailable" });
        }
    }
    result.Add(new { name = "Azure Service Bus", status = "Aspire emulator" });
    result.Add(new { name = "Blob Storage", status = "Aspire Azurite emulator" });
    return Results.Ok(result);
});

// Audit trail proxy endpoint
app.MapGet("/api/client/claims/audit", async (IHttpClientFactory factory, HttpContext http, CancellationToken ct) =>
    await ProxyAsync(factory.CreateClient("ClaimsApi"), HttpMethod.Get, $"/api/claims/audit{http.Request.QueryString}", null, ct));

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Claims.Bff" }));
app.MapGet("/", () => Results.Ok(new { service = "Claims.Bff", role = "Frontend API facade only", ui = "Claims.Web" }));
app.Run();

static async Task<IResult> ProxyAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
        request.Content = JsonContent.Create(body);
    var response = await client.SendAsync(request, ct);
    return await ForwardResponseAsync(response, ct);
}

static bool IsAllowedDocument(string fileName, string? contentType)
{
    var extension = Path.GetExtension(fileName);
    return (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        || (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) && string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
}

static async Task<IResult> ForwardResponseAsync(HttpResponseMessage response, CancellationToken ct)
{
    var body = await response.Content.ReadAsStringAsync(ct);
    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
    return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
}
