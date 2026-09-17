# Local Demo Flow

This guide maps each user action to the service calls visible in the local Aspire environment.

## Submit claim without manual review

1. `Claims.Web` posts the claim as JSON to `Claims.Bff`.
2. `Claims.Bff` forwards the JSON request to `Claims.Api` to create the claim.
3. `Claims.Api` persists the claim and its `claim.submitted` event in the same SQLite transaction using the local outbox.
4. The Claims API outbox worker publishes the event to the Aspire Service Bus emulator.
5. `Claims.Workflow.Functions` consumes `claim.submitted` and starts a Durable orchestration using the claim ID as the orchestration instance ID.
6. Activities call four distinct mock systems: Registration, Policy, Rules and Fraud.
7. The workflow records each downstream response as claim history in `Claims.Api`.
8. Approved claims call `Claims.PaymentGateway.Api`.
9. The gateway calls `Claims.PaymentProvider.Mock.Api`.
10. The provider waits about four seconds and calls the gateway callback.
11. The gateway sends the payment-completed external event to Durable Functions.
12. Durable Functions updates the claim to `Paid` through `Claims.Api`.
13. Claims.Api stores `claim.updated` in the outbox as part of the status change.
14. The outbox worker publishes it to `claim-notifications`, and `Claims.Notification.Function` consumes the event and records mock notifications.

## Manual review

The Rules mock routes claims over R25,000 to review. Fraud can also request review at a high risk score. Durable Functions pauses on an external `AnalystDecision` event rather than blocking a worker thread. The analyst UI posts that event through the BFF.

## Documents

`Claims.Web` uploads documents through a separate multipart endpoint on the BFF after the claim is created. `Claims.Api` writes them to Blob Storage/Azurite, calls the Document Intelligence mock, stores the extracted fields, and publishes a claim update event. The claim details page exposes a download link for each stored document.

## Production boundary

The local project is deliberately mock-heavy. In production, the mock APIs are replaced by the existing enterprise Registration, Policy, Rules/Fraud and Payment systems, while Azure SQL, Blob Storage, Service Bus, Entra ID, Key Vault, APIM, Front Door, private endpoints, NAT Gateway and ExpressRoute are introduced according to the target environment.

## Seeded demo claim

The seeded `CL-003` record starts in `Submitted` without an active Durable instance so that the demo can show the requeue boundary explicitly. From the claim details page, click **Start workflow**. Claims.Api publishes a fresh `claim.submitted` message to the Service Bus emulator, and the normal Durable Functions path takes over.
