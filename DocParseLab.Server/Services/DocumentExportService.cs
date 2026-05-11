using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QDocument = QuestPDF.Fluent.Document;

namespace DocParseLab.Server.Services;

public sealed class DocumentExportService : IDocumentExportService
{
    static DocumentExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] ExportToDocx(string title, string text)
    {
        var lines = TextStructureFormatter.SplitParagraphs(text);
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = new Body();

            body.AppendChild(new Paragraph(
                new Run(
                    new RunProperties(new Bold()),
                    new Text(title))));

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    body.AppendChild(new Paragraph());
                    continue;
                }

                var paragraphProperties = new ParagraphProperties();
                var runProperties = new RunProperties();

                if (TextStructureFormatter.IsCapsTitle(line))
                {
                    paragraphProperties.Justification = new Justification { Val = JustificationValues.Center };
                    runProperties.Bold = new Bold();
                    runProperties.FontSize = new FontSize { Val = "30" };
                }
                else if (TextStructureFormatter.IsSectionTitle(line))
                {
                    paragraphProperties.Justification = new Justification { Val = JustificationValues.Center };
                    runProperties.Bold = new Bold();
                    runProperties.FontSize = new FontSize { Val = "34" };
                }
                else
                {
                    paragraphProperties.Indentation = new Indentation { FirstLine = "720" };
                    runProperties.FontSize = new FontSize { Val = "28" };
                }

                body.AppendChild(new Paragraph(
                    paragraphProperties,
                    new Run(
                        runProperties,
                        new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
            }

            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public byte[] ExportToPdf(string title, string text)
    {
        var lines = TextStructureFormatter.SplitParagraphs(text);
        var bytes = QDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11));
                page.Content().Column(col =>
                {
                    col.Item().Text(title).FontSize(16).Bold();
                    col.Item().PaddingTop(10).Text(string.Empty);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            col.Item().PaddingTop(8).Text(string.Empty);
                            continue;
                        }

                        if (TextStructureFormatter.IsCapsTitle(line))
                        {
                            col.Item().AlignCenter().Text(line).FontSize(14).Bold();
                            continue;
                        }

                        if (TextStructureFormatter.IsSectionTitle(line))
                        {
                            col.Item().AlignCenter().Text(line).FontSize(15).Bold();
                            continue;
                        }

                        col.Item().PaddingLeft(18).Text(line).FontSize(12);
                    }
                });
            });
        }).GeneratePdf();

        return bytes;
    }
}

