using Claims.Contracts.Models;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var policies = new Dictionary<string, (string Number, string Plan, decimal Limit, decimal Excess)>(StringComparer.OrdinalIgnoreCase)
{
    ["POL-001"] = ("POL-001", "Motor Plus", 500000m, 5000m),
    ["POL-002"] = ("POL-002", "Life Secure", 1000000m, 0m),
    ["POL-003"] = ("POL-003", "Health Cover", 250000m, 10000m),
    ["POL-004"] = ("POL-004", "Disability Protect", 800000m, 0m)
};
app.MapPost("/api/policy/validate", (PolicyValidationRequest request) =>
{
    if (!policies.TryGetValue(request.PolicyId, out var policy))
        return Results.Ok(new PolicyValidationResult(false, null, null, 0, 0, "Policy not found in Policy Manager."));
    var valid = request.Amount <= policy.Limit;
    return Results.Ok(new PolicyValidationResult(valid, policy.Number, policy.Plan, policy.Limit, policy.Excess,
        valid ? null : "Claim amount exceeds policy coverage limit."));
});
app.MapGet("/api/mock/policies", () => Results.Ok(policies.Select(x => new { policyId = x.Key, number = x.Value.Number, plan = x.Value.Plan, coverageLimit = x.Value.Limit, excess = x.Value.Excess })));
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Policy Manager (Mock)" }));
app.Run();
public sealed record PolicyValidationRequest(string PolicyId, string ClaimType, decimal Amount);
