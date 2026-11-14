using System.Text;
using System.Text.Json;
using MiniExcelLibs;
using QuickConvert.Core.IO;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Spreadsheets;

public sealed record SpreadsheetRequest(string SourcePath, string TargetExtension, string OutputDirectory, bool Overwrite);

/// <summary>Tabular data as read from csv/json/xlsx/txt. Values are strings, numbers, bools or null.</summary>
public sealed class SheetData
{
    public List<string> Columns { get; } = new();
    public List<object?[]> Rows { get; } = new();
}

public sealed class SpreadsheetFormatException(string message) : Exception(message);

/// <summary>In-process csv/json/xlsx/txt/md conversion. xlsx goes through MiniExcel; no external binary.</summary>
public sealed class SpreadsheetConverter
{
    public static readonly IReadOnlySet<string> Sources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".json", ".xlsx", ".txt" };

    private static readonly HashSet<string> TablePairs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv>.json", ".json>.csv", ".csv>.xlsx", ".json>.xlsx", ".xlsx>.csv", ".xlsx>.json",
        ".txt>.csv", ".csv>.txt", ".csv>.md", ".json>.md", ".xlsx>.md",
    };

    public bool CanConvert(string sourceExt, string targetExt) => TablePairs.Contains(sourceExt + ">" + targetExt);

    public Task<ToolResult> ConvertAsync(SpreadsheetRequest req, IProgress<int>? progress, CancellationToken ct)
        => Task.Run(() => Convert(req, progress, ct), ct);

    public ToolResult Convert(SpreadsheetRequest req, IProgress<int>? progress, CancellationToken ct)
    {
        var src = Path.GetExtension(req.SourcePath).ToLowerInvariant();
        var tgt = req.TargetExtension.ToLowerInvariant();
        if (!CanConvert(src, tgt)) return ToolResult.Fail($"Converting {src} to {tgt} is not supported.");
        if (!File.Exists(req.SourcePath)) return ToolResult.Fail("The source file no longer exists.");

        Directory.CreateDirectory(req.OutputDirectory);
        var tempDir = Path.Combine(req.OutputDirectory, ".tmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            progress?.Report(0);
            SheetData data;
            try { data = Read(req.SourcePath, src, ct); }
            catch (SpreadsheetFormatException ex) { return ToolResult.Fail(ex.Message); }
            catch (JsonException ex) { return ToolResult.Fail($"This is not valid JSON: {ex.Message}"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
            { return ToolResult.Fail($"The file could not be read: {ex.Message}"); }

            if (data.Columns.Count == 0) return ToolResult.Fail("The file contains no data to convert.");
            ct.ThrowIfCancellationRequested();
            progress?.Report(40);

            var tempOut = Path.Combine(tempDir, "out" + tgt);
            Write(data, tgt, tempOut, ct);
            ct.ThrowIfCancellationRequested();
            progress?.Report(95);

            var stem = Path.GetFileNameWithoutExtension(req.SourcePath);
            var final = PromotionHelper.PromoteFile(tempOut, Path.Combine(req.OutputDirectory, stem + tgt), req.Overwrite);
            progress?.Report(100);
            return ToolResult.Ok(final);
        }
        finally { TempCleanup.Purge(tempDir); }
    }

    // ---------- readers ----------

    private static SheetData Read(string path, string ext, CancellationToken ct) => ext switch
    {
        ".csv" => ReadCsv(File.ReadAllText(path, Encoding.UTF8), ct),
        ".json" => ReadJson(path),
        ".xlsx" => ReadXlsx(path, ct),
        ".txt" => ReadTxt(File.ReadAllText(path, Encoding.UTF8)),
        _ => throw new NotSupportedException(ext)
    };

    public static SheetData ReadCsv(string text, CancellationToken ct = default)
    {
        var records = ParseCsv(text, ct);
        var data = new SheetData();
        if (records.Count == 0) return data;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var header = records[0];
        for (var i = 0; i < header.Count; i++)
        {
            var name = string.IsNullOrWhiteSpace(header[i]) ? $"Column{i + 1}" : header[i].Trim();
            var unique = name;
            for (var n = 2; !seen.Add(unique); n++) unique = $"{name}_{n}";
            data.Columns.Add(unique);
        }

        for (var r = 1; r < records.Count; r++)
        {
            var row = new object?[data.Columns.Count];
            for (var c = 0; c < row.Length; c++) row[c] = c < records[r].Count ? records[r][c] : string.Empty;
            data.Rows.Add(row);
        }
        return data;
    }

    /// <summary>RFC 4180 parser: quotes, doubled quotes, CRLF/LF, embedded newlines, BOM.</summary>
    public static List<List<string>> ParseCsv(string text, CancellationToken ct = default)
    {
        var rows = new List<List<string>>();
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var fieldWasQuoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    inQuotes = true; fieldWasQuoted = true; break;
                case '"':
                    throw new SpreadsheetFormatException("The CSV is malformed: a quote appears in the middle of a field.");
                case ',':
                    row.Add(field.ToString()); field.Clear(); fieldWasQuoted = false; break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString()); field.Clear(); fieldWasQuoted = false;
                    if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row); // skip blank lines
                    row = new List<string>();
                    break;
                default:
                    field.Append(ch); break;
            }
        }

        if (inQuotes) throw new SpreadsheetFormatException("The CSV is malformed: a quoted field is never closed.");
        if (field.Length > 0 || row.Count > 0 || fieldWasQuoted)
        {
            row.Add(field.ToString());
            if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row);
        }
        return rows;
    }

    private static SheetData ReadJson(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new SpreadsheetFormatException("Only JSON arrays of objects can be converted to a table (the file's top level is not an array).");

        var data = new SheetData();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new SpreadsheetFormatException("Only JSON arrays of objects can be converted to a table (found an element that is not an object).");
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in item.EnumerateObject())
            {
                if (!index.ContainsKey(prop.Name)) { index[prop.Name] = data.Columns.Count; data.Columns.Add(prop.Name); }
                d[prop.Name] = JsonValue(prop.Value);
            }
            rows.Add(d);
        }

        foreach (var d in rows)
        {
            var row = new object?[data.Columns.Count];
            for (var c = 0; c < row.Length; c++) row[c] = d.TryGetValue(data.Columns[c], out var v) ? v : null;
            data.Rows.Add(row);
        }
        return data;
    }

    private static object? JsonValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetRawText(), // nested objects/arrays are kept as JSON text
    };

    private static SheetData ReadXlsx(string path, CancellationToken ct)
    {
        var data = new SheetData();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<IDictionary<string, object?>>();

        foreach (var item in MiniExcel.Query(path, useHeaderRow: true))
        {
            ct.ThrowIfCancellationRequested();
            var dict = (IDictionary<string, object?>)item;
            foreach (var key in dict.Keys)
                if (!index.ContainsKey(key)) { index[key] = data.Columns.Count; data.Columns.Add(key); }
            rows.Add(dict);
        }

        foreach (var d in rows)
        {
            var row = new object?[data.Columns.Count];
            for (var c = 0; c < row.Length; c++) row[c] = d.TryGetValue(data.Columns[c], out var v) ? v : null;
            data.Rows.Add(row);
        }
        return data;
    }

    /// <summary>Tab-separated lines if most lines contain a tab, otherwise one column named "text".</summary>
    private static SheetData ReadTxt(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        var data = new SheetData();
        if (lines.Count == 0) return data;

        var tabbed = lines.Count(l => l.Contains('\t')) * 2 > lines.Count;
        if (!tabbed)
        {
            data.Columns.Add("text");
            foreach (var l in lines) data.Rows.Add(new object?[] { l });
            return data;
        }

        var split = lines.Select(l => l.Split('\t')).ToList();
        var width = split.Max(s => s.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < width; i++)
        {
            var h = i < split[0].Length ? split[0][i].Trim() : string.Empty;
            var name = h.Length == 0 ? $"Column{i + 1}" : h;
            var unique = name;
            for (var n = 2; !seen.Add(unique); n++) unique = $"{name}_{n}";
            data.Columns.Add(unique);
        }
        foreach (var s in split.Skip(1))
        {
            var row = new object?[width];
            for (var c = 0; c < width; c++) row[c] = c < s.Length ? s[c] : string.Empty;
            data.Rows.Add(row);
        }
        return data;
    }

    // ---------- writers ----------

    private static void Write(SheetData data, string ext, string path, CancellationToken ct)
    {
        switch (ext)
        {
            case ".csv": File.WriteAllText(path, ToCsv(data), new UTF8Encoding(true)); break;
            case ".json": WriteJson(data, path); break;
            case ".xlsx": WriteXlsx(data, path, ct); break;
            case ".txt": File.WriteAllText(path, ToText(data), new UTF8Encoding(false)); break;
            case ".md": File.WriteAllText(path, ToMarkdown(data), new UTF8Encoding(false)); break;
            default: throw new NotSupportedException(ext);
        }
    }

    public static string ToCsv(SheetData d)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', d.Columns.Select(CsvEscape))).Append("\r\n");
        foreach (var r in d.Rows) sb.Append(string.Join(',', r.Select(v => CsvEscape(Format(v))))).Append("\r\n");
        return sb.ToString();
    }

    private static string CsvEscape(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || s.StartsWith(' ') || s.EndsWith(' ')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static string Format(object? v) => v switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty
    };

    private static void WriteJson(SheetData d, string path)
    {
        using var fs = File.Create(path);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        w.WriteStartArray();
        foreach (var r in d.Rows)
        {
            w.WriteStartObject();
            for (var c = 0; c < d.Columns.Count; c++)
            {
                w.WritePropertyName(d.Columns[c]);
                switch (r[c])
                {
                    case null: w.WriteNullValue(); break;
                    case bool b: w.WriteBooleanValue(b); break;
                    case long l: w.WriteNumberValue(l); break;
                    case int i: w.WriteNumberValue(i); break;
                    case double dbl when double.IsFinite(dbl): w.WriteNumberValue(dbl); break;
                    case decimal m: w.WriteNumberValue(m); break;
                    case DateTime dt: w.WriteStringValue(dt); break;
                    default: w.WriteStringValue(Format(r[c])); break;
                }
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteXlsx(SheetData d, string path, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>(d.Rows.Count);
        foreach (var r in d.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var dict = new Dictionary<string, object?>(d.Columns.Count);
            for (var c = 0; c < d.Columns.Count; c++) dict[d.Columns[c]] = r[c];
            rows.Add(dict);
        }
        // An empty data set still needs a header row, so write one blank-valued row's worth of columns.
        if (rows.Count == 0) rows.Add(d.Columns.ToDictionary(c => c, _ => (object?)null));
        MiniExcel.SaveAs(path, rows, printHeader: true, overwriteFile: true);
    }

    private static string ToText(SheetData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join('\t', d.Columns));
        foreach (var r in d.Rows) sb.AppendLine(string.Join('\t', r.Select(v => Format(v).Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "))));
        return sb.ToString();
    }

    private static string ToMarkdown(SheetData d)
    {
        static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", d.Columns.Select(Cell))).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", d.Columns.Select(_ => "---"))).AppendLine(" |");
        foreach (var r in d.Rows) sb.Append("| ").Append(string.Join(" | ", r.Select(v => Cell(Format(v))))).AppendLine(" |");
        return sb.ToString();
    }
}
