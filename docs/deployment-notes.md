# Deployment Mapping

| Template component | Target Azure service |
|---|---|
| Claims UI / BFF / Claims API | Azure App Service or Azure Container Apps |
| Durable Functions / Notification Function | Azure Functions |
| Messaging | Azure Service Bus |
| Claims database | Azure SQL Database |
| Claim documents | Azure Blob Storage |
| Secrets | Azure Key Vault |
| Monitoring | Application Insights + Log Analytics + Azure Monitor |
| Inbound protection | Azure Front Door + API Management |
| Outbound internet | NAT Gateway |
| Private Azure resources | VNet + Private Endpoints |
| On-premises/private connectivity | ExpressRoute where required |

The provided Bicep is intentionally a starter rather than a complete production landing-zone deployment.
