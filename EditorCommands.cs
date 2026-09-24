using System.Windows.Input;

namespace CSVEditor;

/// <summary>编辑器自定义命令。</summary>
public static class EditorCommands
{
    private static readonly InputGestureCollection SaveAsGestures = new() { new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift) };

    // 文件
    public static readonly RoutedUICommand ExportCsv = Create("导出 CSV", nameof(ExportCsv));
    public static readonly RoutedUICommand ExportTsv = Create("导出 TSV", nameof(ExportTsv));
    public static readonly RoutedUICommand ExportJson = Create("导出 JSON", nameof(ExportJson));
    public static readonly RoutedUICommand ExportExcel = Create("导出 Excel", nameof(ExportExcel));

    // 行
    public static readonly RoutedUICommand InsertRowAbove = Create("在上方插入行", nameof(InsertRowAbove));
    public static readonly RoutedUICommand InsertRowBelow = Create("在下方插入行", nameof(InsertRowBelow));
    public static readonly RoutedUICommand InsertRowAtEnd = Create("在末尾追加行", nameof(InsertRowAtEnd));
    public static readonly RoutedUICommand DeleteRow = Create("删除行", nameof(DeleteRow));
    public static readonly RoutedUICommand ClearCell = Create("清空内容", nameof(ClearCell));

    // 列
    public static readonly RoutedUICommand InsertColumnBefore = Create("在左侧插入列", nameof(InsertColumnBefore));
    public static readonly RoutedUICommand InsertColumnAfter = Create("在右侧插入列", nameof(InsertColumnAfter));
    public static readonly RoutedUICommand DeleteColumn = Create("删除列", nameof(DeleteColumn));
    public static readonly RoutedUICommand RenameColumn = Create("重命名列", nameof(RenameColumn));
    public static readonly RoutedUICommand AutoFitColumn = Create("自动调整列宽", nameof(AutoFitColumn));
    public static readonly RoutedUICommand AutoFitAllColumns = Create("自动调整所有列宽", nameof(AutoFitAllColumns));

    // 查找 / 导航 / 视图
    public static readonly RoutedUICommand FindNext = Create("查找下一个", nameof(FindNext));
    public static readonly RoutedUICommand FindPrevious = Create("查找上一个", nameof(FindPrevious));
    public static readonly RoutedUICommand Replace = Create("替换", nameof(Replace));
    public static readonly RoutedUICommand GoTo = Create("跳转", nameof(GoTo));
    public static readonly RoutedUICommand ZoomIn = Create("放大", nameof(ZoomIn));
    public static readonly RoutedUICommand ZoomOut = Create("缩小", nameof(ZoomOut));
    public static readonly RoutedUICommand ZoomReset = Create("重置缩放", nameof(ZoomReset));

    /// <summary>另存为（带 Ctrl+Shift+S 手势）。</summary>
    public static readonly RoutedUICommand SaveAs =
        new("另存为", nameof(SaveAs), typeof(EditorCommands), SaveAsGestures);

    private static RoutedUICommand Create(string text, string name) => new(text, name, typeof(EditorCommands));
}
