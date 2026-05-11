namespace DocParseLab.Server.Services;

public interface IDocumentExportService
{
    byte[] ExportToDocx(string title, string text);
    byte[] ExportToPdf(string title, string text);
}

