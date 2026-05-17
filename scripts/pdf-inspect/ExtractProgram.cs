using DocParseLab.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public static class ExtractProgram
{
public static async Task<int> RunAsync(string[] args)
{
var serverRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "DocParseLab.Server"));
var pdfPath = args.FirstOrDefault() ?? FindRussianPdf();
if (pdfPath == null || !File.Exists(pdfPath))
{
    Console.WriteLine("PDF not found");
    return 1;
}

var host = Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.SetBasePath(serverRoot);
        cfg.AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.Configure<OcrOptions>(ctx.Configuration.GetSection(OcrOptions.SectionName));
        services.AddSingleton<TesseractOcrService>();
        services.AddSingleton<IOcrService, CompositeOcrService>();
        services.AddSingleton<IPdfPageRenderer, DocnetPdfPageRenderer>();
        services.AddSingleton<PdfTextExtractor>();
    })
    .Build();

Directory.SetCurrentDirectory(serverRoot);
var extractor = host.Services.GetRequiredService<PdfTextExtractor>();
await using var fs = File.OpenRead(pdfPath);
var sw = System.Diagnostics.Stopwatch.StartNew();
var text = await extractor.ExtractTextAsync(fs);
sw.Stop();

var pages = text.Split('\f', StringSplitOptions.None);
Console.WriteLine($"Extracted in {sw.Elapsed}. Total chars={text.Length}, page chunks={pages.Length}");
for (var i = 0; i < pages.Length; i++)
{
    var p = pages[i].Trim();
    Console.WriteLine($"  chunk {i + 1}: lines={p.Count(c => c == '\n') + 1} chars={p.Length}");
}

var sample = text.Replace('\f', '|').Replace('\r', ' ').Replace('\n', ' ');
Console.WriteLine("Sample: " + (sample.Length > 400 ? sample[..400] + "…" : sample));
return 0;
}

    private static string? FindRussianPdf() =>
        Directory.GetFiles(@"c:\Users\User\Downloads", "*.pdf")
            .FirstOrDefault(p => !p.EndsWith(".pdf.pdf", StringComparison.OrdinalIgnoreCase)
                                 && new FileInfo(p).Length is > 2_000_000 and < 3_000_000);
}
