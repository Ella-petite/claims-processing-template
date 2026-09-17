# Assumptions Implemented in the Demo

- Claims System remains the system of record for claims.
- Claims.Web is the primary web channel and Claims Analyst Review is an internal workflow UI.
- BFF is API-only; no UI is served from Claims.Bff.
- Claims.Api owns claim operations, status, history and document metadata.
- Service Bus is the asynchronous boundary for claim events.
- Durable Functions orchestrates the long-running workflow.
- Registration, Policy, Rules/Fraud and Payment remain separate systems.
- Payment is accessed through a Gateway/API abstraction with idempotency and callback handling.
- Notifications are event driven.
- Documents are uploaded to Blob Storage and passed through an optional document-processing step.
- Local demo substitutions are SQLite, Azure Service Bus emulator, Azurite, deterministic mock APIs and a payment provider simulator.
