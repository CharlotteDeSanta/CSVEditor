using CSVEditor.Models;

namespace CSVEditor.Services;

public readonly record struct CellEdit(int RowIndex, int ColumnIndex, string? Before, string? After);

public readonly record struct RemovedRow(int Index, CsvRow Row);

public readonly record struct RemovedColumn(int Index, string Name, string[] Values);

/// <summary>单元格编辑。</summary>
public sealed class CellEditCommand(CsvDocument document, CellEdit edit) : IUndoable
{
    public string Description => "编辑单元格";

    public void Execute() => Apply(edit.After);

    public void Undo() => Apply(edit.Before);

    private void Apply(string? value)
    {
        var row = document.Rows[edit.RowIndex];
        row[edit.ColumnIndex] = value ?? string.Empty;
        row.RaiseChanged(edit.ColumnIndex);
        document.MarkDirty();
    }
}

/// <summary>查找替换的批量结果（可整体撤销）。</summary>
public sealed class ReplaceAllCommand(CsvDocument document, IReadOnlyList<CellEdit> edits) : IUndoable
{
    public string Description => $"替换全部（{edits.Count} 处）";

    public void Execute() => Apply(isUndo: false);

    public void Undo() => Apply(isUndo: true);

    private void Apply(bool isUndo)
    {
        var previous = false;
        var currentRow = -1;

        foreach (var edit in edits)
        {
            if (edit.RowIndex != currentRow)
            {
                if (currentRow >= 0)
                {
                    document.Rows[currentRow].SuppressNotifications = previous;
                }

                currentRow = edit.RowIndex;
                previous = document.Rows[currentRow].SuppressNotifications;
                document.Rows[currentRow].SuppressNotifications = true;
            }

            document.Rows[edit.RowIndex].SetCellSilently(edit.ColumnIndex, (isUndo ? edit.Before : edit.After) ?? string.Empty);
        }

        if (currentRow >= 0)
        {
            var row = document.Rows[currentRow];
            row.SuppressNotifications = previous;
            row.RaiseAllChanged();
        }

        document.MarkDirty();
    }
}

/// <summary>插入列。</summary>
public sealed class InsertColumnsCommand(
    CsvDocument document,
    int index,
    IReadOnlyList<string> names,
    string defaultValue) : IUndoable
{
    public string Description => names.Count > 1 ? $"插入 {names.Count} 列" : "插入列";

    public void Execute()
    {
        for (var k = 0; k < names.Count; k++)
        {
            var at = Math.Clamp(index + k, 0, document.Columns.Count);
            document.Columns.Insert(at, names[k]);
            foreach (var row in document.Rows)
            {
                row.InsertCell(at, defaultValue);
            }
        }

        document.MarkDirty();
    }

    public void Undo()
    {
        for (var k = names.Count - 1; k >= 0; k--)
        {
            var at = Math.Clamp(index + k, 0, document.Columns.Count - 1);
            document.Columns.RemoveAt(at);
            foreach (var row in document.Rows)
            {
                row.RemoveCell(at);
            }
        }

        document.MarkDirty();
    }
}

/// <summary>删除列。</summary>
public sealed class DeleteColumnsCommand(CsvDocument document, IReadOnlyList<RemovedColumn> removed) : IUndoable
{
    public string Description => removed.Count > 1 ? $"删除 {removed.Count} 列" : "删除列";

    public void Execute()
    {
        foreach (var column in removed.OrderByDescending(c => c.Index))
        {
            foreach (var row in document.Rows)
            {
                row.RemoveCell(column.Index);
            }

            document.Columns.RemoveAt(column.Index);
        }

        document.MarkDirty();
    }

    public void Undo()
    {
        foreach (var column in removed.OrderBy(c => c.Index))
        {
            var at = Math.Clamp(column.Index, 0, document.Columns.Count);
            document.Columns.Insert(at, column.Name);

            for (var r = 0; r < document.Rows.Count; r++)
            {
                document.Rows[r].InsertCell(at, column.Values[r]);
            }
        }

        document.MarkDirty();
    }
}

/// <summary>重命名列。</summary>
public sealed class RenameColumnCommand(CsvDocument document, int index, string oldName, string newName) : IUndoable
{
    public string Description => "重命名列";

    public void Execute()
    {
        if (index >= 0 && index < document.Columns.Count)
        {
            document.Columns[index] = newName;
        }

        document.MarkDirty();
    }

    public void Undo()
    {
        if (index >= 0 && index < document.Columns.Count)
        {
            document.Columns[index] = oldName;
        }

        document.MarkDirty();
    }
}

/// <summary>在指定位置插入空行。</summary>
public sealed class InsertRowsCommand(CsvDocument document, int index, int count) : IUndoable
{
    public string Description => count > 1 ? $"插入 {count} 行" : "插入行";

    public void Execute()
    {
        var at = Math.Clamp(index, 0, document.Rows.Count);
        for (var i = 0; i < count; i++)
        {
            document.Rows.Insert(at, new CsvRow(document.ColumnCount));
        }

        document.MarkDirty();
    }

    public void Undo()
    {
        for (var i = 0; i < count; i++)
        {
            var at = Math.Clamp(index, 0, document.Rows.Count - 1);
            if (document.Rows.Count == 0)
            {
                break;
            }

            document.Rows.RemoveAt(at);
        }

        document.MarkDirty();
    }
}

/// <summary>删除行（含整行数据备份，可完整还原）。</summary>
public sealed class DeleteRowsCommand(CsvDocument document, IReadOnlyList<RemovedRow> removed) : IUndoable
{
    public string Description => removed.Count > 1 ? $"删除 {removed.Count} 行" : "删除行";

    public void Execute()
    {
        foreach (var item in removed.OrderByDescending(r => r.Index))
        {
            if (item.Index >= 0 && item.Index < document.Rows.Count)
            {
                document.Rows.RemoveAt(item.Index);
            }
        }

        document.MarkDirty();
    }

    public void Undo()
    {
        foreach (var item in removed.OrderBy(r => r.Index))
        {
            var at = Math.Clamp(item.Index, 0, document.Rows.Count);
            document.Rows.Insert(at, item.Row);
        }

        document.MarkDirty();
    }
}
