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
            doc.Columns.Add(NoteColumn.Empty($"Column {i + 1}"));
        doc.RefreshPlainText();
        return doc;
    }

    public static NoteDocument Welcome()
    {
        var doc = Empty(1, "Welcome");
        var col = doc.Columns[0];
        col.Name = "Notes";
        col.Sections[0].Title = "Getting started";
        col.Sections[0].Color = "sage";
        col.Blocks =
        [
            NoteBlock.Paragraph("Welcome to ColumnNotes", bold: true, col.Sections[0].Id),
            NoteBlock.Paragraph("A local notepad with columns and checklists. Nothing leaves this device.", false, col.Sections[0].Id),
            NoteBlock.Paragraph("", false, col.Sections[0].Id),
            NoteBlock.Check("Turn selected lines into checkboxes with Ctrl+Shift+K", false, col.Sections[0].Id),
            NoteBlock.Check("Split the page from View → Columns, then rename a column", false, col.Sections[0].Id),
            NoteBlock.Check("Add a colored section inside a column", false, col.Sections[0].Id),
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
            var col = Columns[i];
            col.EnsureMeta(i);
            if (Columns.Count > 1) parts.Add($"--- {col.Name} ---");
            foreach (var b in col.Blocks)
            {
                var t = b.PlainText ?? "";
                parts.Add(b.Type == "check" ? $"{(b.IsChecked == true ? "[x]" : "[ ]")} {t}" : t);
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
            if (b.IsChecked == true) n++;
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
                for (var i = 0; i < doc.Columns.Count; i++)
                    doc.Columns[i].EnsureMeta(i);
                if (string.IsNullOrWhiteSpace(doc.Title)) doc.Title = fallbackTitle;
                return doc;
            }
        }
        catch
        {
            /* plain text */
        }

        var fromText = Empty(1, fallbackTitle);
        var sid = fromText.Columns[0].Sections[0].Id;
        fromText.Columns[0].Blocks = raw.Replace("\r\n", "\n").Split('\n')
            .Select(line => NoteBlock.FromLine(line, sid))
            .ToList();
        if (fromText.Columns[0].Blocks.Count == 0)
            fromText.Columns[0].Blocks.Add(NoteBlock.Paragraph("", false, sid));
        fromText.RefreshPlainText();
        return fromText;
    }

    public void SetColumnCount(int count)
    {
        count = Math.Clamp(count, 1, 12);
        if (count > Columns.Count)
        {
            while (Columns.Count < count) Columns.Add(NoteColumn.Empty($"Column {Columns.Count + 1}"));
        }
        else if (count < Columns.Count)
        {
            var keep = Columns.Take(count).ToList();
            var rest = Columns.Skip(count);
            foreach (var col in rest)
            {
                keep[^1].Sections.AddRange(col.Sections);
                foreach (var b in col.Blocks)
                {
                    if (b.Type == "paragraph" && string.IsNullOrWhiteSpace(b.PlainText)) continue;
                    keep[^1].Blocks.Add(b);
                }
            }
            if (keep[^1].Blocks.Count == 0) keep[^1].Blocks.Add(NoteBlock.Paragraph("", false, keep[^1].Sections[0].Id));
            Columns = keep;
        }
        RefreshPlainText();
    }

    public void AddSection(int columnIndex)
    {
        var col = Columns[Math.Clamp(columnIndex, 0, Columns.Count - 1)];
        col.EnsureMeta(columnIndex);
        var colors = NoteSection.Colors;
        var section = NoteSection.Create($"Section {col.Sections.Count + 1}", colors[col.Sections.Count % colors.Length]);
        col.Sections.Add(section);
        col.Blocks.Add(NoteBlock.Paragraph("", false, section.Id));
        RefreshPlainText();
    }

    public void DeleteSection(int columnIndex, string sectionId)
    {
        var col = Columns[Math.Clamp(columnIndex, 0, Columns.Count - 1)];
        col.Sections = col.Sections.Where(s => s.Id != sectionId).ToList();
        col.Blocks = col.Blocks.Where(b => b.SectionId != sectionId).ToList();
        if (col.Sections.Count == 0)
        {
            var fresh = NoteSection.Create("Section 1", "paper");
            col.Sections.Add(fresh);
            col.Blocks.Add(NoteBlock.Paragraph("", false, fresh.Id));
        }
        else if (col.Blocks.Count == 0)
            col.Blocks.Add(NoteBlock.Paragraph("", false, col.Sections[0].Id));
        RefreshPlainText();
    }

    public void InsertAfter(int columnIndex, string afterId, NoteBlock next)
    {
        var col = Columns[Math.Clamp(columnIndex, 0, Columns.Count - 1)];
        var idx = col.Blocks.FindIndex(b => b.Id == afterId);
        if (idx < 0) col.Blocks.Add(next);
        else col.Blocks.Insert(idx + 1, next);
        RefreshPlainText();
    }

    public void MoveSection(int fromCol, string sectionId, int toCol, string? beforeSectionId)
    {
        if (fromCol < 0 || toCol < 0 || fromCol >= Columns.Count || toCol >= Columns.Count) return;
        var src = Columns[fromCol];
        var section = src.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null) return;
        var moved = src.Blocks.Where(b => b.SectionId == sectionId).ToList();
        src.Sections = src.Sections.Where(s => s.Id != sectionId).ToList();
        src.Blocks = src.Blocks.Where(b => b.SectionId != sectionId).ToList();
        if (fromCol != toCol && src.Sections.Count == 0)
        {
            var fresh = NoteSection.Create("Section 1", "paper");
            src.Sections.Add(fresh);
            src.Blocks.Add(NoteBlock.Paragraph("", false, fresh.Id));
        }
        var dst = Columns[toCol];
        var at = beforeSectionId == null ? dst.Sections.Count : dst.Sections.FindIndex(s => s.Id == beforeSectionId);
        if (at < 0) at = dst.Sections.Count;
        dst.Sections.Insert(at, section);
        var insertAt = 0;
        if (at > 0)
        {
            var prevId = dst.Sections[at - 1].Id;
            var last = -1;
            for (var i = 0; i < dst.Blocks.Count; i++)
                if (dst.Blocks[i].SectionId == prevId) last = i;
            insertAt = last + 1;
        }
        if (moved.Count == 0) moved.Add(NoteBlock.Paragraph("", false, section.Id));
        dst.Blocks.InsertRange(insertAt, moved);
        RefreshPlainText();
    }

    public void MoveBlock(int fromCol, string blockId, int toCol, string toSectionId, string? beforeBlockId)
    {
        if (fromCol < 0 || toCol < 0 || fromCol >= Columns.Count || toCol >= Columns.Count) return;
        var src = Columns[fromCol];
        var block = src.Blocks.FirstOrDefault(b => b.Id == blockId);
        if (block == null) return;
        src.Blocks.Remove(block);
        if (src.Blocks.Count == 0)
            src.Blocks.Add(NoteBlock.Paragraph("", false, src.Sections.FirstOrDefault()?.Id));
        block.SectionId = toSectionId;
        var dst = Columns[toCol];
        var at = beforeBlockId == null ? -1 : dst.Blocks.FindIndex(b => b.Id == beforeBlockId);
        if (at < 0)
        {
            var last = -1;
            for (var i = 0; i < dst.Blocks.Count; i++)
                if (dst.Blocks[i].SectionId == toSectionId) last = i;
            at = last < 0 ? dst.Blocks.Count : last + 1;
        }
        dst.Blocks.Insert(at, block);
        RefreshPlainText();
    }

    static System.Text.Json.JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class NoteSection
{
    public static readonly string[] Colors = ["paper", "sage", "ochre", "slate", "rose", "mist"];

    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("title")] public string Title { get; set; } = "Section 1";
    [JsonPropertyName("color")] public string Color { get; set; } = "paper";

    public static NoteSection Create(string title, string color) => new() { Title = title, Color = color };
}

public sealed class NoteColumn
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "Column 1";
    [JsonPropertyName("sections")] public List<NoteSection> Sections { get; set; } = new();
    [JsonPropertyName("blocks")] public List<NoteBlock> Blocks { get; set; } = new();

    public static NoteColumn Empty(string name = "Column 1")
    {
        var section = NoteSection.Create("Section 1", "paper");
        return new NoteColumn
        {
            Name = name,
            Sections = [section],
            Blocks = [NoteBlock.Paragraph("", false, section.Id)]
        };
    }

    public void EnsureMeta(int index)
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = $"Column {index + 1}";
        if (Sections.Count == 0) Sections.Add(NoteSection.Create("Section 1", "paper"));
        var fallback = Sections[0].Id;
        foreach (var b in Blocks)
        {
            if (string.IsNullOrEmpty(b.Id)) b.Id = Guid.NewGuid().ToString();
            if (string.IsNullOrEmpty(b.SectionId) || Sections.All(s => s.Id != b.SectionId))
                b.SectionId = fallback;
        }
        if (Blocks.Count == 0) Blocks.Add(NoteBlock.Paragraph("", false, fallback));
    }
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
    [JsonPropertyName("sectionId")] public string? SectionId { get; set; }

    public static NoteBlock Paragraph(string text, bool bold = false, string? sectionId = null)
    {
        var html = bold ? $"<strong>{System.Net.WebUtility.HtmlEncode(text)}</strong>" : System.Net.WebUtility.HtmlEncode(text);
        return new NoteBlock { Type = "paragraph", Html = html, PlainText = text, SectionId = sectionId };
    }

    public static NoteBlock Check(string text, bool isChecked, string? sectionId = null)
    {
        return new NoteBlock
        {
            Type = "check",
            IsChecked = isChecked,
            Html = System.Net.WebUtility.HtmlEncode(text),
            PlainText = text,
            SectionId = sectionId
        };
    }

    public static NoteBlock FromLine(string line, string? sectionId = null)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*\[(x|X| )\]\s?(.*)$");
        if (m.Success) return Check(m.Groups[2].Value, m.Groups[1].Value is "x" or "X", sectionId);
        return Paragraph(line, false, sectionId);
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

public sealed class RichTextBoxAdapter
{
    public FlowDocument Document { get; }
    public RichTextBoxAdapter(FlowDocument document) => Document = document;
}
