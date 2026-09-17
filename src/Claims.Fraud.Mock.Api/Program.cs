using Claims.Contracts.Models;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/api/fraud/score", (FraudRequest request) =>
{
    var score = request.Amount >= 50000m ? 75 : request.ClaimType.Equals("Death", StringComparison.OrdinalIgnoreCase) ? 55 : 15;
    var review = score >= 70;
    return Results.Ok(new FraudResult(!review, score, review,
        review ? "Fraud risk score requires manual review." : "Low risk."));
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Fraud Detection (Mock)" }));
app.Run();
public sealed record FraudRequest(string ClientId, decimal Amount, string ClaimType);
