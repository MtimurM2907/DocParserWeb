namespace DocParseLab.Server.Services;

public interface IDocumentExportService
{
    byte[] ExportToDocx(string title, string text);
    byte[] ExportToPdf(string title, string text);
    byte[] ExportToSignedPdf(string title, string text, IReadOnlyList<SignatureStampLine> signatures);
}

public sealed record SignatureStampLine(string SignerLabel, DateTime SignedAt, string HashPreview, string? Comment);

