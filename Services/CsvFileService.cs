using System.IO;
using System.Text;
using CSVEditor.Models;

namespace CSVEditor.Services;

/// <summary>
/// CSV 读写与原始格式（编码 / BOM / 换行符）保留。
/// 与 Excel 不同，这里把每个字段都当作文本，绝不自动转换类型。
/// </summary>
public static class CsvFileService
{
    private static readonly string[] FieldChars = [",", "\"", "\r", "\n"];

    // ---------------------------------------------------------------- 读取

    public static CsvDocument Load(string path, string? delimiterOverride = null)
    {
        var bytes = File.ReadAllBytes(path);
        var text = TextFileService.Decode(bytes, out var encoding, out var hasBom);

        var lineEnding = TextFileService.DetectLineEnding(text);
        var delimiter = delimiterOverride ?? CsvParser.DetectDelimiter(Head(text, 64 * 1024));

        var parser = new CsvParser(delimiter, "\"");
        var records = parser.Parse(text.AsSpan());

        var document = new CsvDocument
        {
            FilePath = path,
        };

        using (document.BeginLoad())
        {
            ApplyRecords(document, records, delimiter);
            document.Encoding = encoding;
            document.EncodingHasBom = hasBom;
            document.LineEnding = lineEnding;
        }

        document.MarkSaved();
        return document;
    }

    /// <summary>从剪贴板 / 文本创建文档。</summary>
    public static CsvDocument FromText(string text, string? delimiterOverride = null, string? name = null)
    {
        var delimiter = delimiterOverride ?? CsvParser.DetectDelimiter(Head(text, 64 * 1024));
        var parser = new CsvParser(delimiter, "\"");
        var records = parser.Parse(text.AsSpan());

        var document = new CsvDocument
        {
            FilePath = name,
        };

        using (document.BeginLoad())
        {
            ApplyRecords(document, records, delimiter);
            document.Encoding = new UTF8Encoding(false);
            document.EncodingHasBom = false;
            document.LineEnding = TextFileService.DetectLineEnding(text);
        }

        document.MarkSaved();
        return document;
    }

    private static void ApplyRecords(CsvDocument document, List<string[]> records, string delimiter)
    {
        var columnCount = records.Count == 0 ? 0 : records.Max(r => r.Length);
        for (var i = 0; i < records.Count; i++)
        {
            columnCount = Math.Max(columnCount, records[i].Length);
        }

        if (columnCount == 0)
        {
            columnCount = 1;
        }

        document.Delimiter = delimiter;

        if (records.Count > 0)
        {
            document.SetColumns(records[0], columnCount, true);
            var start = document.HasHeaderRow ? 1 : 0;
            for (var i = start; i < records.Count; i++)
            {
                document.Rows.Add(new CsvRow(Pad(records[i], columnCount)));
            }
        }
        else
        {
            document.SetColumns([], columnCount, true);
        }

        document.NormalizeRows();
    }

    private static IEnumerable<string> Pad(string[] fields, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i < fields.Length ? fields[i] : string.Empty;
        }
    }

    // ---------------------------------------------------------------- 写入

    public static string BuildText(CsvDocument document)
    {
        var builder = new StringBuilder(EstimateSize(document));
        var lineEnding = document.LineEnding.ToLiteral();
        var delimiter = document.Delimiter;
        var quote = document.QuoteChar;
        var policy = document.QuotePolicy;

        builder.Append(JoinFields(document.Columns, delimiter, quote, policy));
        builder.Append(lineEnding);

        foreach (var row in document.Rows)
        {
            builder.Append(JoinFields(row, delimiter, quote, policy));
            builder.Append(lineEnding);
        }

        return builder.ToString();
    }

    private static string JoinFields(IEnumerable<string> fields, string delimiter, string quote, QuotePolicy policy)
    {
        var list = fields as IReadOnlyList<string> ?? fields.ToList();
        var builder = new StringBuilder(list.Count * 12);

        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(delimiter);
            }

            var value = list[i] ?? string.Empty;

            switch (policy)
            {
                case QuotePolicy.Always:
                    builder.Append(quote).Append(value.Replace(quote, quote + quote)).Append(quote);
                    break;

                case QuotePolicy.Never:
                    builder.Append(value);
                    break;

                default:
                    if (NeedsQuoting(value, delimiter, quote))
                    {
                        builder.Append(quote).Append(value.Replace(quote, quote + quote)).Append(quote);
                    }
                    else
                    {
                        builder.Append(value);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static bool NeedsQuoting(string value, string delimiter, string quote)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(quote) && value.Contains(quote, StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Contains(delimiter, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var fieldChar in FieldChars)
        {
            if (value.Contains(fieldChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return value[0] == ' ' || value[^1] == ' ';
    }

    public static void Save(CsvDocument document, string path)
    {
        var text = BuildText(document);
        TextFileService.Write(path, text, document.Encoding, document.EncodingHasBom);
        document.FilePath = path;
        document.MarkSaved();
    }

    private static int EstimateSize(CsvDocument document)
    {
        var estimated = Math.Max(256, document.ColumnCount * 12 + 2);
        return (int)Math.Min(int.MaxValue - 1, (long)estimated * (document.RowCount + 1) + 64);
    }

    private static string Head(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars];
}
