using UglyToad.PdfPig;

if (args.Any(a => a.Equals("--extract", StringComparison.OrdinalIgnoreCase)))
{
    var extractArgs = args.Where(a => !a.Equals("--extract", StringComparison.OrdinalIgnoreCase)).ToArray();
    return await ExtractProgram.RunAsync(extractArgs);
}

string[] paths = args.Length > 0
    ? args
    : Directory.GetFiles(@"c:\Users\User\Downloads", "*.pdf")
        .Where(p =>
        {
            var sz = new FileInfo(p).Length;
            return sz is > 2_000_000 and < 3_000_000
                   || (sz is > 100_000 and < 200_000 && p.EndsWith(".pdf.pdf", StringComparison.OrdinalIgnoreCase));
        })
        .ToArray();

foreach (var path in paths.OrderBy(File.GetLastWriteTimeUtc))
{
    Console.WriteLine($"======== {Path.GetFileName(path)} ({new FileInfo(path).Length} bytes) ========");
    using var pdf = PdfDocument.Open(path);
    var pages = pdf.GetPages().OrderBy(p => p.Number).ToList();
    Console.WriteLine($"Pages: {pages.Count}");
    for (var i = 0; i < Math.Min(5, pages.Count); i++)
    {
        var p = pages[i];
        var t = p.Text ?? "";
        var letters = t.Count(char.IsLetter);
        Console.WriteLine($"  Page {p.Number}: chars={t.Trim().Length} letters={letters} sample={Preview(t, 200)}");
    }
    if (pages.Count > 5)
    {
        var last = pages[^1];
        var t = last.Text ?? "";
        Console.WriteLine($"  Page {last.Number}: chars={t.Trim().Length} sample={Preview(t, 120)}");
    }
}

return 0;

static string Preview(string t, int max)
{
    t = t.Replace('\r', ' ').Replace('\n', ' ');
    return t.Length <= max ? t : t[..max] + "…";
}
