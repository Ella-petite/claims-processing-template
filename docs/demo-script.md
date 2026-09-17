# Case-Study Demo Script

## 1. Start

Run `dotnet run --project src/Claims.AppHost/Claims.AppHost.csproj` and open the Claims Web resource in Aspire.

## 2. Explain the separation

Start with the UI. Explain that Claims.Web is a separate frontend. The BFF is API-only and sits between the browser and the core backend. Claims.Api is the backend system of record.

## 3. Show submission

Open **Submit Claim**. Enter a client, policy, amount and claim type. Upload one or more documents.

## 4. Show the event-driven flow

After submit, open the claim. The timeline should begin updating automatically. In Aspire, show the Claims API, Service Bus emulator, Workflow Functions and downstream mock services.

## 5. Show validation ownership

Point out that customer and policy checks happen through separate mocked systems. The workflow coordinates them but does not own their data.

## 6. Show manual review

Use a high-value claim. The workflow waits in `UnderReview`. Approve or reject it from Analyst Review. Explain that Durable Functions is maintaining workflow state while the analyst decision is pending.

## 7. Show payment

After approval, the workflow calls the Payment Gateway. The gateway assigns an idempotent payment reference and simulates the third-party provider. The provider callback is delivered to the workflow as an external event.

## 8. Show notifications

The Claims API records the status and publishes `claim.updated`. Notification Function consumes the event and records mock email/SMS/in-app notifications. The dashboard displays those messages.
