using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace ColumnNotes.Models;

public sealed class NoteDocument
{
    [JsonPropertyName("format")] public string Format { get; set; } = "cnotes";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("title")] public string Title { get; set; } = "Untitled";
    [JsonPropertyName("encoding")] public string Encoding { get; set; } = "UTF-8";
    [JsonPropertyName("wordWrap")] public bool WordWrap { get; set; } = true;
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("modifiedAt")] public string ModifiedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("plainText")] public string PlainText { get; set; } = "";
    [JsonPropertyName("columns")] public List<NoteColumn> Columns { get; set; } = new();

    public static NoteDocument Empty(int columnCount = 1, string title = "Untitled")
    {
        var doc = new NoteDocument { Title = title };
        for (var i = 0; i < Math.Clamp(columnCount, 1, 12); i++)
            doc.Columns.Add(NoteColumn.Empty());
        doc.RefreshPlainText();
        return doc;
    }

    public static NoteDocument Welcome()
    {
        var doc = Empty(1, "Welcome");
        doc.Columns[0].Blocks =
        [
            NoteBlock.Paragraph("Welcome to ColumnNotes", bold: true),
            NoteBlock.Paragraph("A local notepad with columns and checklists. Nothing leaves this device."),
            NoteBlock.Paragraph(""),
            NoteBlock.Check("Turn selected lines into checkboxes with Ctrl+Shift+K", false),
            NoteBlock.Check("Split the page from View → Columns", false),
            NoteBlock.Check("Press F5 to stamp the date and time", false),
        ];
        doc.RefreshPlainText();
        return doc;
    }

    public NoteDocument Clone()
    {
        return Parse(Serialize(this));
    }

    public void RefreshPlainText()
    {
        ModifiedAt = DateTime.UtcNow.ToString("O");
        var parts = new List<string>();
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns.Count > 1) parts.Add($"--- Column {i + 1} ---");
            foreach (var b in Columns[i].Blocks)
            {
                var t = b.PlainText ?? "";
                parts.Add(b.Type == "check" ? $"{(b.IsChecked ? "[x]" : "[ ]")} {t}" : t);
            }
            parts.Add("");
        }
        PlainText = string.Join("\n", parts).TrimEnd();
    }

    public (int checkedCount, int total) CheckboxStats()
    {
        var total = 0;
        var n = 0;
        foreach (var col in Columns)
        foreach (var b in col.Blocks)
        {
            if (b.Type != "check") continue;
            total++;
            if (b.IsChecked) n++;
        }
        return (n, total);
    }

    public static string Serialize(NoteDocument doc)
    {
        doc.RefreshPlainText();
        return System.Text.Json.JsonSerializer.Serialize(doc, JsonOpts());
    }

    public static NoteDocument Parse(string raw, string fallbackTitle = "Untitled")
    {
        raw = raw.TrimStart('\uFEFF');
        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize<NoteDocument>(raw, JsonOpts());
            if (doc?.Columns is { Count: > 0 })
            {
                foreach (var col in doc.Columns)
                {
                    if (string.IsNullOrEmpty(col.Id)) col.Id = Guid.NewGuid().ToString();
                    if (col.Blocks.Count == 0) col.Blocks.Add(NoteBlock.Paragraph(""));
                    foreach (var b in col.Blocks)
                    {
                        if (string.IsNullOrEmpty(b.Id)) b.Id = Guid.NewGuid().ToString();
                    }
                }
                if (string.IsNullOrWhiteSpace(doc.Title)) doc.Title = fallbackTitle;
                return doc;
            }
        }
        catch
        {
            /* plain text */
        }

        var fromText = Empty(1, fallbackTitle);
        fromText.Columns[0].Blocks = raw.Replace("\r\n", "\n").Split('\n')
            .Select(line => NoteBlock.FromLine(line))
            .ToList();
        if (fromText.Columns[0].Blocks.Count == 0)
            fromText.Columns[0].Blocks.Add(NoteBlock.Paragraph(""));
        fromText.RefreshPlainText();
        return fromText;
    }

    public void SetColumnCount(int count)
    {
        count = Math.Clamp(count, 1, 12);
        if (count > Columns.Count)
        {
            while (Columns.Count < count) Columns.Add(NoteColumn.Empty());
        }
        else if (count < Columns.Count)
        {
            var keep = Columns.Take(count).ToList();
            var rest = Columns.Skip(count);
            foreach (var col in rest)
            foreach (var b in col.Blocks)
            {
                if (b.Type == "paragraph" && string.IsNullOrWhiteSpace(b.PlainText)) continue;
                keep[^1].Blocks.Add(b);
            }
            if (keep[^1].Blocks.Count == 0) keep[^1].Blocks.Add(NoteBlock.Paragraph(""));
            Columns = keep;
        }
        RefreshPlainText();
    }

    static System.Text.Json.JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class NoteColumn
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("blocks")] public List<NoteBlock> Blocks { get; set; } = new();

    public static NoteColumn Empty() => new() { Blocks = [NoteBlock.Paragraph("")] };
}

public sealed class NoteBlock
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("type")] public string Type { get; set; } = "paragraph";
    [JsonPropertyName("isChecked")] public bool? IsChecked { get; set; }
    [JsonPropertyName("html")] public string Html { get; set; } = "";
    [JsonPropertyName("plainText")] public string PlainText { get; set; } = "";
    [JsonPropertyName("rtf")] public string? Rtf { get; set; }
    [JsonPropertyName("xaml")] public string? Xaml { get; set; }

    public static NoteBlock Paragraph(string text, bool bold = false)
    {
        var html = bold ? $"<strong>{System.Net.WebUtility.HtmlEncode(text)}</strong>" : System.Net.WebUtility.HtmlEncode(text);
        return new NoteBlock { Type = "paragraph", Html = html, PlainText = text };
    }

    public static NoteBlock Check(string text, bool isChecked)
    {
        return new NoteBlock
        {
            Type = "check",
            IsChecked = isChecked,
            Html = System.Net.WebUtility.HtmlEncode(text),
            PlainText = text
        };
    }

    public static NoteBlock FromLine(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*\[(x|X| )\]\s?(.*)$");
        if (m.Success) return Check(m.Groups[2].Value, m.Groups[1].Value is "x" or "X");
        return Paragraph(line);
    }

    public NoteBlock AsCheck()
    {
        Type = "check";
        IsChecked ??= false;
        Id = Guid.NewGuid().ToString();
        return this;
    }

    public NoteBlock AsParagraph()
    {
        Type = "paragraph";
        IsChecked = null;
        Id = Guid.NewGuid().ToString();
        return this;
    }

    public FlowDocument ToFlowDocument()
    {
        if (!string.IsNullOrWhiteSpace(Xaml))
        {
            try { return (FlowDocument)XamlReader.Parse(Xaml); }
            catch { /* fall through */ }
        }
        var doc = new FlowDocument();
        var p = new Paragraph(new Run(PlainText ?? ""));
        doc.Blocks.Add(p);
        return doc;
    }

    public void CaptureFrom(RichTextBoxAdapter box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        PlainText = range.Text.TrimEnd('\r', '\n');
        Html = System.Net.WebUtility.HtmlEncode(PlainText).Replace("\n", "<br/>");
        try { Xaml = XamlWriter.Save(box.Document); } catch { /* ignore */ }
        using var ms = new MemoryStream();
        range.Save(ms, System.Windows.DataFormats.Rtf);
        Rtf = System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>Thin adapter so model code can talk to a RichTextBox without leaking View types everywhere.</summary>
public sealed class RichTextBoxAdapter
{
    public FlowDocument Document { get; }
    public RichTextBoxAdapter(FlowDocument document) => Document = document;
}
