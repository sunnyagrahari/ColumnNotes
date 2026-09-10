using System.Net;
using System.Text;
using ColumnNotes.Models;

namespace ColumnNotes.Services;

public static class ExportService
{
    public static string ToMarkdown(NoteDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.Title}").AppendLine();
        for (var i = 0; i < doc.Columns.Count; i++)
        {
            if (doc.Columns.Count > 1) sb.AppendLine($"## {doc.Columns[i].Name}").AppendLine();
            foreach (var b in doc.Columns[i].Blocks)
            {
                if (b.Type == "check")
                    sb.AppendLine($"- [{(b.IsChecked == true ? "x" : " ")}] {b.PlainText}");
                else if (!string.IsNullOrWhiteSpace(b.PlainText))
                    sb.AppendLine(b.PlainText).AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string ToHtml(NoteDocument doc)
    {
        var cols = new StringBuilder();
        foreach (var col in doc.Columns)
        {
            cols.Append("<section>");
            foreach (var b in col.Blocks)
            {
                var body = string.IsNullOrEmpty(b.Html) ? WebUtility.HtmlEncode(b.PlainText) : b.Html;
                if (b.Type == "check")
                    cols.Append($"<div><input type='checkbox' disabled {(b.IsChecked == true ? "checked" : "")}/> {body}</div>");
                else
                    cols.Append($"<p>{body}</p>");
            }
            cols.Append("</section>");
        }
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>{WebUtility.HtmlEncode(doc.Title)}</title></head><body><h1>{WebUtility.HtmlEncode(doc.Title)}</h1><div style='display:flex;gap:24px'>{cols}</div></body></html>";
    }

    public static string ToRtf(NoteDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 ");
        sb.Append(Escape(doc.Title)).Append(@"\par\par ");
        foreach (var col in doc.Columns)
        {
            foreach (var b in col.Blocks)
            {
                if (b.Type == "check")
                    sb.Append(b.IsChecked == true ? "[x] " : "[ ] ");
                sb.Append(Escape(b.PlainText)).Append(@"\par ");
            }
            sb.Append(@"\par ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("\\", @"\\").Replace("{", @"\{").Replace("}", @"\}");
}
