using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using CSVEditor.Models;

namespace CSVEditor.Services;

/// <summary>
/// CSV 文档模型：行集合 + 列头 + 分隔符 / 编码 / 换行符等格式信息 + 撤销栈。
/// </summary>
public sealed class CsvDocument : INotifyPropertyChanged
{
    private string _delimiter = ",";
    private string _quoteChar = "\"";
    private string _columnNamePrefix = "列";
    private string? _filePath;
    private Encoding _encoding = new UTF8Encoding(false);
    private bool _encodingHasBom;
    private LineEnding _lineEnding = LineEnding.Crlf;
    private QuotePolicy _quotePolicy = QuotePolicy.Minimal;
    private bool _hasHeaderRow = true;
    private bool _suppressDirty;

    public CsvDocument()
    {
        Columns = [];
        Rows = [];
        Undo = new UndoStack();
        Rows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RowCount));
            MarkDirty();
        };
        Columns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ColumnCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>内容或格式发生变化（供状态栏 / 标题栏刷新）。</summary>
    public event EventHandler? Changed;

    public ObservableCollection<CsvRow> Rows { get; }

    public ObservableCollection<string> Columns { get; }

    public UndoStack Undo { get; }

    public int RowCount => Rows.Count;

    public int ColumnCount => Columns.Count;

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => string.IsNullOrEmpty(_filePath)
        ? "未命名.csv"
        : Path.GetFileName(_filePath);

    public Encoding Encoding
    {
        get => _encoding;
        set => SetFormat(ref _encoding, value, nameof(Encoding));
    }

    /// <summary>原始文件是否带 BOM，用于“按原样保存”。</summary>
    public bool EncodingHasBom
    {
        get => _encodingHasBom;
        set => SetFormat(ref _encodingHasBom, value, nameof(EncodingHasBom));
    }

    public LineEnding LineEnding
    {
        get => _lineEnding;
        set => SetFormat(ref _lineEnding, value, nameof(LineEnding));
    }

    public string Delimiter
    {
        get => _delimiter;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? "," : value;
            SetFormat(ref _delimiter, normalized, nameof(Delimiter));
        }
    }

    public string QuoteChar
    {
        get => _quoteChar;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? "\"" : value[..1];
            SetFormat(ref _quoteChar, normalized, nameof(QuoteChar));
        }
    }

    public QuotePolicy QuotePolicy
    {
        get => _quotePolicy;
        set => SetFormat(ref _quotePolicy, value, nameof(QuotePolicy));
    }

    public bool HasHeaderRow
    {
        get => _hasHeaderRow;
        set => SetFormat(ref _hasHeaderRow, value, nameof(HasHeaderRow));
    }

    /// <summary>首次载入时使用的占位列名前缀。</summary>
    public string ColumnNamePrefix
    {
        get => _columnNamePrefix;
        set => _columnNamePrefix = value;
    }

    public bool IsDirty => Undo.IsDirty;

    public bool IsEmpty => Columns.Count == 0 && Rows.Count == 0;

    // ---------------------------------------------------------------- 变更通知

    public void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved()
    {
        Undo.MarkSaved();
        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>载入期间抑制脏标记与通知，大幅提升大文件载入速度。</summary>
    public IDisposable BeginLoad()
    {
        _suppressDirty = true;
        return new LoadScope(this);
    }

    private sealed class LoadScope(CsvDocument document) : IDisposable
    {
        public void Dispose()
        {
            document._suppressDirty = false;
            document.OnPropertyChanged(nameof(IsDirty));
            document.Changed?.Invoke(document, EventArgs.Empty);
        }
    }

    // ---------------------------------------------------------------- 列操作

    /// <summary>按分隔符解析结果为列头（含去重）。</summary>
    public void SetColumns(IEnumerable<string> names, int minimumCount, bool synthesizeWhenMissing)
    {
        Columns.Clear();
        var index = 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in names)
        {
            var name = string.IsNullOrWhiteSpace(raw) ? $"{ColumnNamePrefix}{index + 1}" : raw.Trim();
            while (!used.Add(name))
            {
                name = $"{name}_{index + 1}";
            }

            Columns.Add(name);
            index++;
        }

        if (synthesizeWhenMissing)
        {
            while (Columns.Count < minimumCount)
            {
                Columns.Add($"{ColumnNamePrefix}{Columns.Count + 1}");
            }
        }
    }

    /// <summary>把所有行补齐 / 裁剪到当前列数。</summary>
    public void NormalizeRows()
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.Count == ColumnCount)
            {
                continue;
            }

            var replacement = new CsvRow(ColumnCount);
            var copy = Math.Min(row.Count, ColumnCount);
            for (var c = 0; c < copy; c++)
            {
                replacement.SetCellSilently(c, row[c]);
            }

            Rows[i] = replacement;
        }
    }

    public IEnumerable<string> ColumnValues(int columnIndex)
    {
        foreach (var row in Rows)
        {
            yield return row[columnIndex];
        }
    }

    private void SetFormat<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        MarkDirty();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
