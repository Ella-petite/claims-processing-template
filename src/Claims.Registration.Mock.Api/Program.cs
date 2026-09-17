using Claims.Contracts.Models;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var clients = new Dictionary<string, (string Name, string Tier)>(StringComparer.OrdinalIgnoreCase)
{
    ["CLIENT-001"] = ("Alex Morgan", "Standard"),
    ["CLIENT-002"] = ("Demo Policyholder", "Premium"),
    ["CLIENT-003"] = ("Jordan Smith", "Standard"),
    ["CLIENT-004"] = ("Sam Daniels", "Premium")
};
app.MapPost("/api/registry/validate", (ClientValidationRequest request) =>
    clients.TryGetValue(request.ClientId, out var client)
        ? Results.Ok(new ClientValidationResult(true, client.Name, client.Tier))
        : Results.Ok(new ClientValidationResult(false, null, null, "Client not found in Client Registry.")));
app.MapGet("/api/mock/clients", () => Results.Ok(clients.Select(x => new { clientId = x.Key, clientName = x.Value.Name, tier = x.Value.Tier })));
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Registration System (Mock)" }));
app.Run();
public sealed record ClientValidationRequest(string ClientId);
