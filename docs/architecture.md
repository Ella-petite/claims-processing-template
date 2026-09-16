# Architecture Mapping

## Flow

Web form / Claims Analyst -> Front Door / APIM -> Claims UI / BFF -> Claims API -> Claims DB -> Service Bus -> Durable Functions -> Registration / Policy / Rules / Fraud -> Payment Gateway -> Payment Provider -> Claims status -> Notifications.

## Ownership

- Claims System: claim record and status.
- Registration: client validation.
- Policy Manager: plans, policies, benefits, cover, excess and limits.
- Rules/Fraud: business and risk evaluation.
- Payment Gateway: provider integration and idempotency boundary.
- Notification Function: downstream notification delivery.

## Reliability

Service Bus separates submission from processing. Durable Functions stores workflow state and can wait for human or external events. The payment gateway uses an idempotency key so retried payment requests do not intentionally create duplicate payment instructions.
