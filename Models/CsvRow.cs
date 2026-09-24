using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace CSVEditor.Models;

/// <summary>
/// 一行 CSV 数据。各列值以字符串原样保存（与 Excel 不同，绝不自动转换数据类型），
/// 通过整数索引器暴露给 DataGrid 的 <c>Binding Path="[0]"</c>。
/// </summary>
public sealed class CsvRow : INotifyPropertyChanged, IEnumerable<string>
{
    private string[] _cells;

    public CsvRow(IEnumerable<string> cells)
    {
        _cells = cells.ToArray();
    }

    public CsvRow(int columnCount)
    {
        _cells = new string[columnCount];
        Array.Fill(_cells, string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>批量修改期间设为 true，避免逐格触发通知。</summary>
    public bool SuppressNotifications { get; set; }

    public int Count => _cells.Length;

    public string[] RawCells => _cells;

    /// <summary>无障碍 / 调试用名称（由列表演示层写入，例如行号）。</summary>
    public string? Header { get; set; }

    public override string ToString() => Header ?? $"CsvRow({_cells.Length} 列)";

    public string this[int index]
    {
        get => index >= 0 && index < _cells.Length ? _cells[index] : string.Empty;
        set
        {
            if (index < 0 || index >= _cells.Length)
            {
                return;
            }

            var normalized = value ?? string.Empty;
            if (string.Equals(_cells[index], normalized, StringComparison.Ordinal))
            {
                return;
            }

            _cells[index] = normalized;
            RaiseChanged(index);
        }
    }

    /// <summary>不触发通知的写入，用于批量载入与查找替换。</summary>
    public void SetCellSilently(int index, string value)
    {
        if (index >= 0 && index < _cells.Length)
        {
            _cells[index] = value ?? string.Empty;
        }
    }

    // ------------------------------------------------------------ 结构性增删

    /// <summary>在指定位置插入一个单元格（列结构变化时使用）。</summary>
    public void InsertCell(int index, string value)
    {
        var target = Math.Clamp(index, 0, _cells.Length);
        var next = new string[_cells.Length + 1];
        Array.Copy(_cells, 0, next, 0, target);
        next[target] = value ?? string.Empty;
        Array.Copy(_cells, target, next, target + 1, _cells.Length - target);
        _cells = next;
        RaiseAllChanged();
    }

    /// <summary>移除指定位置的单元格。</summary>
    public void RemoveCell(int index)
    {
        if (index < 0 || index >= _cells.Length || _cells.Length <= 1)
        {
            return;
        }

        var next = new string[_cells.Length - 1];
        Array.Copy(_cells, 0, next, 0, index);
        Array.Copy(_cells, index + 1, next, index, _cells.Length - index - 1);
        _cells = next;
        RaiseAllChanged();
    }

    /// <summary>批量替换后调用一次，刷新可视区域。</summary>
    public void RaiseAllChanged()
    {
        if (SuppressNotifications)
        {
            return;
        }

        for (var i = 0; i < _cells.Length; i++)
        {
            RaiseChanged(i);
        }
    }

    public void RaiseChanged(int index)
    {
        if (SuppressNotifications)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(
            $"Item[{index.ToString(CultureInfo.InvariantCulture)}]"));
    }

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_cells).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _cells.GetEnumerator();

    public string[] ToArray() => (string[])_cells.Clone();
}
