# Claims Processing Platform – .NET Template

This repository is a case-study template based on the supplied end-to-end and network architecture diagrams and the insurance-claims requirements.

## Projects

- `Claims.Api` – Claims System API and local SQLite persistence.
- `Claims.Bff` – frontend-oriented API facade.
- `Claims.Workflow.Functions` – Azure Durable Functions orchestration, Service Bus trigger, external event endpoints and workflow activities.
- `Claims.PaymentGateway.Api` – payment abstraction with idempotency and dummy provider behaviour.
- `Claims.ExternalSystems.Api` – dummy Client Registry, Policy Manager, Rules Engine and Fraud Detection APIs.
- `Claims.Notification.Function` – dummy notification worker.
- `Claims.Contracts` – shared DTOs/events.
- `Claims.Tests` – example unit tests.

## Local setup

The environment is intentionally runnable without real Azure services. The Claims API uses SQLite and logs the `claim.submitted` event when Service Bus configuration is absent. A development-only endpoint starts the Durable workflow directly.

Production Azure mapping is documented in `docs/deployment-notes.md` and the Bicep starter is in `infra/main.bicep`.

## Suggested local ports

Claims API: `http://localhost:5101`  
BFF: `http://localhost:5102`  
Workflow Functions: `http://localhost:5103`  
Payment Gateway: `http://localhost:5104`  
Dummy External Systems: `http://localhost:5105`  
Notification Function: `http://localhost:5106`

## Claims operations demo

The BFF serves a browser dashboard at `http://localhost:5102/`. On first startup, the Claims API seeds three deterministic mock claims, including `CL-002`, a R250,000 death claim in manual review with customer validation, policy validation, fraud review and two documents. Select a claim to inspect its workflow, then approve or reject `CL-002` to exercise the status API.

Start the Claims API and BFF for the dashboard:

```powershell
dotnet run --project src/Claims.Api/Claims.Api.csproj --urls http://localhost:5101
dotnet run --project src/Claims.Bff/Claims.Bff.csproj --urls http://localhost:5102
```

## Demo journey

1. Start the external systems API.
2. Start the Payment Gateway API.
3. Start the Notification Function.
4. Start the Workflow Functions project.
5. Start the Claims API.
6. Submit a claim with `POST /api/claims` or through the BFF.
7. Copy the returned claim ID.
8. Call `POST /api/dev/workflow/{claimId}` to start the orchestration locally.
9. The workflow validates the customer, policy and rules/fraud.
10. A straightforward claim moves to payment; a higher-value/complex claim enters the manual-review branch.
11. The Payment Gateway returns a payment reference.
12. Use the workflow's `payment-completed` endpoint to simulate a provider callback and complete the Durable workflow.

## Production hardening still required

This sample does not attempt to implement every production concern from the architecture: full Entra ID, managed identities, APIM policies, Front Door, private endpoints, Azure SQL production configuration, Blob Storage, Key Vault, webhook signature verification, durable idempotency persistence, structured audit storage, PII controls, resilience policies, monitoring/alerts, disaster recovery and formal SLA/SLOs.
