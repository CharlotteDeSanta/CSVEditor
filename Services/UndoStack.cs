namespace CSVEditor.Services;

/// <summary>可撤销操作。</summary>
public interface IUndoable
{
    /// <summary>操作描述，用于菜单与提示（例如“编辑单元格”“插入列”）。</summary>
    string Description { get; }

    void Execute();

    void Undo();
}

/// <summary>
/// 撤销 / 重做栈。通过 <see cref="SavePoint"/> 判断文档是否有未保存的修改。
/// </summary>
public sealed class UndoStack
{
    private readonly List<IUndoable> _undo = [];
    private readonly List<IUndoable> _redo = [];
    private int _savePoint;

    public UndoStack(int capacity = 500)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? NextRedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    public bool IsDirty => _undo.Count != _savePoint;

    /// <summary>记录一个已执行的操作。</summary>
    public void Push(IUndoable operation)
    {
        _undo.Add(operation);
        _redo.Clear();

        if (_undo.Count > Capacity)
        {
            var overflow = _undo.Count - Capacity;
            _undo.RemoveRange(0, overflow);
            _savePoint = Math.Max(0, _savePoint - overflow);
        }
    }

    public string? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        var operation = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        operation.Undo();
        _redo.Add(operation);
        return operation.Description;
    }

    public string? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var operation = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        operation.Execute();
        _undo.Add(operation);
        return operation.Description;
    }

    /// <summary>把当前状态标记为“已保存”。</summary>
    public void MarkSaved() => _savePoint = _undo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savePoint = 0;
    }
}
