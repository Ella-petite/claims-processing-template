# Claims Processing Platform – .NET 8 + .NET Aspire

A lightweight, fully mocked POC of the insurance‑claims architecture. It showcases a Blazor WebAssembly frontend, a BFF, core Claims API, Durable Functions workflow, and various mock services.

---

## Quick‑Start
1. **Prerequisites**
   - .NET 8 SDK
   - .NET 10 SDK (for `Claims.AppHost`)
   - Docker Desktop (or any container runtime) for Service Bus & Blob emulators
   - Azure Functions Core Tools
2. **Run the solution**
   ```powershell
   dotnet restore
   dotnet build
   dotnet run --project src/Claims.AppHost/Claims.AppHost.csproj
   ```
3. Open the UI at **http://localhost:5100/**. The Aspire dashboard will display resource health.

---

## Documentation
- **Architecture overview** – see [ARCHITECTURE.md](docs/ARCHITECTURE.md)
- **Demo scenarios & mock behaviour** – see [DEMO.md](docs/DEMO.md)
- **API reference** – see `docs/API-ENDPOINTS.md`

---

## Local Ports
| Component | Port | Purpose |
|---|---:|---|
| Claims.Web | 5100 | Frontend |
| Claims.Api | 5101 | Core Claims backend / Swagger |
| Claims.Bff | 5102 | UI‑facing backend |
| Workflow Functions | 5103 | Durable workflow / events |
| Payment Gateway | 5104 | Payment integration boundary |
| Notifications | 5106 | Notification worker |
| Document Intelligence Mock | 5107 | Document extraction mock |
| Registration Mock | 5108 | Client Registry |
| Policy Mock | 5109 | Policy Manager |
| Rules Mock | 5110 | Business rules |
| Fraud Mock | 5111 | Risk scoring |
| Payment Provider Mock | 5112 | Third‑party payment provider |

---

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
| Aspire local networking | VNet/private endpoints/ExpressRoute/NAT |

---

## Sample data
`docs/sample-data/` contains fictional PDFs and a sample JSON claim payload for testing uploads.

---

*This repository is a functional demonstration, not a production deployment. It uses deterministic mocks and local emulators to allow the full claim journey to be run without real customer data or external services.*
