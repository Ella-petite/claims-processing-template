using Claims.Contracts.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Claims.Workflow.Functions;

public sealed class ClaimsFunctions(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<ClaimsFunctions> logger)
{
    [Function("ClaimSubmitted")]
    public async Task ClaimSubmitted(
        [ServiceBusTrigger("claims", Connection = "ServiceBusConnection")] string body,
        [DurableClient] DurableTaskClient durableClient)
    {
        var message = JsonSerializer.Deserialize<ClaimSubmittedEvent>(body) ?? throw new InvalidOperationException("Invalid claim.submitted message.");
        await durableClient.ScheduleNewOrchestrationInstanceAsync("ClaimsOrchestration", message);
    }

    [Function("StartClaimsWorkflow")]
    public async Task<HttpResponseData> StartClaimsWorkflow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient durableClient)
    {
        var input = await req.ReadFromJsonAsync<ClaimSubmittedEvent>();
        if (input is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync("ClaimsOrchestration", input);
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { instanceId });
        return response;
    }

    [Function("RaisePaymentCompleted")]
    public async Task<HttpResponseData> RaisePaymentCompleted(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/{instanceId}/payment-completed")] HttpRequestData req,
        string instanceId,
        [DurableClient] DurableTaskClient durableClient)
    {
        var payload = await req.ReadFromJsonAsync<PaymentCompletedEvent>();
        if (payload is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        await durableClient.RaiseEventAsync(instanceId, "PaymentCompleted", payload);
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("RaiseAnalystDecision")]
    public async Task<HttpResponseData> RaiseAnalystDecision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/{instanceId}/analyst-decision")] HttpRequestData req,
        string instanceId,
        [DurableClient] DurableTaskClient durableClient)
    {
        var body = await req.ReadFromJsonAsync<AnalystDecisionRequest>();
        if (body is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        await durableClient.RaiseEventAsync(instanceId, "AnalystDecision", body.Approved);
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    [Function("ClaimsOrchestration")]
    public static async Task RunOrchestration([OrchestrationTrigger] TaskOrchestrationContext context, ClaimSubmittedEvent input)
    {
        var customer = await context.CallActivityAsync<ValidationResult>("ValidateCustomer", input);
        if (!customer.Success)
        {
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "RegistrationSystem", customer.Reason));
            return;
        }

        var policy = await context.CallActivityAsync<ValidationResult>("ValidatePolicy", input);
        if (!policy.Success)
        {
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "PolicyManager", policy.Reason));
            return;
        }

        var fraud = await context.CallActivityAsync<FraudResult>("RunFraudAndRules", input);
        if (fraud.RequiresManualReview)
        {
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.UnderReview, "Rules/Fraud", fraud.Reason));
            var analystDecision = await context.WaitForExternalEvent<bool>("AnalystDecision");
            if (!analystDecision)
            {
                await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "ClaimsAnalyst", "Manual review declined the claim."));
                return;
            }
        }

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Approved, "Workflow", "Claim approved for payment."));
        var payment = await context.CallActivityAsync<PaymentResponse>("InitiatePayment", new PaymentRequest(input.ClaimId, input.Amount, "ZAR", $"claim-{input.ClaimId}"));
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.PaymentPending, "PaymentGateway", "Waiting for provider result.", payment.PaymentReference));

        var completed = await context.WaitForExternalEvent<PaymentCompletedEvent>("PaymentCompleted");
        var finalStatus = completed.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? ClaimStatus.Paid : ClaimStatus.Failed;
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, finalStatus, "PaymentGateway", completed.Status, completed.PaymentReference));
        await context.CallActivityAsync("SendNotification", new NotificationEvent(input.ClaimId, "ClaimUpdated", $"Claim status changed to {finalStatus}."));
    }

    [Function("ValidateCustomer")]
    public async Task<ValidationResult> ValidateCustomer([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await External().PostAsJsonAsync("/api/registry/validate", new { input.ClientId });
        return await Read<ValidationResult>(response);
    }

    [Function("ValidatePolicy")]
    public async Task<ValidationResult> ValidatePolicy([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await External().PostAsJsonAsync("/api/policy/validate", new { input.PolicyId, input.ClaimType, input.Amount });
        return await Read<ValidationResult>(response);
    }

    [Function("RunFraudAndRules")]
    public async Task<FraudResult> RunFraudAndRules([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var ruleResponse = await External().PostAsJsonAsync("/api/rules/evaluate", new { input.ClaimType, input.Amount });
        var ruleResult = await Read<FraudResult>(ruleResponse);
        if (ruleResult.RequiresManualReview) return ruleResult;

        var fraudResponse = await External().PostAsJsonAsync("/api/fraud/score", new { input.ClientId, input.Amount, input.ClaimType });
        return await Read<FraudResult>(fraudResponse);
    }

    [Function("InitiatePayment")]
    public async Task<PaymentResponse> InitiatePayment([ActivityTrigger] PaymentRequest input)
    {
        var response = await Payment().PostAsJsonAsync("/api/payments", input);
        return await Read<PaymentResponse>(response);
    }

    [Function("UpdateClaimStatus")]
    public async Task UpdateClaimStatus([ActivityTrigger] StatusCommand command)
    {
        var response = await Claims().PostAsJsonAsync($"/api/claims/{command.ClaimId}/status", new
        {
            command.Status, command.Source, command.Note, command.PaymentReference
        });
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Claim {ClaimId} -> {Status}", command.ClaimId, command.Status);
    }

    [Function("SendNotification")]
    public async Task SendNotification([ActivityTrigger] NotificationEvent input)
    {
        var response = await Notifications().PostAsJsonAsync("/api/notifications", input);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient External() => CreateClient(config["ExternalSystems:BaseUrl"] ?? "http://localhost:5105");
    private HttpClient Claims() => CreateClient(config["ClaimsApi:BaseUrl"] ?? "http://localhost:5101");
    private HttpClient Payment() => CreateClient(config["PaymentGateway:BaseUrl"] ?? "http://localhost:5104");
    private HttpClient Notifications() => CreateClient(config["NotificationService:BaseUrl"] ?? "http://localhost:5106");
    private HttpClient CreateClient(string baseUrl) { var client = httpClientFactory.CreateClient(); client.BaseAddress = new Uri(baseUrl); return client; }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("Empty downstream response.");
    }

    public sealed record StatusCommand(Guid ClaimId, ClaimStatus Status, string Source, string? Note, string? PaymentReference = null);
    public sealed record AnalystDecisionRequest(bool Approved);
}
