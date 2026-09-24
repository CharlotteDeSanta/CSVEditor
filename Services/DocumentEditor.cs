using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CSVEditor.Models;

namespace CSVEditor.Services;

public sealed record FindOptions(string Query, bool MatchCase, bool WholeWord, bool Regex)
{
    public static FindOptions Default { get; } = new(string.Empty, false, false, false);

    public bool IsEmpty => string.IsNullOrEmpty(Query);
}

/// <summary>
/// 对 <see cref="CsvDocument"/> 的全部修改入口：统一走撤销栈，保证可撤销、可重做。
/// </summary>
public sealed class DocumentEditor(CsvDocument document)
{
    private const int RegexTimeoutMs = 500;

    public CsvDocument Document { get; } = document;

    public bool CanUndo => Document.Undo.CanUndo;

    public bool CanRedo => Document.Undo.CanRedo;

    public string? Undo() => Document.Undo.Undo();

    public string? Redo() => Document.Undo.Redo();

    // ---------------------------------------------------------------- 单元格

    public CellEdit? SetCell(int rowIndex, int columnIndex, string? value, string description = "编辑单元格")
    {
        if (!IsValid(rowIndex, columnIndex))
        {
            return null;
        }

        var before = Document.Rows[rowIndex][columnIndex];
        var after = value ?? string.Empty;

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return null;
        }

        var edit = new CellEdit(rowIndex, columnIndex, before, after);
        var command = new CellEditCommand(Document, edit);
        command.Execute();
        Document.Undo.Push(command);
        return edit;
    }

    /// <summary>写入单元格但记录为一条批量操作（用于粘贴整块数据）。</summary>
    public void ApplyEdits(IReadOnlyList<CellEdit> edits, string description)
    {
        if (edits.Count == 0)
        {
            return;
        }

        var command = new ReplaceAllCommand(Document, edits);
        command.Execute();
        Document.Undo.Push(command);
    }

    public bool IsValid(int rowIndex, int columnIndex) =>
        rowIndex >= 0 && rowIndex < Document.Rows.Count &&
        columnIndex >= 0 && columnIndex < Document.ColumnCount;

    // ---------------------------------------------------------------- 行

    public void InsertRows(int index, int count = 1)
    {
        var command = new InsertRowsCommand(Document, index, count);
        command.Execute();
        Document.Undo.Push(command);
    }

    public void DeleteRows(IReadOnlyList<int> indexes)
    {
        var targets = indexes
            .Where(i => i >= 0 && i < Document.Rows.Count)
            .Distinct()
            .OrderBy(i => i)
            .Select(i => new RemovedRow(i, Document.Rows[i]))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        var command = new DeleteRowsCommand(Document, targets);
        command.Execute();
        Document.Undo.Push(command);
    }

    public void ClearCells(IReadOnlyList<(int Row, int Column)> cells)
    {
        var edits = new List<CellEdit>();
        foreach (var (row, column) in cells)
        {
            if (!IsValid(row, column))
            {
                continue;
            }

            var before = Document.Rows[row][column];
            if (before.Length > 0)
            {
                edits.Add(new CellEdit(row, column, before, string.Empty));
            }
        }

        ApplyEdits(edits, "清空单元格");
    }

    // ---------------------------------------------------------------- 列

    public void InsertColumns(int index, int count = 1)
    {
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            names.Add(UniqueColumnName(index + i + 1));
        }

        var command = new InsertColumnsCommand(Document, index, names, string.Empty);
        command.Execute();
        Document.Undo.Push(command);
    }

    public void DeleteColumns(IReadOnlyList<int> indexes)
    {
        var targets = indexes
            .Where(i => i >= 0 && i < Document.ColumnCount)
            .Distinct()
            .OrderBy(i => i)
            .Select(i => new RemovedColumn(
                i,
                Document.Columns[i],
                Document.Rows.Select(r => r[i]).ToArray()))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        var command = new DeleteColumnsCommand(Document, targets);
        command.Execute();
        Document.Undo.Push(command);
    }

    public void RenameColumn(int index, string newName)
    {
        if (index < 0 || index >= Document.ColumnCount)
        {
            return;
        }

        var oldName = Document.Columns[index];
        var candidate = string.IsNullOrWhiteSpace(newName) ? oldName : newName.Trim();
        if (string.Equals(oldName, candidate, StringComparison.Ordinal))
        {
            return;
        }

        var command = new RenameColumnCommand(Document, index, oldName, candidate);
        command.Execute();
        Document.Undo.Push(command);
    }

    private string UniqueColumnName(int ordinal)
    {
        var candidate = $"{Document.ColumnNamePrefix}{ordinal}";
        var existing = new HashSet<string>(Document.Columns, StringComparer.OrdinalIgnoreCase);
        var suffix = ordinal;

        while (!existing.Add(candidate))
        {
            suffix++;
            candidate = $"{Document.ColumnNamePrefix}{suffix}";
        }

        return candidate;
    }

    // ---------------------------------------------------------------- 查找 / 替换

    /// <summary>
    /// 从 <paramref name="fromIndex"/>（线性单元格下标，可为 -1）开始按 <paramref name="direction"/>（+1 / -1）
    /// 循环扫描，返回下一个命中；整表扫一遍没有命中则返回 null。
    /// </summary>
    public int? ScanMatches(FindOptions options, ref int fromIndex, int direction)
    {
        if (options.IsEmpty || Document.IsEmpty)
        {
            return null;
        }

        var totalColumns = Document.ColumnCount;
        var rows = Document.Rows;
        var total = rows.Count * totalColumns;

        if (total == 0)
        {
            return null;
        }

        if (fromIndex < -1)
        {
            fromIndex = -1;
        }

        if (fromIndex >= total)
        {
            fromIndex = total - 1;
        }

        for (var step = 1; step <= total; step++)
        {
            var index = fromIndex + direction * step;
            index = ((index % total) + total) % total;

            var row = index / totalColumns;
            var column = index % totalColumns;

            if (Matches(rows[row][column], options))
            {
                fromIndex = index;
                return index;
            }
        }

        return null;
    }

    public (int Row, int Column) ToCoordinates(int index) =>
        (index / Math.Max(1, Document.ColumnCount), index % Math.Max(1, Document.ColumnCount));

    public int ToIndex(int row, int column) => row * Math.Max(1, Document.ColumnCount) + column;

    /// <summary>统计匹配数量（上限用于避免超大数据集卡顿，默认不限制）。</summary>
    public int CountMatches(FindOptions options, int limit = int.MaxValue)
    {
        if (options.IsEmpty)
        {
            return 0;
        }

        var count = 0;
        foreach (var row in Document.Rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                if (Matches(row[c], options) && ++count >= limit)
                {
                    return count;
                }
            }
        }

        return count;
    }

    /// <summary>替换当前匹配项。</summary>
    public CellEdit? ReplaceCurrent(FindOptions options, int rowIndex, int columnIndex, string replacement)
    {
        if (!IsValid(rowIndex, columnIndex))
        {
            return null;
        }

        var value = Document.Rows[rowIndex][columnIndex];
        var replaced = ReplaceInValue(value, options, replacement, replaceAll: false);
        return replaced is null ? null : SetCell(rowIndex, columnIndex, replaced, "替换");
    }

    /// <summary>全部替换，返回替换的单元格数量。</summary>
    public int ReplaceAll(FindOptions options, string replacement)
    {
        if (options.IsEmpty)
        {
            return 0;
        }

        var edits = new List<CellEdit>();

        for (var r = 0; r < Document.Rows.Count; r++)
        {
            var row = Document.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                // 注意：必须先取出原值。ReplaceInValue 内部会更新字符串引用，
                // 若直接比较 row[c] 会得到“替换后等于自身”的错误结论。
                var original = row[c];
                var replaced = ReplaceInValue(original, options, replacement, replaceAll: true);

                if (replaced is not null && !string.Equals(replaced, original, StringComparison.Ordinal))
                {
                    edits.Add(new CellEdit(r, c, original, replaced));
                }
            }
        }

        if (edits.Count == 0)
        {
            return 0;
        }

        ApplyEdits(edits, $"替换全部（{edits.Count} 处）");
        return edits.Count;
    }

    public bool Matches(string value, FindOptions options)
    {
        if (options.IsEmpty || string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            if (options.Regex)
            {
                return BuildRegex(options).IsMatch(value);
            }

            var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var index = value.IndexOf(options.Query, comparison);

            while (index >= 0)
            {
                if (!options.WholeWord || IsWholeWord(value, index, options.Query.Length))
                {
                    return true;
                }

                index = value.IndexOf(options.Query, index + 1, comparison);
            }

            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? ReplaceInValue(string value, FindOptions options, string replacement, bool replaceAll)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            if (options.Regex)
            {
                var regex = BuildRegex(options);
                if (!regex.IsMatch(value))
                {
                    return null;
                }

                return replaceAll
                    ? regex.Replace(value, replacement)
                    : regex.Replace(value, replacement, 1);
            }

            var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var searchStart = 0;
            string? firstResult = null;

            while (true)
            {
                var index = value.IndexOf(options.Query, searchStart, comparison);
                if (index < 0)
                {
                    // 一次都没命中 -> null；命中过 -> 返回第一次替换后的完整内容
                    return firstResult;
                }

                if (!options.WholeWord || IsWholeWord(value, index, options.Query.Length))
                {
                    var result = string.Concat(
                        value.AsSpan(0, index),
                        replacement,
                        value.AsSpan(index + options.Query.Length));

                    firstResult ??= result;

                    if (!replaceAll)
                    {
                        return result;
                    }

                    // 继续在替换结果之后搜索，避免替换文本自身被再次匹配
                    searchStart = index + replacement.Length;
                    value = result;
                    continue;
                }

                searchStart = index + 1;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Regex BuildRegex(FindOptions options)
    {
        var pattern = options.WholeWord ? $@"\b(?:{options.Query})\b" : options.Query;
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        return new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(RegexTimeoutMs));
    }

    /// <summary>判断匹配是否为完整词（中英文都按“非字母数字下划线”作为边界）。</summary>
    private static bool IsWholeWord(string value, int index, int length)
    {
        var before = index == 0 || !IsWordChar(value[index - 1]);
        var afterIndex = index + length;
        var after = afterIndex >= value.Length || !IsWordChar(value[afterIndex]);
        return before && after;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
