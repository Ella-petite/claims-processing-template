# Sample Endpoints

Claims API:
- POST `/api/claims`
- GET `/api/claims/{id}`
- POST `/api/claims/{id}/status`
- POST `/api/dev/workflow/{id}` (Development only)

BFF:
- POST `/api/client/claims`
- GET `/api/client/claims/{id}`

Dummy external systems:
- POST `/api/registry/validate`
- POST `/api/policy/validate`
- POST `/api/rules/evaluate`
- POST `/api/fraud/score`

Payment Gateway:
- POST `/api/payments`
- GET `/api/payments/{reference}`
- POST `/api/payments/{reference}/complete` (dummy callback simulator)

Workflow Functions:
- POST `/api/workflows/start`
- POST `/api/workflows/{instanceId}/analyst-decision`
- POST `/api/workflows/{instanceId}/payment-completed`

Notification Function:
- POST `/api/notifications`
