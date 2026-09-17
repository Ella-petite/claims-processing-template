# Claims Processing Platform – .NET 8 + .NET Aspire Case Study

This solution is a local, fully mocked demonstration of the insurance claims architecture described in the supplied requirements and architecture diagrams. It keeps the frontend separate from the BFF and Claims API, preserves .NET Aspire orchestration, and models the major system boundaries explicitly.

## Architecture

### Frontend
- `Claims.Web` — standalone Blazor WebAssembly application. This is the browser UI. It is **not** part of the BFF.

### Backend
- `Claims.Bff` — API-only Backend for Frontend. It shapes endpoints for the UI and does not own claim state or workflow orchestration.
- `Claims.Api` — core Claims backend and system of record. Owns claim persistence, status, history and document metadata.
- `Claims.Workflow.Functions` — Azure Durable Functions workflow plus Service Bus trigger.
- `Claims.PaymentGateway.Api` — payment integration boundary, idempotency and provider callback handling.
- `Claims.Notification.Function` — consumes claim update events and simulates email/SMS/in-app notifications.

### Mocked existing / external systems
- `Claims.Registration.Mock.Api` — Client Registry.
- `Claims.Policy.Mock.Api` — Policy Manager.
- `Claims.Rules.Mock.Api` — business Rules Engine.
- `Claims.Fraud.Mock.Api` — Fraud Detection / risk scoring.
- `Claims.PaymentProvider.Mock.Api` — third-party payment provider.
- `Claims.DocumentProcessing.Api` — Azure AI Document Intelligence mock.

### Shared / orchestration
- `Claims.Contracts` — shared DTOs and events.
- `Claims.AppHost` — .NET Aspire application host.

## End-to-end demo flow

```text
Claims.Web
   ↓
Claims.Bff
   ↓
Claims.Api
   ↓
Azure Service Bus Emulator
   ↓
Durable Functions
   ├── Registration System
   ├── Policy Manager
   ├── Rules Engine
   └── Fraud Detection
            ↓
       Manual Review OR Approval
            ↓
     Payment Gateway API
            ↓
   Payment Provider Mock
            ↓
     Provider Callback
            ↓
    Durable Functions
            ↓
      Claims.Api
            ↓
       claim.updated
            ↓
  Notification Function
            ↓
 Email / SMS / In-App / Analyst Alert
```

## Documents

The Claims Web submission page captures the claim as a JSON payload and sends it to Claims.Bff. Supporting documents are uploaded separately as multipart content to Claims.Bff, which forwards them to Claims.Api. Claims.Api stores each file in the Aspire Blob Storage/Azurite emulator and sends it to the Document Intelligence mock. The mock returns extracted metadata; the UI displays the processing result and provides a download link on the claim details page.

## Mock behaviour

The demo contains seeded claims and deterministic validation/risk rules.

- Valid clients: `CLIENT-001` through `CLIENT-004`.
- Valid policies: `POL-001` through `POL-004`.
- Claims over `R25,000`, `Death` claims at/above `R100,000`, or `Complex` claims go to manual review.
- Fraud risk score is higher for claims of `R50,000` or more; a score of 70+ causes manual review.
- Payment provider auto-completes after approximately four seconds by default and calls the Payment Gateway callback.
- The Gateway then raises the `PaymentCompleted` external event to Durable Functions.
- Notification Function records a notification entry and logs simulated channels.
- Claims.Api uses a local transactional outbox table so claim state changes and their outbound events are persisted together before Service Bus delivery is attempted.

## Running the application

The application projects target **.NET 8**. The current C# Aspire AppHost targets **.NET 10**, so the machine running the AppHost needs the .NET 10 SDK as well as the .NET 8 SDK used by the application projects. Current Aspire documentation states that C# AppHosts require the .NET 10 SDK, while Aspire can run applications targeting .NET 8 or later.

Prerequisites:

1. .NET 8 SDK.
2. .NET 10 SDK for `Claims.AppHost`.
3. Docker Desktop or another supported container runtime for the local Service Bus and Storage emulators.
4. Azure Functions Core Tools for the Functions projects.
5. Aspire tooling/CLI is useful for IDE integration, but the supplied AppHost can be launched with `dotnet run`.

From the solution root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Claims.AppHost/Claims.AppHost.csproj
```

Open the Claims UI at `http://localhost:5100/`. Aspire's dashboard will show the individual resources and their health/logs.

## API reference

See `docs/API-ENDPOINTS.md` for the complete local endpoint map.

## Fixed local endpoints

| Component | Port | Purpose |
|---|---:|---|
| Claims.Web | 5100 | Frontend |
| Claims.Api | 5101 | Core Claims backend / Swagger |
| Claims.Bff | 5102 | UI-facing backend |
| Workflow Functions | 5103 | Durable workflow / events |
| Payment Gateway | 5104 | Payment integration boundary |
| Notifications | 5106 | Notification worker |
| Document Intelligence Mock | 5107 | Document extraction mock |
| Registration Mock | 5108 | Client Registry |
| Policy Mock | 5109 | Policy Manager |
| Rules Mock | 5110 | Business rules |
| Fraud Mock | 5111 | Risk scoring |
| Payment Provider Mock | 5112 | Third-party payment provider |

## Demo scenarios

### Straight-through claim

Use:
- Client `CLIENT-003`
- Policy `POL-003`
- Claim type `Hospitalisation`
- Amount `R10,000`

Expected progression:
`Submitted → Validating Customer → Validating Policy → Checking Rules → Checking Fraud → Approved → Payment Pending → Paid`

### Manual review

Use:
- Client `CLIENT-002`
- Policy `POL-002`
- Claim type `Death`
- Amount `R250,000`
- Upload a document such as `Death Certificate.pdf`.

Expected progression:
`Submitted → validations → Under Review → Analyst Decision → Approved → Payment Pending → Paid`

### Validation failure

Use an invalid client such as `CLIENT-999`. The Registration mock rejects the client and the workflow records `Rejected`.

## What is local/mock versus production

| Local implementation | Production mapping |
|---|---|
| Blazor WebAssembly | Client web/mobile channel |
| BFF local HTTP API | Front Door/APIM protected BFF |
| SQLite | Azure SQL Database |
| Aspire Service Bus emulator | Azure Service Bus |
| Azurite Blob emulator | Azure Blob Storage |
| Mock Registration/Policy/Rules/Fraud APIs | Existing enterprise systems |
| Payment Provider Mock | Real payment provider |
| Document Intelligence Mock | Azure AI Document Intelligence |
| Local configuration | Entra ID / Managed Identity / Key Vault |
| Aspire local networking | VNet/private endpoints/ExpressRoute/NAT as required |

## Sample files

`docs/sample-data/` contains fictional PDF files for testing the document upload flow plus a sample JSON claim payload. They contain no real customer information.

## Case-study scope

This is a functional demonstration, not a production deployment. It intentionally uses local emulators and deterministic mocks so the entire journey can be run and inspected without real customer data or third-party integrations. The architecture leaves clear integration boundaries for the production capabilities described in the design assumptions.
