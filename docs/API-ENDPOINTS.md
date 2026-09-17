# Local API Endpoints

The application uses clear boundaries between the frontend, BFF, Claims backend, workflow, payment integration and mocked downstream systems.

## Claims.Web (frontend)

Browser UI only. Calls the BFF and does not call backend services directly.

- `/` — claims dashboard
- `/claims/new` — claim submission with document upload
- `/claims/{id}` — claim detail, audit timeline, integration results and document downloads
- `/analyst/review` — exception/manual-review queue

## Claims.Bff (frontend API boundary) — `http://localhost:5102`

- `GET /api/client/claims`
- `GET /api/client/claims/{id}`
- `GET /api/client/summary`
- `GET /api/client/reference-data`
- `POST /api/client/claims` — JSON claim submission
- `POST /api/client/claims/{id}/documents` — multipart document upload
- `GET /api/client/claims/{id}/documents/{documentId}/download`
- `POST /api/client/claims/{id}/start-workflow` — demo requeue for seeded Submitted claim
- `POST /api/client/claims/{id}/decision` — analyst decision
- `POST /api/client/payments/{reference}/simulate-completion` — demo payment callback trigger
- `GET /api/client/notifications`
- `GET /api/client/services`
- `GET /health`

## Claims.Api (core backend / system of record) — `http://localhost:5101`

- `POST /api/claims`
- `GET /api/claims`
- `GET /api/claims/{id}`
- `GET /api/claims/summary`
- `POST /api/claims/{id}/status`
- `POST /api/claims/{id}/documents`
- `GET /api/claims/{id}/documents/{documentId}/download`
- `POST /api/claims/{id}/requeue` — demo-only Service Bus requeue
- `GET /health`
- `GET /swagger`

## Workflow Functions — `http://localhost:5103`

- `POST /api/workflows/start`
- `POST /api/workflows/{instanceId}/analyst-decision`
- `POST /api/workflows/{instanceId}/payment-completed`
- `GET /api/health`

## Payment Gateway — `http://localhost:5104`

- `POST /api/payments`
- `GET /api/payments/{reference}`
- `POST /api/payments/{reference}/complete`
- `GET /api/mock/provider/payments`
- `POST /api/provider/callback`
- `GET /health`

## Mock downstream systems

- Registration: `POST /api/registry/validate`, `GET /api/mock/clients`, `GET /health`
- Policy Manager: `POST /api/policy/validate`, `GET /api/mock/policies`, `GET /health`
- Rules Engine: `POST /api/rules/evaluate`, `GET /health`
- Fraud Detection: `POST /api/fraud/score`, `GET /health`
- Payment Provider: `POST /api/provider/payments`, `POST /api/provider/payments/{reference}/complete`, `GET /api/provider/payments`, `GET /health`
- Document Intelligence: `POST /api/document-intelligence/extract`, `GET /health`
- Notifications Function: `GET /api/notifications`, `GET /api/health`
