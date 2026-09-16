using Claims.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("ClaimsApi", client => client.BaseAddress = new Uri(builder.Configuration["ClaimsApi:BaseUrl"] ?? "http://localhost:5101"));
var app = builder.Build();

app.MapPost("/api/client/claims", async (CreateClaimRequest request, IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ClaimsApi").PostAsJsonAsync("/api/claims", request, ct);
    return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
});
app.MapGet("/api/client/claims", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ClaimsApi").GetAsync("/api/claims", ct);
    return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
});
app.MapGet("/api/client/claims/{id:guid}", async (Guid id, IHttpClientFactory factory, CancellationToken ct) =>
{
    var response = await factory.CreateClient("ClaimsApi").GetAsync($"/api/claims/{id}", ct);
    return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
});
app.MapPost("/api/client/claims/{id:guid}/decision", async (Guid id, DecisionRequest request, IHttpClientFactory factory, CancellationToken ct) =>
{
    var status = request.Approved ? ClaimStatus.Approved : ClaimStatus.Rejected;
    var payload = new { Status = status, Source = "ClaimsAnalyst", Note = request.Approved ? "Claim approved after manual review." : "Claim rejected after manual review." };
    var response = await factory.CreateClient("ClaimsApi").PostAsJsonAsync($"/api/claims/{id}/status", payload, ct);
    return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record DecisionRequest(bool Approved);
