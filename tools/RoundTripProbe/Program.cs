using System.IO;
using System.Text;
using CSVEditor.Models;
using CSVEditor.Services;

namespace RoundTripProbe;

/// <summary>命令行自检：验证 CSV 解析 / 往返 / 撤销 / 导出等核心行为。</summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        RoundTripKeepsBytes();
        HandlesQuotedFieldsWithNewlines();
        PreservesLeadingZerosAndDates();
        DetectsDelimiters();
        UndoRedoWorks();
        RowAndColumnEditsWork();
        FindReplaceWorks();
        ExportsWork();
        DetectsEncoding();
        HandlesEmptyAndRaggedFiles();

        Console.WriteLine();
        Console.WriteLine($"passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 测试用例

    private static void RoundTripKeepsBytes()
    {
        var csv = SampleData.CreateCsv();
        var path = Temp("roundtrip.csv");
        File.WriteAllText(path, csv, new UTF8Encoding(false));

        var document = CsvFileService.Load(path);
        Check("往返：行数", document.RowCount == 13, $"{document.RowCount}");
        Check("往返：列数", document.ColumnCount == 8, $"{document.ColumnCount}");
        Check("往返：表头", document.Columns[0] == "产品编号", document.Columns[0]);

        var output = CsvFileService.BuildText(document);
        Check("往返：内容一致", output == csv, FirstDifference(csv, output));
    }

    private static void HandlesQuotedFieldsWithNewlines()
    {
        var text = "a,b\r\n\"line1\nline2\",\"quote \"\"x\"\"\"\r\n";
        var document = CsvFileService.FromText(text);

        Check("引号：行数", document.RowCount == 1, $"{document.RowCount}");
        Check("引号：字段内换行", document.Rows[0][0] == "line1\nline2", document.Rows[0][0]);
        Check("引号：转义引号", document.Rows[0][1] == "quote \"x\"", document.Rows[0][1]);
        Check("引号：往返一致", CsvFileService.BuildText(document) == text, CsvFileService.BuildText(document));
    }

    private static void PreservesLeadingZerosAndDates()
    {
        var text = "code,date,big\r\n00123,2024-03-15,12345678901234567890\r\n";
        var document = CsvFileService.FromText(text);

        Check("前导零保留", document.Rows[0][0] == "00123", document.Rows[0][0]);
        Check("日期保留", document.Rows[0][1] == "2024-03-15", document.Rows[0][1]);
        Check("大数字保留", document.Rows[0][2] == "12345678901234567890", document.Rows[0][2]);
    }

    private static void DetectsDelimiters()
    {
        Check("分隔符：逗号", CsvParser.DetectDelimiter("a,b,c\n1,2,3") == ",", "comma");
        Check("分隔符：制表符", CsvParser.DetectDelimiter("a\tb\tc\n1\t2\t3") == "\t", "tab");
        Check("分隔符：分号", CsvParser.DetectDelimiter("a;b;c\n1;2;3") == ";", "semicolon");

        var document = CsvFileService.FromText("a\tb\n1\t2\n");
        Check("分隔符：自动解析列数", document.ColumnCount == 2, $"{document.ColumnCount}");
    }

    private static void UndoRedoWorks()
    {
        var document = CsvFileService.FromText("a,b\n1,2\n");
        var editor = new DocumentEditor(document);

        editor.SetCell(0, 0, "changed");
        Check("撤销：编辑生效", document.Rows[0][0] == "changed", document.Rows[0][0]);
        Check("撤销：文档变脏", document.IsDirty, "dirty");

        editor.Undo();
        Check("撤销：恢复原值", document.Rows[0][0] == "1", document.Rows[0][0]);

        editor.Redo();
        Check("重做：再次生效", document.Rows[0][0] == "changed", document.Rows[0][0]);

        document.MarkSaved();
        Check("撤销：保存后不脏", !document.IsDirty, "clean");
    }

    private static void RowAndColumnEditsWork()
    {
        var document = CsvFileService.FromText("a,b\n1,2\n");
        var editor = new DocumentEditor(document);

        editor.InsertRows(1);
        Check("插入行", document.RowCount == 2, $"{document.RowCount}");
        editor.Undo();
        Check("撤销插入行", document.RowCount == 1, $"{document.RowCount}");

        editor.InsertColumns(1);
        Check("插入列：列数", document.ColumnCount == 3, $"{document.ColumnCount}");
        Check("插入列：行宽", document.Rows[0].Count == 3, $"{document.Rows[0].Count}");
        editor.Undo();
        Check("撤销插入列", document.ColumnCount == 2, $"{document.ColumnCount}");

        editor.DeleteColumns([0]);
        Check("删除列", document.ColumnCount == 1, $"{document.ColumnCount}");
        Check("删除列：数据保留", document.Rows[0][0] == "2", document.Rows[0][0]);
        editor.Undo();
        Check("撤销删除列：列名恢复", document.Columns[0] == "a", document.Columns[0]);
        Check("撤销删除列：数据恢复", document.Rows[0][0] == "1", document.Rows[0][0]);

        editor.DeleteRows([0]);
        Check("删除行", document.RowCount == 0, $"{document.RowCount}");
        editor.Undo();
        Check("撤销删除行", document.RowCount == 1, $"{document.RowCount}");

        editor.RenameColumn(0, "新列名");
        Check("重命名列", document.Columns[0] == "新列名", document.Columns[0]);
        editor.Undo();
        Check("撤销重命名", document.Columns[0] == "a", document.Columns[0]);
    }

    private static void FindReplaceWorks()
    {
        var document = CsvFileService.FromText("name,note\nAlice,bob@example.com\nBob,Alice\n");
        var editor = new DocumentEditor(document);

        // 单元格内容：Alice=2 处、bob@example.com、Bob
        var plain = new FindOptions("alice", false, false, false);
        Check("查找：忽略大小写计数", editor.CountMatches(plain) == 2, $"{editor.CountMatches(plain)}");

        var caseSensitive = new FindOptions("Alice", true, false, false);
        Check("查找：区分大小写计数", editor.CountMatches(caseSensitive) == 2, $"{editor.CountMatches(caseSensitive)}");

        var caseSensitiveLower = new FindOptions("alice", true, false, false);
        Check("查找：区分大小写（小写不匹配）", editor.CountMatches(caseSensitiveLower) == 0, $"{editor.CountMatches(caseSensitiveLower)}");

        var wholeWord = new FindOptions("Alice", false, true, false);
        Check("查找：全字匹配", editor.CountMatches(wholeWord) == 2, $"{editor.CountMatches(wholeWord)}");

        var subString = new FindOptions("bob", false, true, false);
        Check("查找：全字匹配（子串不命中）", editor.CountMatches(subString) == 2, $"{editor.CountMatches(subString)}");

        var regex = new FindOptions("^A", false, false, true);
        Check("查找：正则计数", editor.CountMatches(regex) == 2, $"{editor.CountMatches(regex)}");

        var replaced = editor.ReplaceAll(new FindOptions("alice", false, false, false), "Carol");
        Check("替换全部：数量", replaced == 2, $"{replaced}");
        Check("替换全部：内容", document.Rows[0][0] == "Carol", document.Rows[0][0]);
        Check("替换全部：未命中其他单元格", document.Rows[1][0] == "Bob", document.Rows[1][0]);

        editor.Undo();
        Check("替换全部：可撤销", document.Rows[0][0] == "Alice", document.Rows[0][0]);
        Check("替换全部：撤销后第二处", document.Rows[1][1] == "Alice", document.Rows[1][1]);

        // ScanMatches 从 fromIndex 的下一格开始按方向扫描，整表扫一遍没有命中返回 null
        var nextIndex = -1;
        var next = editor.ScanMatches(new FindOptions("Alice", false, false, false), ref nextIndex, +1);
        Check("查找下一个：从头命中第一处", next == 0, $"{next}");

        var secondIndex = nextIndex;
        var second = editor.ScanMatches(new FindOptions("Alice", false, false, false), ref secondIndex, +1);
        Check("查找下一个：第二处", second == 3, $"{second}");

        var bobIndex = 1;
        var nextBob = editor.ScanMatches(new FindOptions("Bob", false, false, false), ref bobIndex, +1);
        Check("查找下一个：指定起点", nextBob == 2, $"{nextBob}");

        var backIndex = 3;
        var previous = editor.ScanMatches(new FindOptions("Alice", false, false, false), ref backIndex, -1);
        Check("查找上一个", previous == 0, $"{previous}");

        editor.SetCell(1, 0, "Carol");
        Check("替换当前项：已改名", document.Rows[1][0] == "Carol", document.Rows[1][0]);

        var other = CsvFileService.FromText("a,b\nx,y\n");
        var otherEditor = new DocumentEditor(other);
        var missingIndex = -1;
        Check("查找下一个：未命中返回 null", otherEditor.ScanMatches(new FindOptions("zzz", false, false, false), ref missingIndex, +1) is null, "found");
        Check("查找计数：未命中为 0", otherEditor.CountMatches(new FindOptions("zzz", false, false, false)) == 0, "count");
    }

    private static void ExportsWork()
    {
        var document = CsvFileService.FromText("名称,数量\n\"含,逗号\",2\n");
        var json = ExportService.BuildJson(document);
        Check("导出 JSON", json.Contains("含,逗号") && json.Contains("\"数量\""), json.ReplaceLineEndings(" "));

        var tsv = ExportService.BuildTsv(document);
        Check("导出 TSV", tsv.StartsWith("名称\t数量", StringComparison.Ordinal), tsv.ReplaceLineEndings(" "));

        var xlsx = Temp("export.xlsx");
        ExportService.ExportExcel(document, xlsx);
        var bytes = File.ReadAllBytes(xlsx);
        Check("导出 Excel：ZIP 头", bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B, $"{bytes.Length} bytes");
        Check("导出 Excel：可解压", CanOpenZip(xlsx), "zip");
    }

    private static bool CanOpenZip(string path)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            return archive.GetEntry("xl/worksheets/sheet1.xml") is not null &&
                   archive.GetEntry("[Content_Types].xml") is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DetectsEncoding()
    {
        var utf8Bom = Temp("bom.csv");
        File.WriteAllText(utf8Bom, "a,b\n中文,2\n", new UTF8Encoding(true));
        var document = CsvFileService.Load(utf8Bom);
        Check("编码：UTF-8 BOM 识别", document.EncodingHasBom, "bom");
        Check("编码：中文解析", document.Rows[0][0] == "中文", document.Rows[0][0]);

        var gbk = Temp("gbk.csv");
        File.WriteAllBytes(gbk, Encoding.GetEncoding("GB18030").GetBytes("a,b\n中文,2\n"));
        var gbkDocument = CsvFileService.Load(gbk);
        Check("编码：GB18030 识别", gbkDocument.Rows[0][0] == "中文", gbkDocument.Rows[0][0]);

        var gbkOut = Temp("gbk-out.csv");
        CsvFileService.Save(gbkDocument, gbkOut);
        var reloaded = CsvFileService.Load(gbkOut);
        Check("编码：GB18030 往返", reloaded.Rows[0][0] == "中文", reloaded.Rows[0][0]);

        Check("换行符：CRLF", TextFileService.DetectLineEnding("a\r\nb\r\n") == LineEnding.Crlf, "crlf");
        Check("换行符：LF", TextFileService.DetectLineEnding("a\nb\n") == LineEnding.Lf, "lf");
    }

    private static void HandlesEmptyAndRaggedFiles()
    {
        var empty = CsvFileService.FromText(string.Empty);
        Check("空文件：不抛异常", empty.RowCount == 0, $"{empty.RowCount}");

        var ragged = CsvFileService.FromText("a,b,c\n1\n1,2,3,4\n");
        Check("不等长：列数为最大值", ragged.ColumnCount == 4, $"{ragged.ColumnCount}");
        Check("不等长：短行补齐", ragged.Rows[0].Count == 4, $"{ragged.Rows[0].Count}");
        Check("不等长：补齐为空", ragged.Rows[0][1] == string.Empty, "empty");

        var headerOnly = CsvFileService.FromText("a,b,c\n");
        Check("仅表头：行数为 0", headerOnly.RowCount == 0, $"{headerOnly.RowCount}");
        Check("仅表头：列数", headerOnly.ColumnCount == 3, $"{headerOnly.ColumnCount}");
    }

    // ---------------------------------------------------------------- 工具

    private static string Temp(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "csveditor-probe");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  OK   {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL {name} -> {detail}");
        }
    }

    private static string FirstDifference(string expected, string actual)
    {
        var limit = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < limit; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"位置 {i}：期望 '{Show(expected, i)}'，实际 '{Show(actual, i)}'";
            }
        }

        return $"长度不同：期望 {expected.Length}，实际 {actual.Length}";
    }

    private static string Show(string text, int index)
    {
        var start = Math.Max(0, index - 12);
        var length = Math.Min(28, text.Length - start);
        return text.Substring(start, length).ReplaceLineEndings("\\n");
    }
}
