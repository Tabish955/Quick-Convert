using QuickConvert.Engines.Docs.Spreadsheets;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Tests;

public class SpreadsheetConverterTests
{
    private readonly SpreadsheetConverter _c = new();

    private ToolResult Run(TempDir t, string name, string content, string target, bool overwrite = false)
    {
        var src = t.Sub(name);
        File.WriteAllText(src, content);
        return _c.Convert(new SpreadsheetRequest(src, target, t.Path, overwrite), null, default);
    }

    [Theory]
    [InlineData(".csv", ".json", true)]
    [InlineData(".json", ".csv", true)]
    [InlineData(".csv", ".xlsx", true)]
    [InlineData(".xlsx", ".csv", true)]
    [InlineData(".xlsx", ".json", true)]
    [InlineData(".txt", ".csv", true)]
    [InlineData(".csv", ".md", true)]
    [InlineData(".csv", ".csv", false)]
    [InlineData(".csv", ".pdf", false)]
    [InlineData(".json", ".yaml", false)]
    public void Pair_gating(string s, string t, bool expected) => Assert.Equal(expected, _c.CanConvert(s, t));

    [Fact]
    public void Csv_parser_handles_quotes_newlines_bom_and_blank_lines()
    {
        var rows = SpreadsheetConverter.ParseCsv("\uFEFFa,b\r\n\"x,1\",\"he said \"\"hi\"\"\"\r\n\r\n\"multi\nline\",z\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "x,1", "he said \"hi\"" }, rows[1]);
        Assert.Equal("multi\nline", rows[2][0]);
    }

    [Theory]
    [InlineData("a,b\n\"unclosed,1\n")]
    [InlineData("a,b\nab\"cd,1\n")]
    public void Malformed_csv_is_a_clean_failure_with_no_leftovers(string csv)
    {
        using var t = new TempDir();
        var r = Run(t, "bad.csv", csv, ".json");
        Assert.False(r.Success);
        Assert.Contains("malformed", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(t.Path, ".tmp_*"));
        Assert.False(File.Exists(t.Sub("bad.json")));
    }

    [Fact]
    public void Csv_to_json_to_csv_round_trips()
    {
        using var t = new TempDir();
        const string csv = "name,note\r\nAda,\"likes, commas\"\r\nBob,\"quote \"\"x\"\"\"\r\n";
        var j = Run(t, "people.csv", csv, ".json");
        Assert.True(j.Success, j.Message);
        Assert.Contains("\"name\": \"Ada\"", File.ReadAllText(j.OutputPath!));

        var back = _c.Convert(new SpreadsheetRequest(j.OutputPath!, ".csv", t.Sub("out"), false), null, default);
        Assert.True(back.Success, back.Message);
        var original = SpreadsheetConverter.ParseCsv(csv);
        var round = SpreadsheetConverter.ParseCsv(File.ReadAllText(back.OutputPath!));
        Assert.Equal(original.Select(r => string.Join("|", r)), round.Select(r => string.Join("|", r)));
    }

    [Fact]
    public void Csv_to_xlsx_and_back_keeps_headers_and_values()
    {
        using var t = new TempDir();
        var x = Run(t, "data.csv", "id,city\n1,Hyderabad\n2,Karachi\n", ".xlsx");
        Assert.True(x.Success, x.Message);
        Assert.True(new FileInfo(x.OutputPath!).Length > 0);
        // xlsx is a zip: structure assertion
        using (var z = System.IO.Compression.ZipFile.OpenRead(x.OutputPath!))
            Assert.Contains(z.Entries, e => e.FullName == "xl/workbook.xml");

        var back = _c.Convert(new SpreadsheetRequest(x.OutputPath!, ".csv", t.Sub("rt"), false), null, default);
        Assert.True(back.Success, back.Message);
        var rows = SpreadsheetConverter.ParseCsv(File.ReadAllText(back.OutputPath!));
        Assert.Equal(new[] { "id", "city" }, rows[0]);
        Assert.Equal("Hyderabad", rows[1][1]);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Json_with_mixed_keys_unions_columns_and_non_array_fails_clearly()
    {
        using var t = new TempDir();
        var ok = Run(t, "m.json", "[{\"a\":1},{\"b\":\"x\"}]", ".csv");
        Assert.True(ok.Success, ok.Message);
        var rows = SpreadsheetConverter.ParseCsv(File.ReadAllText(ok.OutputPath!));
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "" }, rows[1]);

        var notArray = Run(t, "o.json", "{\"a\":1}", ".csv");
        Assert.False(notArray.Success);
        Assert.Contains("array", notArray.Message);

        var broken = Run(t, "x.json", "[{\"a\":", ".csv");
        Assert.False(broken.Success);
        Assert.Empty(Directory.GetDirectories(t.Path, ".tmp_*"));
    }

    [Fact]
    public void Markdown_and_txt_outputs()
    {
        using var t = new TempDir();
        var md = Run(t, "t.csv", "a,b\n1,x|y\n", ".md");
        Assert.True(md.Success, md.Message);
        var text = File.ReadAllText(md.OutputPath!);
        Assert.Contains("| a | b |", text);
        Assert.Contains("x\\|y", text);

        var csv = Run(t, "lines.txt", "one\ntwo\n", ".csv");
        Assert.True(csv.Success, csv.Message);
        Assert.Equal(new[] { "text" }, SpreadsheetConverter.ParseCsv(File.ReadAllText(csv.OutputPath!))[0]);
    }

    [Fact]
    public void Empty_input_fails_and_existing_output_is_numbered()
    {
        using var t = new TempDir();
        Assert.False(Run(t, "empty.csv", "", ".json").Success);

        File.WriteAllText(t.Sub("d.json"), "keep");
        var r = Run(t, "d.csv", "a\n1\n", ".json");
        Assert.Equal(t.Sub("d (1).json"), r.OutputPath);
        Assert.Equal("keep", File.ReadAllText(t.Sub("d.json")));
    }

    [Fact]
    public void Duplicate_and_blank_headers_are_made_unique()
    {
        var d = SpreadsheetConverter.ReadCsv("a,a,\n1,2,3\n");
        Assert.Equal(new[] { "a", "a_2", "Column3" }, d.Columns);
    }
}
