using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var documents = storage.AddBlobs("claim-documents");

var serviceBus = builder.AddAzureServiceBus("servicebus")
    .RunAsEmulator();
var workflowQueue = serviceBus.AddServiceBusQueue("claim-workflow", "claim-workflow");
var notificationQueue = serviceBus.AddServiceBusQueue("claim-notifications", "claim-notifications");

var registration = builder.AddProject("registration-system", "../Claims.Registration.Mock.Api/Claims.Registration.Mock.Api.csproj")
    .WithHttpEndpoint(port: 5108, targetPort: 5108, name: "http", isProxied: false);
var policy = builder.AddProject("policy-manager", "../Claims.Policy.Mock.Api/Claims.Policy.Mock.Api.csproj")
    .WithHttpEndpoint(port: 5109, targetPort: 5109, name: "http", isProxied: false);
var rules = builder.AddProject("rules-engine", "../Claims.Rules.Mock.Api/Claims.Rules.Mock.Api.csproj")
    .WithHttpEndpoint(port: 5110, targetPort: 5110, name: "http", isProxied: false);
var fraud = builder.AddProject("fraud-detection", "../Claims.Fraud.Mock.Api/Claims.Fraud.Mock.Api.csproj")
    .WithHttpEndpoint(port: 5111, targetPort: 5111, name: "http", isProxied: false);
var paymentProvider = builder.AddProject("payment-provider", "../Claims.PaymentProvider.Mock.Api/Claims.PaymentProvider.Mock.Api.csproj")
    .WithHttpEndpoint(port: 5112, targetPort: 5112, name: "http", isProxied: false);

var documentProcessing = builder.AddProject("document-processing", "../Claims.DocumentProcessing.Api/Claims.DocumentProcessing.Api.csproj")
    .WithHttpEndpoint(port: 5107, targetPort: 5107, name: "http", isProxied: false);
var paymentGateway = builder.AddProject("payment-gateway", "../Claims.PaymentGateway.Api/Claims.PaymentGateway.Api.csproj")
    .WithHttpEndpoint(port: 5104, targetPort: 5104, name: "http", isProxied: false)
    .WithEnvironment("Provider__BaseUrl", paymentProvider.GetEndpoint("http"))
    .WaitFor(paymentProvider);

var claimsApi = builder.AddProject("claims-api", "../Claims.Api/Claims.Api.csproj")
    .WithHttpEndpoint(port: 5101, targetPort: 5101, name: "http", isProxied: false)
    .WithEnvironment("ServiceBus__ConnectionString", workflowQueue.Resource.ConnectionStringExpression)
    .WithEnvironment("BlobStorage__ConnectionString", documents.Resource.ConnectionStringExpression)
    .WithEnvironment("DocumentProcessing__BaseUrl", documentProcessing.GetEndpoint("http"))
    .WaitFor(storage)
    .WaitFor(serviceBus)
    .WaitFor(documentProcessing);

var workflow = builder.AddAzureFunctionsProject("workflow-functions", "../Claims.Workflow.Functions/Claims.Workflow.Functions.csproj")
    .WithHttpEndpoint(port: 5103, targetPort: 5103, name: "http", isProxied: false)
    .WithHostStorage(storage)
    .WithEnvironment("ServiceBusConnection", workflowQueue.Resource.ConnectionStringExpression)
    .WithEnvironment("Registration__BaseUrl", registration.GetEndpoint("http"))
    .WithEnvironment("Policy__BaseUrl", policy.GetEndpoint("http"))
    .WithEnvironment("Rules__BaseUrl", rules.GetEndpoint("http"))
    .WithEnvironment("Fraud__BaseUrl", fraud.GetEndpoint("http"))
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"))
    .WithEnvironment("PaymentGateway__BaseUrl", paymentGateway.GetEndpoint("http"))
    .WaitFor(claimsApi)
    .WaitFor(registration)
    .WaitFor(policy)
    .WaitFor(rules)
    .WaitFor(fraud)
    .WaitFor(paymentGateway);

var notifications = builder.AddAzureFunctionsProject("notification-function", "../Claims.Notification.Function/Claims.Notification.Function.csproj")
    .WithHttpEndpoint(port: 5106, targetPort: 5106, name: "http", isProxied: false)
    .WithHostStorage(storage)
    .WithEnvironment("NotificationServiceBusConnection", notificationQueue.Resource.ConnectionStringExpression)
    .WaitFor(serviceBus);

paymentGateway.WithEnvironment("Workflow__BaseUrl", workflow.GetEndpoint("http"))
    .WithEnvironment("PublicBaseUrl", paymentGateway.GetEndpoint("http"));
paymentProvider.WithEnvironment("Demo__AutoCompletePayments", "true");

var bff = builder.AddProject("claims-bff", "../Claims.Bff/Claims.Bff.csproj")
    .WithHttpEndpoint(port: 5102, targetPort: 5102, name: "http", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithEnvironment("ClaimsApi__BaseUrl", claimsApi.GetEndpoint("http"))
    .WithEnvironment("Workflow__BaseUrl", workflow.GetEndpoint("http"))
    .WithEnvironment("PaymentGateway__BaseUrl", paymentGateway.GetEndpoint("http"))
    .WithEnvironment("NotificationService__BaseUrl", notifications.GetEndpoint("http"))
    .WithEnvironment("Registration__BaseUrl", registration.GetEndpoint("http"))
    .WithEnvironment("Policy__BaseUrl", policy.GetEndpoint("http"))
    .WithEnvironment("Rules__BaseUrl", rules.GetEndpoint("http"))
    .WithEnvironment("Fraud__BaseUrl", fraud.GetEndpoint("http"))
    .WithEnvironment("PaymentProvider__BaseUrl", paymentProvider.GetEndpoint("http"))
    .WithEnvironment("DocumentProcessing__BaseUrl", documentProcessing.GetEndpoint("http"))
    .WaitFor(claimsApi)
    .WaitFor(workflow)
    .WaitFor(notifications);

var web = builder.AddProject("claims-web", "../Claims.Web/Claims.Web.csproj")
    .WithHttpEndpoint(port: 5100, targetPort: 5100, name: "http", isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(bff);

builder.Build().Run();
