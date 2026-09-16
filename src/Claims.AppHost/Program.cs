using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var claimsApi = builder.AddProject("claims-api", "../Claims.Api/Claims.Api.csproj");

var externalSystems = builder.AddProject("external-systems", "../Claims.ExternalSystems.Api/Claims.ExternalSystems.Api.csproj");

var paymentGateway = builder.AddProject("payment-gateway", "../Claims.PaymentGateway.Api/Claims.PaymentGateway.Api.csproj");

var notificationFunction = builder.AddProject("notification-function", "../Claims.Notification.Function/Claims.Notification.Function.csproj");

builder.AddProject("workflow-functions", "../Claims.Workflow.Functions/Claims.Workflow.Functions.csproj")
    .WithEnvironment("ExternalSystems__BaseUrl", externalSystems.GetEndpoint("http"))
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"))
    .WithEnvironment("PaymentGateway__BaseUrl", paymentGateway.GetEndpoint("http"))
    .WithEnvironment("NotificationService__BaseUrl", notificationFunction.GetEndpoint("http"));

builder.AddProject("claims-bff", "../Claims.Bff/Claims.Bff.csproj")
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"));

builder.Build().Run();
