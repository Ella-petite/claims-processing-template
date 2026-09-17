using Claims.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/document-intelligence/extract", async (HttpRequest request, CancellationToken ct) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Multipart form-data is required." });
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file is null) return Results.BadRequest(new { error = "A file is required." });

    var extension = Path.GetExtension(file.FileName);
    var documentType = file.FileName.Contains("death", StringComparison.OrdinalIgnoreCase)
        ? "Death Certificate"
        : file.FileName.Contains("invoice", StringComparison.OrdinalIgnoreCase)
            ? "Invoice"
            : file.FileName.Contains("repair", StringComparison.OrdinalIgnoreCase)
                ? "Repair Estimate"
                : extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "Supporting Document" : "Uploaded Document";

    var fields = new Dictionary<string, string>
    {
        ["documentType"] = documentType,
        ["fileName"] = file.FileName,
        ["receivedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        ["processingMode"] = "Mock Azure AI Document Intelligence"
    };

    if (documentType == "Death Certificate")
        fields["deceasedName"] = "Demo Policyholder";

    return Results.Ok(new DocumentExtractionResult(true, documentType, fields));
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Document Intelligence Mock" }));
app.Run();
