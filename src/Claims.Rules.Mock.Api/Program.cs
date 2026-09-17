using Claims.Contracts.Models;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/api/rules/evaluate", (RulesRequest request) =>
{
    var review = request.Amount > 25000m ||
                 (request.ClaimType.Equals("Death", StringComparison.OrdinalIgnoreCase) && request.Amount >= 100000m) ||
                 request.ClaimType.Equals("Complex", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new RulesResult(true, review,
        review ? "HighValueOrComplexClaim" : null,
        review ? "Claim requires Claims Analyst review under the configured demo rules." : null));
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Rules Engine (Mock)" }));
app.Run();
public sealed record RulesRequest(string ClaimType, decimal Amount);
