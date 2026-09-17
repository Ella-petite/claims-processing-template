# Architecture Notes

## Frontend and backend separation

**Claims.Web** is the standalone frontend. It renders the dashboard, claim form, document-upload control and Claims Analyst review UI. It sends claim data as JSON to the BFF and uploads supporting documents through a separate multipart endpoint. It does not call the Claims API directly.

**Claims.Bff** is the backend-for-frontend API boundary. It shapes and aggregates endpoints for the UI, handles the frontend upload boundary, forwards requests to the backend services, and exposes demo controls for analyst decisions and payment simulation. It contains no UI and has no `wwwroot` folder. It does not own claim state or orchestrate the claims workflow.

**Claims.Api** is the core Claims backend and system of record. It owns claim creation/retrieval/update operations, status, audit history, payment reference, and document metadata. It persists claims in SQLite for local development and maps to Azure SQL in the target architecture. Outbound claim events are first persisted in a transactional outbox in the same database operation as the claim change, then delivered asynchronously to Service Bus.

## Responsibility boundaries

**Claims.Workflow.Functions** owns orchestration. Durable Functions holds the long-running workflow state and pauses for analyst decisions or payment callbacks.

**Registration System** owns client validation and exposes a mocked API in `Claims.Registration.Mock.Api`.

**Policy Manager** owns plan, policy, benefit, coverage, excess and limit validation and is represented locally by `Claims.Policy.Mock.Api`.

**Rules Engine** owns deterministic business-rule evaluation and is represented locally by `Claims.Rules.Mock.Api`.

**Fraud Detection** owns risk scoring and review thresholds and is represented locally by `Claims.Fraud.Mock.Api`.

**Payment Gateway/API** is the integration boundary to the payment provider. It handles provider-specific mapping, payment references, idempotency, callbacks and error handling. The third-party provider itself is separately mocked by `Claims.PaymentProvider.Mock.Api`.

**Notification Function** consumes claim update events rather than being called directly by the UI. It records mock email/SMS/in-app notifications.

**Document Processing API** represents the optional Azure AI Document Intelligence boundary. Uploaded documents are stored through the Blob Storage/Azurite emulator and then processed by the mock extraction service.

## Local event path

- `claim.submitted` → Claims API outbox → `claim-workflow` queue → Durable Functions trigger.
- `claim.updated` → Claims API outbox → `claim-notifications` queue → Notification Function trigger.

This keeps the workflow and notification consumers separated and models the asynchronous hand-off used by the target architecture.

## Local data and infrastructure mapping

- SQLite → Azure SQL Database
- Azurite Blob emulator → Azure Blob Storage
- Azure Service Bus emulator → Azure Service Bus
- Mock enterprise APIs → existing Registration, Policy, Rules/Fraud and Payment systems
- Document Intelligence mock → Azure AI Document Intelligence
- Local HTTP/CORS → Front Door/APIM and appropriate identity/network controls in production
