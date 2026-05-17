namespace DocParseLab.Server.Services;

/// <summary>Сборка текста с сохранением строк и колонок (таблицы) по координатам слов.</summary>
internal static class PdfPageLayoutFormatter
{
    internal readonly record struct LayoutWord(string Text, double Left, double Top, double Right, double Bottom);

    private readonly record struct NormWord(string Text, double Left, double Top, double Right, double Bottom, double CenterX, double CenterY);

    public static string FormatFromPlainText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

    public static string FormatFromWords(IReadOnlyList<LayoutWord> words, string? fallbackPlainText = null, double pageWidth = 0)
    {
        if (words.Count == 0)
        {
            return FormatFromPlainText(fallbackPlainText);
        }

        var normalized = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => new NormWord(
                w.Text.Trim(),
                w.Left,
                w.Top,
                w.Right,
                w.Bottom,
                (w.Left + w.Right) * 0.5,
                (w.Top + w.Bottom) * 0.5))
            .OrderBy(w => w.CenterY)
            .ThenBy(w => w.Left)
            .ToList();

        if (normalized.Count == 0)
        {
            return FormatFromPlainText(fallbackPlainText);
        }

        var effectivePageWidth = pageWidth > 0
            ? pageWidth
            : Math.Max(400, normalized.Max(w => w.Right));

        var heights = normalized
            .Select(w => Math.Max(4.0, w.Bottom - w.Top))
            .OrderBy(h => h)
            .ToList();
        var medianHeight = heights[heights.Count / 2];
        var rowThreshold = Math.Max(5.0, medianHeight * 0.42);

        var rows = ClusterRows(normalized, rowThreshold);
        var columnCenters = DetectColumnCenters(normalized, effectivePageWidth);

        var lines = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var line = FormatRow(row, columnCenters);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines.Count == 0 ? FormatFromPlainText(fallbackPlainText) : string.Join(Environment.NewLine, lines);
    }

    private static List<List<NormWord>> ClusterRows(IReadOnlyList<NormWord> words, double rowThreshold)
    {
        var rows = new List<List<NormWord>>();
        List<NormWord>? currentRow = null;
        double currentCenterY = double.NaN;

        foreach (var word in words)
        {
            if (currentRow == null || Math.Abs(word.CenterY - currentCenterY) > rowThreshold)
            {
                currentRow = new List<NormWord> { word };
                rows.Add(currentRow);
                currentCenterY = word.CenterY;
            }
            else
            {
                currentRow!.Add(word);
            }
        }

        return rows;
    }

    private static List<double> DetectColumnCenters(IReadOnlyList<NormWord> words, double pageWidth)
    {
        var centers = words.Select(w => w.CenterX).OrderBy(x => x).ToList();
        if (centers.Count == 0) return new List<double>();

        var mergeThreshold = Math.Max(10.0, pageWidth * 0.014);
        var clusters = new List<List<double>>();
        List<double>? cluster = null;

        foreach (var x in centers)
        {
            if (cluster == null || x - cluster[^1] > mergeThreshold)
            {
                cluster = new List<double> { x };
                clusters.Add(cluster);
            }
            else
            {
                cluster.Add(x);
            }
        }

        var anchors = clusters.Select(c => c.Average()).OrderBy(a => a).ToList();

        if (anchors.Count <= 1 && words.Count >= 12)
        {
            var gapAnchors = DetectColumnsByGaps(words, pageWidth);
            if (gapAnchors.Count > anchors.Count)
            {
                anchors = gapAnchors;
            }
        }

        if (anchors.Count > 10)
        {
            anchors = MergeClosestAnchors(anchors, targetCount: 8);
        }

        return anchors;
    }

    private static List<double> DetectColumnsByGaps(IReadOnlyList<NormWord> words, double pageWidth)
    {
        var leftEdges = words.Select(w => w.Left).OrderBy(x => x).ToList();
        var gaps = new List<double>();
        for (var i = 1; i < leftEdges.Count; i++)
        {
            gaps.Add(leftEdges[i] - leftEdges[i - 1]);
        }

        if (gaps.Count == 0) return new List<double> { leftEdges[0] };

        gaps.Sort();
        var medianGap = gaps[gaps.Count / 2];
        var splitThreshold = Math.Max(28.0, Math.Max(medianGap * 3.2, pageWidth * 0.045));

        var anchors = new List<double> { leftEdges[0] };
        for (var i = 1; i < leftEdges.Count; i++)
        {
            if (leftEdges[i] - leftEdges[i - 1] >= splitThreshold)
            {
                anchors.Add((leftEdges[i] + leftEdges[i - 1]) * 0.5);
            }
        }

        return anchors;
    }

    private static List<double> MergeClosestAnchors(List<double> anchors, int targetCount)
    {
        var list = anchors.ToList();
        while (list.Count > targetCount)
        {
            var minGap = double.MaxValue;
            var mergeIndex = 0;
            for (var i = 1; i < list.Count; i++)
            {
                var gap = list[i] - list[i - 1];
                if (gap < minGap)
                {
                    minGap = gap;
                    mergeIndex = i;
                }
            }

            list[mergeIndex - 1] = (list[mergeIndex - 1] + list[mergeIndex]) * 0.5;
            list.RemoveAt(mergeIndex);
        }

        return list;
    }

    private static string FormatRow(IReadOnlyList<NormWord> row, IReadOnlyList<double> columnCenters)
    {
        var ordered = row.OrderBy(w => w.Left).ToList();
        if (ordered.Count == 0) return string.Empty;

        if (columnCenters.Count <= 1)
        {
            return string.Join(" ", ordered.Select(w => w.Text));
        }

        var columnCount = columnCenters.Count;
        var cells = new string[columnCount];
        var counts = new int[columnCount];

        foreach (var word in ordered)
        {
            var col = FindNearestColumn(word.CenterX, columnCenters);
            cells[col] = counts[col] == 0 ? word.Text : $"{cells[col]} {word.Text}";
            counts[col]++;
        }

        var usedColumns = counts.Count(c => c > 0);
        if (usedColumns <= 1)
        {
            return string.Join(" ", ordered.Select(w => w.Text));
        }

        return string.Join("\t", cells.Select(c => c?.Trim() ?? string.Empty)).TrimEnd();
    }

    private static int FindNearestColumn(double centerX, IReadOnlyList<double> columnCenters)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < columnCenters.Count; i++)
        {
            var dist = Math.Abs(centerX - columnCenters[i]);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }
}
