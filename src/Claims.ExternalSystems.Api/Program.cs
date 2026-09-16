using Claims.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/registry/validate", (ClientValidationRequest request) =>
{
    var valid = request.ClientId.StartsWith("CLIENT-", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new ValidationResult(valid, valid ? null : "Client not found in Client Registry."));
});

app.MapPost("/api/policy/validate", (PolicyValidationRequest request) =>
{
    var valid = request.PolicyId.StartsWith("POL-", StringComparison.OrdinalIgnoreCase) && request.Amount <= 100000;
    return Results.Ok(new ValidationResult(valid, valid ? null : "Policy validation failed for this claim."));
});

app.MapPost("/api/rules/evaluate", (RulesRequest request) =>
{
    var review = request.Amount > 25000 || request.ClaimType.Equals("COMPLEX", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new FraudResult(true, review ? 70 : 10, review, review ? "Business rules require manual review." : null));
});

app.MapPost("/api/fraud/score", (FraudRequest request) =>
{
    var score = request.Amount > 50000 ? 80 : 15;
    var review = score >= 70;
    return Results.Ok(new FraudResult(!review, score, review, review ? "Fraud score requires manual review." : null));
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record ClientValidationRequest(string ClientId);
public sealed record PolicyValidationRequest(string PolicyId, string ClaimType, decimal Amount);
public sealed record RulesRequest(string ClaimType, decimal Amount);
public sealed record FraudRequest(string ClientId, decimal Amount, string ClaimType);
