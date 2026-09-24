using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CSVEditor.Models;

namespace CSVEditor.Services;

/// <summary>导出为 TSV / JSON / Excel(.xlsx) / 纯文本。</summary>
public static class ExportService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // ---------------------------------------------------------------- TSV / 文本

    /// <summary>生成 TSV（不含表头时可指定），同时用于剪贴板。</summary>
    public static string BuildTsv(CsvDocument document, bool includeHeader = true)
    {
        var builder = new StringBuilder();
        var lineEnding = document.LineEnding.ToLiteral();

        if (includeHeader)
        {
            builder.Append(string.Join('\t', document.Columns.Select(Sanitize))).Append(lineEnding);
        }

        foreach (var row in document.Rows)
        {
            builder.Append(string.Join('\t', row.Select(Sanitize))).Append(lineEnding);
        }

        return builder.ToString();
    }

    public static string BuildPlainText(CsvDocument document)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join('\t', document.Columns)).Append('\n');

        foreach (var row in document.Rows)
        {
            builder.Append(string.Join('\t', row)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // ---------------------------------------------------------------- JSON

    public static string BuildJson(CsvDocument document)
    {
        var records = new List<Dictionary<string, string>>(document.RowCount);

        foreach (var row in document.Rows)
        {
            var record = new Dictionary<string, string>(document.ColumnCount);
            for (var c = 0; c < document.ColumnCount; c++)
            {
                var key = document.Columns[c];
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = $"column{c + 1}";
                }

                if (!record.TryAdd(key, row[c]))
                {
                    var index = 2;
                    while (!record.TryAdd($"{key}_{index}", row[c]))
                    {
                        index++;
                    }
                }
            }

            records.Add(record);
        }

        return JsonSerializer.Serialize(records, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    // ---------------------------------------------------------------- Excel

    /// <summary>
    /// 生成最小可用的 OpenXML (.xlsx)。所有单元格都以文本写入，
    /// 因此不会出现 Excel 自动转换前导零 / 日期 / 大数字的问题。
    /// </summary>
    public static void ExportExcel(CsvDocument document, string path)
    {
        var sheet = BuildSheetXml(document);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);

        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """);

        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="2">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="11"/><name val="Calibri"/></font>
              </fonts>
              <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
              <borders count="1"><border/></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="2">
                <xf numFmtId="49" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                <xf numFmtId="49" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/>
              </cellXfs>
            </styleSheet>
            """);

        WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet);
    }

    private static string BuildSheetXml(CsvDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            """);

        // 列宽
        if (document.ColumnCount > 0)
        {
            builder.Append("<cols>");
            for (var c = 0; c < document.ColumnCount; c++)
            {
                var width = Math.Clamp(MeasureWidth(document, c), 9, 60);
                builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width:0.##}\" customWidth=\"1\"/>");
            }

            builder.Append("</cols>");
        }

        builder.Append("<sheetData>");

        // 表头（加粗）
        builder.Append("<row r=\"1\">");
        for (var c = 0; c < document.ColumnCount; c++)
        {
            AppendCell(builder, c, 1, document.Columns[c], styleIndex: 1);
        }

        builder.Append("</row>");

        for (var r = 0; r < document.Rows.Count; r++)
        {
            var row = document.Rows[r];
            var rowNumber = r + 2;
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");

            for (var c = 0; c < row.Count; c++)
            {
                AppendCell(builder, c, rowNumber, row[c], styleIndex: 0);
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static double MeasureWidth(CsvDocument document, int columnIndex)
    {
        var width = (double)document.Columns[columnIndex].Length;
        var sampled = Math.Min(document.RowCount, 200);

        for (var r = 0; r < sampled; r++)
        {
            width = Math.Max(width, document.Rows[r][columnIndex].Length);
        }

        return width * 1.1 + 3;
    }

    private static void AppendCell(StringBuilder builder, int columnIndex, int rowNumber, string value, int styleIndex)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var reference = $"{ColumnName(columnIndex)}{rowNumber}";
        builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{styleIndex}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
        builder.Append(EscapeXml(value));
        builder.Append("</t></is></c>");
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        var value = index + 1;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            name = (char)('A' + remainder) + name;
            value = (value - 1) / 26;
        }

        return name;
    }

    private static string EscapeXml(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&apos;");
                    break;
                default:
                    // 去掉 XML 1.0 不允许的控制字符
                    if (c < 0x20 && c is not ('\t' or '\n' or '\r'))
                    {
                        break;
                    }

                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8NoBom);
        writer.Write(content);
    }
}
