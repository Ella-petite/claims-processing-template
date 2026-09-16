using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var claimsApi = builder.AddProject("claims-api", "../Claims.Api/Claims.Api.csproj")
    .WithHttpEndpoint(port: 5101, name: "http");

var externalSystems = builder.AddProject("external-systems", "../Claims.ExternalSystems.Api/Claims.ExternalSystems.Api.csproj")
    .WithHttpEndpoint(port: 5105, name: "http");

var paymentGateway = builder.AddProject("payment-gateway", "../Claims.PaymentGateway.Api/Claims.PaymentGateway.Api.csproj")
    .WithHttpEndpoint(port: 5104, name: "http");

var notificationFunction = builder.AddProject("notification-function", "../Claims.Notification.Function/Claims.Notification.Function.csproj")
    .WithHttpEndpoint(port: 5106, name: "http");

builder.AddProject("workflow-functions", "../Claims.Workflow.Functions/Claims.Workflow.Functions.csproj")
    .WithHttpEndpoint(port: 5103, name: "http")
    .WithEnvironment("ExternalSystems__BaseUrl", externalSystems.GetEndpoint("http"))
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"))
    .WithEnvironment("PaymentGateway__BaseUrl", paymentGateway.GetEndpoint("http"))
    .WithEnvironment("NotificationService__BaseUrl", notificationFunction.GetEndpoint("http"));

builder.AddProject("claims-bff", "../Claims.Bff/Claims.Bff.csproj")
    .WithHttpEndpoint(port: 5102, name: "http")
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"));

builder.Build().Run();
