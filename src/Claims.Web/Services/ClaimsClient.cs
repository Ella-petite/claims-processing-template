using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Claims.Contracts.Models;

namespace Claims.Web.Services;

public sealed class ClaimsClient(HttpClient http)
{
    public async Task<List<ClaimDto>> GetClaimsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ClaimDto>>("api/client/claims", ct) ?? [];

    public Task<ClaimDto?> GetClaimAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<ClaimDto>($"api/client/claims/{id}", ct);

    public Task<SummaryDto?> GetSummaryAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<SummaryDto>("api/client/summary", ct);

    public Task<ReferenceDataDto?> GetReferenceDataAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<ReferenceDataDto>("api/client/reference-data", ct);

    public async Task<ClaimDto?> SubmitClaimAsync(
        string clientId,
        string policyId,
        string claimType,
        decimal amount,
        DateTime incidentDate,
        string description,
        CancellationToken ct = default)
    {
        var request = new CreateClaimRequest(
            clientId,
            policyId,
            claimType,
            amount,
            incidentDate,
            description);

        var response = await http.PostAsJsonAsync("api/client/claims", request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));

        return await response.Content.ReadFromJsonAsync<ClaimDto>(cancellationToken: ct);
    }

    public async Task UploadDocumentsAsync(
        Guid claimId,
        IReadOnlyCollection<IBrowserFile> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0) return;

        using var content = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var stream = file.OpenReadStream(10 * 1024 * 1024);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "files", file.Name);
        }

        var response = await http.PostAsync($"api/client/claims/{claimId}/documents", content, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));
    }

    public string GetDocumentDownloadUrl(Guid claimId, Guid documentId) =>
        new Uri(http.BaseAddress!, $"api/client/claims/{claimId}/documents/{documentId}/download").ToString();

    public async Task StartWorkflowAsync(Guid claimId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/client/claims/{claimId}/start-workflow", null, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task DecideAsync(Guid claimId, bool approved, string? note, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"api/client/claims/{claimId}/decision", new AnalystDecisionRequest(approved, note), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SimulatePaymentCompletionAsync(string paymentReference, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/client/payments/{paymentReference}/simulate-completion", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<NotificationEvent>> GetNotificationsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<NotificationEvent>>("api/client/notifications", ct) ?? [];

    public async Task<List<ServiceHealth>> GetServicesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ServiceHealth>>("api/client/services", ct) ?? [];

    public sealed record SummaryDto(int total, int submitted, int underReview, int approved, int paymentPending, int paid, int rejected);
    public sealed record ServiceHealth(string name, string status);
}
