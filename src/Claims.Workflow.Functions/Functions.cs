using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.Contracts.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Claims.Workflow.Functions;

public sealed class ClaimsFunctions(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ClaimsFunctions> logger)
{
    [Function("ClaimSubmitted")]
    public async Task ClaimSubmitted(
        [ServiceBusTrigger("claim-workflow", Connection = "ServiceBusConnection")] string body,
        [DurableClient] DurableTaskClient durableClient,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<ClaimSubmittedEvent>(body)
            ?? throw new InvalidOperationException("Invalid claim.submitted message.");

        var instanceId = message.ClaimId.ToString("N");
        var options = new StartOrchestrationOptions(instanceId, null);
        try
        {
            await durableClient.ScheduleNewOrchestrationInstanceAsync("ClaimsOrchestration", message, options, cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("AlreadyExists", StringComparison.Ordinal))
        {
            logger.LogInformation("Workflow already exists for claim {ClaimId}.", message.ClaimId);
        }

        logger.LogInformation("claim.submitted consumed for {ClaimId} with workflow instance {InstanceId}.", message.ClaimId, instanceId);
    }

    [Function("StartClaimsWorkflow")]
    public async Task<HttpResponseData> StartClaimsWorkflow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient durableClient)
    {
        var input = await req.ReadFromJsonAsync<ClaimSubmittedEvent>();
        if (input is null) return req.CreateResponse(HttpStatusCode.BadRequest);

        var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
            "ClaimsOrchestration",
            input);

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
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { accepted = true, instanceId, eventName = "PaymentCompleted" });
        return response;
    }

    [Function("RaiseAnalystDecision")]
    public async Task<HttpResponseData> RaiseAnalystDecision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/{instanceId}/analyst-decision")] HttpRequestData req,
        string instanceId,
        [DurableClient] DurableTaskClient durableClient)
    {
        var body = await req.ReadFromJsonAsync<AnalystDecisionRequest>();
        if (body is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        await durableClient.RaiseEventAsync(instanceId, "AnalystDecision", body);
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { accepted = true, instanceId, eventName = "AnalystDecision" });
        return response;
    }

    [Function("ClaimsOrchestration")]
    public async Task RunOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        ClaimSubmittedEvent input)
    {
        var correlationId = input.CorrelationId;
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.ValidatingCustomer, "Workflow", "Starting customer validation.", null, context.InstanceId, correlationId));

        var customer = await context.CallActivityAsync<ClientValidationResult>("ValidateCustomer", input);
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(
            input.ClaimId, ClaimStatus.ValidatingCustomer, "RegistrationSystem",
            customer.Success
                ? $"Customer validated: {customer.ClientName} ({customer.ClientTier})."
                : customer.Reason ?? "Customer validation failed.",
            null, context.InstanceId, correlationId));
        if (!customer.Success)
        {
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "RegistrationSystem", customer.Reason, null, context.InstanceId, correlationId));
            return;
        }

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.ValidatingPolicy, "Workflow", "Starting policy validation.", null, context.InstanceId, correlationId));
        var policy = await context.CallActivityAsync<PolicyValidationResult>("ValidatePolicy", input);
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(
            input.ClaimId, ClaimStatus.ValidatingPolicy, "PolicyManager",
            policy.Success
                ? $"Policy {policy.PolicyNumber} / {policy.Plan} validated. Coverage limit R {policy.CoverageLimit:N2}; excess R {policy.Excess:N2}."
                : policy.Reason ?? "Policy validation failed.",
            null, context.InstanceId, correlationId));
        if (!policy.Success)
        {
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "PolicyManager", policy.Reason, null, context.InstanceId, correlationId));
            return;
        }

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.CheckingRules, "Workflow", "Applying business rules.", null, context.InstanceId, correlationId));
        var rules = await context.CallActivityAsync<RulesResult>("RunRules", input);
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(
            input.ClaimId, ClaimStatus.CheckingRules, "RulesEngine",
            rules.RequiresManualReview ? (rules.Reason ?? "Rule requires manual review.") : "Business rules passed; no exception raised.",
            null, context.InstanceId, correlationId));

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.CheckingFraud, "Workflow", "Checking fraud and risk.", null, context.InstanceId, correlationId));
        var fraud = await context.CallActivityAsync<FraudResult>("RunFraud", input);
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(
            input.ClaimId, ClaimStatus.CheckingFraud, "FraudDetection",
            $"Risk score {fraud.RiskScore}. {fraud.Reason}", null, context.InstanceId, correlationId));

        if (rules.RequiresManualReview || fraud.RequiresManualReview)
        {
            var reason = rules.RequiresManualReview ? rules.Reason : fraud.Reason;
            await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.UnderReview, "Rules/Fraud", reason, null, context.InstanceId, correlationId));
            var decision = await context.WaitForExternalEvent<AnalystDecisionRequest>("AnalystDecision");
            if (!decision.Approved)
            {
                await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Rejected, "ClaimsAnalyst", decision.Note ?? "Claim rejected during manual review.", null, context.InstanceId, correlationId));
                return;
            }
        }

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.Approved, "Workflow", "Claim approved for payment.", null, context.InstanceId, correlationId));
        var payment = await context.CallActivityAsync<PaymentResponse>(
            "InitiatePayment",
            new PaymentRequest(input.ClaimId, input.Amount, "ZAR", $"claim-{input.ClaimId:N}", context.InstanceId));

        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, ClaimStatus.PaymentPending, "PaymentGateway", "Payment initiated; waiting for provider callback.", payment.PaymentReference, context.InstanceId, correlationId));
        var completed = await context.WaitForExternalEvent<PaymentCompletedEvent>("PaymentCompleted");
        var finalStatus = completed.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? ClaimStatus.Paid : ClaimStatus.Failed;
        await context.CallActivityAsync("UpdateClaimStatus", new StatusCommand(input.ClaimId, finalStatus, "PaymentGateway", $"Payment provider returned '{completed.Status}'.", completed.PaymentReference, context.InstanceId, correlationId));
    }

    [Function("ValidateCustomer")]
    public async Task<ClientValidationResult> ValidateCustomer([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await CreateClient(configuration["Registration:BaseUrl"] ?? "http://localhost:5108").PostAsJsonAsync("/api/registry/validate", new { input.ClientId });
        return await ReadAsync<ClientValidationResult>(response);
    }

    [Function("ValidatePolicy")]
    public async Task<PolicyValidationResult> ValidatePolicy([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await CreateClient(configuration["Policy:BaseUrl"] ?? "http://localhost:5109").PostAsJsonAsync("/api/policy/validate", new { input.PolicyId, input.ClaimType, input.Amount });
        return await ReadAsync<PolicyValidationResult>(response);
    }

    [Function("RunRules")]
    public async Task<RulesResult> RunRules([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await CreateClient(configuration["Rules:BaseUrl"] ?? "http://localhost:5110").PostAsJsonAsync("/api/rules/evaluate", new { input.ClaimType, input.Amount });
        return await ReadAsync<RulesResult>(response);
    }

    [Function("RunFraud")]
    public async Task<FraudResult> RunFraud([ActivityTrigger] ClaimSubmittedEvent input)
    {
        var response = await CreateClient(configuration["Fraud:BaseUrl"] ?? "http://localhost:5111").PostAsJsonAsync("/api/fraud/score", new { input.ClientId, input.Amount, input.ClaimType });
        return await ReadAsync<FraudResult>(response);
    }

    [Function("InitiatePayment")]
    public async Task<PaymentResponse> InitiatePayment([ActivityTrigger] PaymentRequest input)
    {
        var response = await Payment().PostAsJsonAsync("/api/payments", input);
        return await ReadAsync<PaymentResponse>(response);
    }

    [Function("UpdateClaimStatus")]
    public async Task UpdateClaimStatus([ActivityTrigger] StatusCommand command)
    {
        var response = await Claims().PostAsJsonAsync($"/api/claims/{command.ClaimId}/status", new
        {
            command.Status,
            command.Source,
            command.Note,
            command.PaymentReference,
            WorkflowInstanceId = command.WorkflowInstanceId,
            command.CorrelationId
        });
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Claim {ClaimId} -> {Status}", command.ClaimId, command.Status);
    }

    private HttpClient Claims() => CreateClient(configuration["ClaimsApi:BaseUrl"] ?? "http://localhost:5101");
    private HttpClient Payment() => CreateClient(configuration["PaymentGateway:BaseUrl"] ?? "http://localhost:5104");
    private HttpClient CreateClient(string baseUrl)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("Downstream response was empty.");
    }

    public sealed record StatusCommand(
        Guid ClaimId,
        ClaimStatus Status,
        string Source,
        string? Note,
        string? PaymentReference,
        string? WorkflowInstanceId,
        string CorrelationId);
}

public static class ClaimsFunctionsHealth
{
    [Function("WorkflowHealth")]
    public static async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "ok", service = "Claims.Workflow.Functions" });
        return response;
    }
}
