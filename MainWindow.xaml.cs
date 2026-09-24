using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CSVEditor.Models;
using CSVEditor.Services;
using CSVEditor.Views;
using Microsoft.Win32;

namespace CSVEditor;

public partial class MainWindow : Window
{
    private const double BaseFontSize = 13;
    private const double MinFontSize = 10;
    private const double MaxFontSize = 24;
    private const int ReparseConfirmRowLimit = 50_000;
    private const int MatchCountLimit = 20_000;

    /// <summary>下拉框里的预设分隔符，顺序与 XAML 中的条目一致。</summary>
    private static readonly string[] PresetDelimiters = [",", "\t", ";", "|"];

    private const int CustomDelimiterIndex = 4;

    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _findDebounce;

    private CsvDocument _document = new();
    private DocumentEditor _editor;
    private string? _clipboard;

    private bool _suspendDelimiterEvents;

    /// <summary>行号绑定的上下文（供行头通过 MultiBinding 取得文档信息）。</summary>
    public static readonly DependencyProperty RowNumberContextProperty =
        DependencyProperty.Register(
            nameof(RowNumberContext),
            typeof(CsvDocument),
            typeof(MainWindow),
            new PropertyMetadata(null));

    /// <summary>窗口圆角（Windows 11 为 8，旧系统为 0）。</summary>
    private static CornerRadius DesiredWindowCornerRadius => ThemeManager.WindowCornerRadius;

    // 单元格编辑：记录进入编辑前的原值，供撤销使用
    private int _editRowIndex = -1;
    private int _editColumnIndex = -1;
    private string? _editOriginalValue;
    private CellEdit? _pendingEdit;

    public MainWindow()
    {
        InitializeComponent();

        _editor = new DocumentEditor(_document);
        RowNumberContext = _document;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastHost.Visibility = Visibility.Collapsed;
        };

        _findDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _findDebounce.Tick += (_, _) =>
        {
            _findDebounce.Stop();
            UpdateMatchCount();
        };

        BuildCommandBindings();
        SetFontSize(UserSettings.LoadFontSize());
        ApplyWindowCornerRadius();
        ShowWelcome();
        SyncDelimiterBox();
        UpdateTitleBar();

        SourceInitialized += (_, _) => ThemeManager.ApplyWindowAppearance(this);
        Loaded += OnWindowLoaded;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    // ================================================================= 初始化

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        UpdateMaximizeGlyph();
    }

    // ================================================================= 自绘标题栏

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            // 最大化后底部的任务栏遮挡由 Window_StateChanged -> ApplyWorkAreaCompensation 处理
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        UpdateMaximizeGlyph();

        // 留白的计算依赖「客户区高」，而客户区高又会被留白改变，所以必须
        // 清空留白、等布局稳定后再算一次，避免两轮结果互相干扰。
        _compensationApplied = false;
        WindowFrame.Margin = default;
        ScheduleCompensationRetry();
    }

    private int _compensationAttempts;
    private bool _compensationApplied;

    /// <summary>等布局稳定后再计算留白（最多重试几次，避免死循环）。</summary>
    private void ScheduleCompensationRetry()
    {
        if (_compensationAttempts++ > 6)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyWorkAreaCompensation();
        };
        timer.Start();
    }

    /// <summary>
    /// 无边框窗口最大化时，系统给出的窗口矩形按**屏幕边界**（而非工作区）计算，
    /// 底部会多出任务栏那一条，状态栏就会被遮住；有时又已经贴合，再补留白反而多出空白。
    /// 这里量出真正需要补的留白。
    ///
    /// 全部计算只用一个参考点：<see cref="UIElement.PointToScreen"/> 给出的
    /// 窗口内容左上角屏幕坐标——它与「屏幕高 / 工作区高」属于同一坐标口径，
    /// 因此不需要任何 DPI 或虚拟化的换算技巧，也就不会算错。
    ///
    ///   内容底边 = 参考点 Y + (客户区高 − 当前留白) × dipScale
    ///   工作区底边 = 屏幕高 − 任务栏高        （任务栏高 = 屏幕高 − 工作区高）
    ///   新留白 = (内容底边 − 工作区底边) / dipScale
    ///
    /// 说明：dipScale 只用于把布局单位（DIP）换算到 PointToScreen 的坐标口径。
    /// 之前几版之所以反复出错，是因为把 DWM 真实像素、Win32 虚拟化像素与 WPF 布局单位
    /// 三者混在一处相减（实测同一窗口分别为 1920x1200 / 1549x973 / 1548.8x972.8）。
    /// </summary>
    private void ApplyWorkAreaCompensation()
    {
        if (WindowState != WindowState.Maximized)
        {
            WindowFrame.Margin = default;
            return;
        }

        if (ActualHeight <= 0 || !WindowFrame.IsVisible)
        {
            ScheduleCompensationRetry();
            return;
        }

        // 只算一次：留白会改变 ActualHeight，重复计算会来回震荡
        if (_compensationApplied)
        {
            return;
        }

        _compensationApplied = true;

        // 参考点：内容左上角的屏幕坐标
        Point originOnScreen;
        try
        {
            originOnScreen = PointToScreen(WindowFrame.TransformToAncestor(this).Transform(new Point(0, 0)));
        }
        catch (InvalidOperationException)
        {
            _compensationApplied = false;
            ScheduleCompensationRetry();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var dipScale = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;

        // 内容底边：去掉留白后内容铺满客户区
        var contentHeightDips = ActualHeight - WindowFrame.Margin.Bottom;
        var contentBottomOnScreen = originOnScreen.Y + contentHeightDips * dipScale;

        // 屏幕底边与工作区底边（同一口径）
        var screenHeight = SystemParameters.PrimaryScreenHeight * dipScale;
        var taskbarHeight = Math.Max(0, SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height) * dipScale;
        var workBottom = screenHeight - taskbarHeight;

        var target = Math.Clamp((contentBottomOnScreen - workBottom) / dipScale, 0, 400);

        WindowFrame.Margin = target > 0.5 ? new Thickness(0, 0, 0, target) : default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    /// <summary>最大化时按钮显示“还原”图标，并去掉窗口圆角。</summary>
    private void UpdateMaximizeGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "向下还原" : "最大化";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "向下还原" : "最大化");
        ApplyWindowCornerRadius();
    }

    /// <summary>同步自绘标题栏上的文件名与未保存标记。</summary>
    private void UpdateTitleBar()
    {
        var dirty = _document.IsDirty ? " *" : string.Empty;
        TitleBarFileName.Text = _document.DisplayName + dirty;
        Title = $"{_document.DisplayName}{dirty} — CSV Editor";
    }

    private void BuildCommandBindings()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewDocument()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenFile()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveDocument(saveAs: false)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => SaveDocument(saveAs: true)));
        CommandBindings.Add(new CommandBinding(EditorCommands.SaveAs, (_, _) => SaveDocument(saveAs: true)));

        CommandBindings.Add(new CommandBinding(EditorCommands.ExportCsv, (_, _) => ExportAs("csv")));
        CommandBindings.Add(new CommandBinding(EditorCommands.ExportTsv, (_, _) => ExportAs("tsv")));
        CommandBindings.Add(new CommandBinding(EditorCommands.ExportJson, (_, _) => ExportAs("json")));
        CommandBindings.Add(new CommandBinding(EditorCommands.ExportExcel, (_, _) => ExportAs("xlsx")));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = HasDocument && _editor.CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = HasDocument && _editor.CanRedo));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => PasteFromClipboard(), (_, e) => e.CanExecute = HasDocument && Clipboard.ContainsText()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) => SelectAllCells(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.ClearCell, (_, _) => ClearSelection(), (_, e) => e.CanExecute = HasDocument));

        CommandBindings.Add(new CommandBinding(EditorCommands.InsertRowAbove, (_, _) => InsertRows(above: true), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.InsertRowBelow, (_, _) => InsertRows(above: false), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.InsertRowAtEnd, (_, _) => InsertRowAtEnd(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.DeleteRow, (_, _) => DeleteSelectedRows(), (_, e) => e.CanExecute = HasDocument));

        CommandBindings.Add(new CommandBinding(EditorCommands.InsertColumnBefore, (_, _) => InsertColumn(before: true), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.InsertColumnAfter, (_, _) => InsertColumn(before: false), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.DeleteColumn, (_, _) => DeleteSelectedColumns(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.RenameColumn, (_, _) => RenameCurrentColumn(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.AutoFitColumn, (_, _) => AutoFitColumn(CurrentColumnIndex), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.AutoFitAllColumns, (_, _) => AutoFitAllColumns(), (_, e) => e.CanExecute = HasDocument));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => OpenFindPanel(), (_, e) => e.CanExecute = true));
        CommandBindings.Add(new CommandBinding(EditorCommands.Replace, (_, _) => OpenFindPanel(focusReplace: true), (_, e) => e.CanExecute = true));
        CommandBindings.Add(new CommandBinding(EditorCommands.FindNext, (_, _) => FindNextMatch(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.FindPrevious, (_, _) => FindPreviousMatch(), (_, e) => e.CanExecute = HasDocument));
        CommandBindings.Add(new CommandBinding(EditorCommands.GoTo, (_, _) => GoToCell(), (_, e) => e.CanExecute = HasDocument));

        CommandBindings.Add(new CommandBinding(EditorCommands.ZoomIn, (_, _) => ChangeFontSize(+1)));
        CommandBindings.Add(new CommandBinding(EditorCommands.ZoomOut, (_, _) => ChangeFontSize(-1)));
        CommandBindings.Add(new CommandBinding(EditorCommands.ZoomReset, (_, _) => SetFontSize(BaseFontSize)));
    }

    // ================================================================= 文档生命周期

    public CsvDocument? RowNumberContext
    {
        get => (CsvDocument?)GetValue(RowNumberContextProperty);
        private set => SetValue(RowNumberContextProperty, value);
    }

    /// <summary>窗口圆角，直接设置在外层 RoundedBorder 上。</summary>
    private void ApplyWindowCornerRadius()
    {
        WindowFrame.CornerRadius = WindowState == WindowState.Maximized
            ? default
            : DesiredWindowCornerRadius;
    }

    private bool HasDocument => _document.ColumnCount > 0 || _document.RowCount > 0;

    private void AttachDocument(CsvDocument document)
    {
        _document.Changed -= OnDocumentChanged;
        _document = document;
        _document.Changed += OnDocumentChanged;
        _editor = new DocumentEditor(_document);
        RowNumberContext = document;
        BuildColumns();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => UpdateStatusBar();

    private void NewDocument()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var document = new CsvDocument();
        using (document.BeginLoad())
        {
            document.SetColumns([], 3, true);
            for (var r = 0; r < 10; r++)
            {
                document.Rows.Add(new CsvRow(document.ColumnCount));
            }
        }

        document.MarkSaved();
        AttachDocument(document);
        SyncDelimiterBox();
        ShowEditor();
        UpdateStatusBar();
        Notify("已新建空白表格");
    }

    /// <summary>启动时通过命令行参数打开的 CSV 文件。</summary>
    public void OpenInitialFile(string path)
    {
        if (!File.Exists(path))
        {
            ShowError($"找不到文件：\n{path}");
            return;
        }

        OpenFile(path);
    }

    private void OpenFile(string? path = null)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        if (path is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = "打开 CSV 文件",
                Filter = "CSV / 文本文件 (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|所有文件 (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            path = dialog.FileName;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var document = CsvFileService.Load(path);
            AttachDocument(document);
            SyncDelimiterBox();
            ShowEditor();
            UpdateStatusBar();
            Notify($"已打开 {document.DisplayName}（{document.RowCount:N0} 行 × {document.ColumnCount} 列）");
        }
        catch (Exception ex)
        {
            ShowError($"无法打开文件：\n{ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void LoadFromText(string text, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Notify("剪贴板中没有可用的文本");
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        try
        {
            var document = CsvFileService.FromText(text, name: name);
            AttachDocument(document);
            SyncDelimiterBox();
            ShowEditor();
            UpdateStatusBar();
            Notify($"已载入粘贴内容（{document.RowCount:N0} 行 × {document.ColumnCount} 列）");
        }
        catch (Exception ex)
        {
            ShowError($"无法解析粘贴内容：\n{ex.Message}");
        }
    }

    private void LoadSample()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var document = CsvFileService.FromText(SampleData.CreateCsv(), name: "示例数据.csv");
        AttachDocument(document);
        SyncDelimiterBox();
        ShowEditor();
        UpdateStatusBar();
        Notify("已载入示例数据，可直接编辑");
    }

    private bool SaveDocument(bool saveAs)
    {
        if (!HasDocument)
        {
            return false;
        }

        CommitPendingEdit();
        var path = _document.FilePath;

        if (saveAs || string.IsNullOrEmpty(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = saveAs ? "另存为" : "保存 CSV 文件",
                Filter = "CSV 文件 (*.csv)|*.csv|制表符分隔 (*.tsv)|*.tsv|文本文件 (*.txt)|*.txt",
                FileName = string.IsNullOrEmpty(path) ? "未命名.csv" : Path.GetFileName(path),
                DefaultExt = ".csv",
                AddExtension = true,
            };

            if (!string.IsNullOrEmpty(path))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(path);
            }

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            CsvFileService.Save(_document, path);
            UpdateStatusBar();
            Notify($"已保存到 {Path.GetFileName(path)}（{TextFileService.Describe(_document.Encoding, _document.EncodingHasBom)}）");
            return true;
        }
        catch (Exception ex)
        {
            ShowError($"保存失败：\n{ex.Message}");
            return false;
        }
    }

    private void ExportAs(string kind)
    {
        if (!HasDocument)
        {
            return;
        }

        var (filter, extension) = kind switch
        {
            "tsv" => ("制表符分隔 (*.tsv)|*.tsv", ".tsv"),
            "json" => ("JSON 文件 (*.json)|*.json", ".json"),
            "xlsx" => ("Excel 工作簿 (*.xlsx)|*.xlsx", ".xlsx"),
            _ => ("CSV 文件 (*.csv)|*.csv", ".csv"),
        };

        var baseName = Path.GetFileNameWithoutExtension(_document.DisplayName);
        var dialog = new SaveFileDialog
        {
            Title = "导出",
            Filter = filter,
            FileName = baseName + extension,
            DefaultExt = extension,
            AddExtension = true,
        };

        if (!string.IsNullOrEmpty(_document.FilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_document.FilePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var path = dialog.FileName;

            switch (kind)
            {
                case "json":
                    File.WriteAllText(path, ExportService.BuildJson(_document), new UTF8Encoding(false));
                    break;
                case "xlsx":
                    ExportService.ExportExcel(_document, path);
                    break;
                case "tsv":
                    File.WriteAllText(path, ExportService.BuildTsv(_document), new UTF8Encoding(true));
                    break;
                default:
                    File.WriteAllText(path, CsvFileService.BuildText(_document), _document.EncodingHasBom
                        ? TextFileService.WithBom(_document.Encoding)
                        : TextFileService.WithoutBom(_document.Encoding));
                    break;
            }

            Notify($"已导出到 {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            ShowError($"导出失败：\n{ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_document.IsDirty || !HasDocument)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            $"“{_document.DisplayName}”有未保存的修改，是否保存？",
            "CSV Editor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        UserSettings.SaveFontSize(Sheet.FontSize);
        base.OnClosing(e);
    }

    // ================================================================= 视图状态

    private void ShowWelcome()
    {
        WelcomeView.Visibility = Visibility.Visible;
        EditorView.Visibility = Visibility.Collapsed;
        ToolBarHost.Visibility = Visibility.Collapsed;
        SetFontSize(BaseFontSize);
    }

    private void ShowEditor()
    {
        WelcomeView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        ToolBarHost.Visibility = Visibility.Visible;
        Sheet.Focus();
    }

    private void UpdateStatusBar()
    {
        StatusRows.Text = $"{_document.RowCount:N0} 行";
        StatusColumns.Text = $"{_document.ColumnCount:N0} 列";
        StatusHeader.Text = _document.HasHeaderRow ? "含表头" : "无表头";
        StatusEncoding.Text = TextFileService.Describe(_document.Encoding, _document.EncodingHasBom);
        StatusDelimiter.Text = $"分隔符：{DescribeDelimiter(_document.Delimiter)}";

        UpdateTitleBar();

        UpdateSelectionStatus();
        UpdateFindInfo();
    }

    private void UpdateSelectionStatus()
    {
        if (!HasDocument)
        {
            StatusSelection.Text = "未选择";
            return;
        }

        var selected = Sheet.SelectedCells.Count;
        StatusSelection.Text = selected > 1
            ? $"已选 {selected:N0} 个单元格"
            : CurrentRowIndex >= 0 && CurrentColumnIndex >= 0
                ? $"R{DisplayRowNumber(CurrentRowIndex)} C{CurrentColumnIndex + 1}"
                : "未选择";
    }

    private static string DescribeDelimiter(string delimiter) => delimiter switch
    {
        "," => "逗号 (,)",
        "\t" => "制表符 (\\t)",
        ";" => "分号 (;)",
        "|" => "竖线 (|)",
        _ => $"“{delimiter}”",
    };

    private int DisplayRowNumber(int rowIndex) => _document.HasHeaderRow ? rowIndex + 2 : rowIndex + 1;

    private void Notify(string message)
    {
        StatusMessage.Text = message;
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "CSV Editor", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ================================================================= 表格构建

    private void BuildColumns()
    {
        Sheet.Columns.Clear();
        Sheet.ItemsSource = null;

        for (var c = 0; c < _document.ColumnCount; c++)
        {
            var header = _document.Columns[c];

            var column = new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(EstimateColumnWidth(c), DataGridLengthUnitType.Pixel),
                Binding = new Binding($"[{c}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                },
                ElementStyle = CreateCellTextStyle(),
                EditingElementStyle = (Style)FindResource("DataGridEditTextBox"),
                MinWidth = 48,
                MaxWidth = 900,
            };

            column.HeaderStyle = CreateHeaderStyle(c);
            Sheet.Columns.Add(column);
        }

        Sheet.ItemsSource = _document.Rows;
        UpdateRowHeaders();
        Sheet.UpdateLayout();
    }

    /// <summary>
    /// 把行号写回行对象：既让数据行的无障碍名称可读（DataGridRow 的自动化名称默认取 ToString），
    /// 也保证行列增删后行号语义仍然正确。
    /// </summary>
    private void UpdateRowHeaders()
    {
        for (var i = 0; i < _document.RowCount; i++)
        {
            _document.Rows[i].Header = DisplayRowNumber(i).ToString(CultureInfo.InvariantCulture);
        }
    }

    private Style CreateCellTextStyle() => new(typeof(TextBlock))
    {
        Setters =
        {
            new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis),
            new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
            new Setter(FrameworkElement.ToolTipProperty, new Binding(".")),
        },
    };

    private Style CreateHeaderStyle(int columnIndex)
    {
        var style = new Style(typeof(DataGridColumnHeader), (Style)FindResource("DataGridColumnHeaderStyle"));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, _document.Columns[columnIndex]));
        style.Setters.Add(new EventSetter(FrameworkElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(Header_Click)));
        style.Setters.Add(new EventSetter(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(Header_DoubleClick)));
        // DataGridColumnHeader 不会绑定到模板父级的 ContextMenu，因此在代码里挂载
        style.Setters.Add(new EventSetter(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(Header_ContextMenuOpening)));
        style.Setters.Add(new EventSetter(FrameworkElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(Header_RightButtonDown)));
        return style;
    }

    private void Header_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridColumnHeader header)
        {
            return;
        }

        var index = header.DisplayIndex;
        if (index < 0 || index >= _document.ColumnCount || _document.RowCount == 0)
        {
            return;
        }

        // 右键同时把当前列设为活动列，菜单命令才能作用于正确的列
        Sheet.CurrentCell = new DataGridCellInfo(_document.Rows[0], Sheet.Columns[index]);
        header.ContextMenu = (ContextMenu)FindResource("HeaderContextMenu");
    }

    private void Header_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is DataGridColumnHeader { ContextMenu: { } menu })
        {
            HeaderContextMenu_Opened(menu, new RoutedEventArgs());
            return;
        }

        e.Handled = true;
    }

    private double EstimateColumnWidth(int columnIndex)    {
        var font = new Typeface(
            (FontFamily)FindResource("Font.Mono"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widest = MeasureText(_document.Columns[columnIndex], font, BaseFontSize, dpi) + 34;

        var sampled = Math.Min(_document.RowCount, 40);
        for (var r = 0; r < sampled; r++)
        {
            var value = _document.Rows[r][columnIndex];
            if (value.Length is 0 or > 120)
            {
                continue;
            }

            widest = Math.Max(widest, MeasureText(value, font, BaseFontSize, dpi) + 26);
        }

        return Math.Clamp(widest, 88, 320);
    }

    private static double MeasureText(string text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private void ColumnHeaderRenamed(int columnIndex, string newName)
    {
        _editor.RenameColumn(columnIndex, newName);

        if (columnIndex < Sheet.Columns.Count)
        {
            Sheet.Columns[columnIndex].Header = _document.Columns[columnIndex];
            ((Style)Sheet.Columns[columnIndex].HeaderStyle).Setters
                .OfType<Setter>()
                .FirstOrDefault(s => s.Property == FrameworkElement.ToolTipProperty)!.Value = _document.Columns[columnIndex];
        }

        UpdateStatusBar();
        Notify($"列已重命名为“{_document.Columns[columnIndex]}”");
    }

    // ================================================================= 表格事件

    private void Sheet_Loaded(object sender, RoutedEventArgs e)
    {
        Sheet.Focus();
    }

    private void Sheet_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) =>
        e.Cancel = true;

    /// <summary>单元格右键菜单：根据当前选择启用 / 禁用条目。</summary>
    private void CellContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var hasSelection = SelectedCellCoordinates().Count > 0;
        var hasClipboard = false;

        try
        {
            hasClipboard = Clipboard.ContainsText();
        }
        catch (Exception)
        {
            hasClipboard = _clipboard is not null;
        }

        SetMenuEnabled(menu, EditorCommands.ClearCell, hasSelection);
        SetMenuEnabled(menu, EditorCommands.DeleteRow, hasSelection);
        SetMenuEnabled(menu, EditorCommands.InsertRowAbove, HasDocument);
        SetMenuEnabled(menu, EditorCommands.InsertRowBelow, HasDocument);
        SetMenuEnabled(menu, EditorCommands.InsertRowAtEnd, HasDocument);
        SetMenuEnabled(menu, ApplicationCommands.Copy, hasSelection);
        SetMenuEnabled(menu, ApplicationCommands.Paste, hasClipboard);
    }

    /// <summary>列头右键菜单。</summary>
    private void HeaderContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var index = CurrentColumnIndex;

        menu.DataContext = index;
        SetMenuEnabled(menu, EditorCommands.InsertColumnBefore, HasDocument);
        SetMenuEnabled(menu, EditorCommands.InsertColumnAfter, HasDocument);
        SetMenuEnabled(menu, EditorCommands.RenameColumn, index >= 0);
        SetMenuEnabled(menu, EditorCommands.DeleteColumn, index >= 0);
        SetMenuEnabled(menu, EditorCommands.AutoFitColumn, index >= 0);
    }

    private static void SetMenuEnabled(ContextMenu menu, ICommand command, bool enabled)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (ReferenceEquals(item.Command, command))
            {
                item.IsEnabled = enabled;
                return;
            }
        }
    }

    private void Sheet_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // 编辑期间隐藏气泡，避免遮挡
        ToastHost.Visibility = Visibility.Collapsed;

        _editRowIndex = Sheet.Items.IndexOf(e.Row.Item);
        _editColumnIndex = e.Column.DisplayIndex;

        if (_editRowIndex >= 0 && _editColumnIndex >= 0 && _editColumnIndex < _document.ColumnCount)
        {
            _editOriginalValue = _document.Rows[_editRowIndex][_editColumnIndex];
        }
        else
        {
            _editOriginalValue = null;
        }
    }

    private void Sheet_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void Sheet_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.EditingElement is not TextBox textBox)
        {
            return;
        }

        var rowIndex = _editRowIndex;
        var columnIndex = _editColumnIndex;
        var original = _editOriginalValue;

        _editRowIndex = -1;
        _editColumnIndex = -1;
        _editOriginalValue = null;

        if (rowIndex < 0 || columnIndex < 0 || columnIndex >= _document.ColumnCount || rowIndex >= _document.RowCount)
        {
            return;
        }

        var after = textBox.Text;
        if (string.Equals(original, after, StringComparison.Ordinal))
        {
            return;
        }

        // DataGrid 已经写入模型（含 PropertyChanged），这里只登记撤销记录，
        // 避免同一格触发两次通知造成虚拟化下的闪烁。
        _pendingEdit = new CellEdit(rowIndex, columnIndex, original, after);
    }

    private void Sheet_CurrentCellChanged(object sender, EventArgs e)
    {
        CommitPendingEdit();
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 把刚结束的单元格编辑登记进撤销栈。
    /// 保存 / 导出前也会调用一次，避免“改完最后一格直接保存”时丢失撤销记录。
    /// </summary>
    private void CommitPendingEdit()
    {
        if (_pendingEdit is not { } edit)
        {
            return;
        }

        _pendingEdit = null;
        _editor.ApplyEdits([edit], "编辑单元格");
        UpdateStatusBar();
    }

    private void Sheet_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionStatus();

    private void Sheet_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (FindPanel.Visibility == Visibility.Visible)
            {
                CloseFindPanel();
            }
            else
            {
                ToastHost.Visibility = Visibility.Collapsed;
            }

            e.Handled = true;
            return;
        }

        if (_editRowIndex >= 0 && _editColumnIndex >= 0)
        {
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                ClearSelection();
                e.Handled = true;
                break;

            case Key.F2:
                Sheet.BeginEdit();
                e.Handled = true;
                break;

            case Key.Enter when alt:
                InsertRows(above: false);
                e.Handled = true;
                break;

            case Key.Tab:
                MoveCurrentCell(control ? -1 : 1, 0, extendSelection: shift);
                e.Handled = true;
                break;

            case Key.Enter:
                MoveCurrentCell(0, shift ? -1 : 1, extendSelection: false);
                e.Handled = true;
                break;

            case Key.V when control:
                PasteFromClipboard();
                e.Handled = true;
                break;

            case Key.C when control:
                CopySelection();
                e.Handled = true;
                break;

            case Key.X when control:
                CopySelection();
                ClearSelection();
                e.Handled = true;
                break;
        }
    }

    private void MoveCurrentCell(int columnDelta, int rowDelta, bool extendSelection)
    {
        var row = Math.Clamp(CurrentRowIndex + rowDelta, 0, Math.Max(0, _document.RowCount - 1));
        var column = Math.Clamp(CurrentColumnIndex + columnDelta, 0, Math.Max(0, _document.ColumnCount - 1));

        if (row == CurrentRowIndex && column == CurrentColumnIndex)
        {
            return;
        }

        var target = Sheet.Columns[column];
        var item = _document.Rows[row];

        Sheet.CurrentCell = new DataGridCellInfo(item, target);
        Sheet.ScrollIntoView(item, target);

        if (!extendSelection)
        {
            Sheet.SelectedCells.Clear();
            Sheet.SelectedCells.Add(Sheet.CurrentCell);
        }

        UpdateSelectionStatus();
    }

    private void Header_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridColumnHeader header)
        {
            var index = header.DisplayIndex;
            if (index < 0 || index >= _document.ColumnCount)
            {
                return;
            }

            var firstRow = _document.RowCount > 0 ? _document.Rows[0] : null;
            if (firstRow is not null)
            {
                Sheet.CurrentCell = new DataGridCellInfo(firstRow, Sheet.Columns[index]);
            }

            if (e.ClickCount == 2)
            {
                RenameColumnAt(index);
                e.Handled = true;
            }
        }
    }

    private void Header_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridColumnHeader header)
        {
            RenameColumnAt(header.DisplayIndex);
            e.Handled = true;
        }
    }

    // ================================================================= 单元格操作

    private int CurrentRowIndex
    {
        get
        {
            // 注意：ItemsSource 被清空时 CurrentCell.Item 会是 UnsetValue，必须先做类型判断
            if (Sheet.CurrentCell.Item is CsvRow row)
            {
                return _document.Rows.IndexOf(row);
            }

            return -1;
        }
    }

    private int CurrentColumnIndex => Sheet.CurrentCell.Column?.DisplayIndex ?? -1;

    private IReadOnlyList<(int Row, int Column)> SelectedCellCoordinates()
    {
        var result = new List<(int, int)>(Sheet.SelectedCells.Count);

        foreach (var cell in Sheet.SelectedCells)
        {
            if (cell.Item is not CsvRow row)
            {
                continue;
            }

            var rowIndex = _document.Rows.IndexOf(row);
            var columnIndex = cell.Column?.DisplayIndex ?? -1;

            if (rowIndex >= 0 && columnIndex >= 0)
            {
                result.Add((rowIndex, columnIndex));
            }
        }

        if (result.Count == 0 && CurrentRowIndex >= 0 && CurrentColumnIndex >= 0)
        {
            result.Add((CurrentRowIndex, CurrentColumnIndex));
        }

        return result;
    }

    private void CopySelection()
    {
        var cells = SelectedCellCoordinates();
        if (cells.Count == 0)
        {
            return;
        }

        var rows = cells.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        var columns = cells.Select(c => c.Column).Distinct().OrderBy(c => c).ToList();

        var selected = cells.ToHashSet();
        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\t');
                }

                builder.Append(selected.Contains((row, columns[i]))
                    ? SanitizeForClipboard(_document.Rows[row][columns[i]])
                    : string.Empty);
            }

            builder.Append('\n');
        }

        _clipboard = builder.ToString();

        try
        {
            Clipboard.SetText(_clipboard);
        }
        catch (Exception)
        {
            // 剪贴板被其他进程占用时静默失败，内部剪贴板仍然可用
        }

        Notify($"已复制 {cells.Count:N0} 个单元格");
    }

    private static string SanitizeForClipboard(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private void PasteFromClipboard()
    {
        if (!HasDocument)
        {
            return;
        }

        string? text = null;

        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
        }
        catch (Exception)
        {
            text = _clipboard;
        }

        text ??= _clipboard;

        if (string.IsNullOrEmpty(text))
        {
            Notify("剪贴板中没有文本内容");
            return;
        }

        var startRow = Math.Max(0, CurrentRowIndex);
        var startColumn = Math.Max(0, CurrentColumnIndex);

        if (startRow < 0 || startColumn < 0)
        {
            Notify("请先选择一个起始单元格");
            return;
        }

        var block = ParseClipboardBlock(text);

        // 单行单列时按原样写入（保留内部换行符）
        if (block.Count == 1 && block[0].Length == 1)
        {
            var value = text.TrimEnd('\r', '\n');
            _editor.SetCell(startRow, startColumn, value, "粘贴");
            _document.Rows[startRow].RaiseChanged(startColumn);
            Notify("已粘贴 1 个单元格");
            UpdateStatusBar();
            return;
        }

        var edits = new List<CellEdit>();
        for (var r = 0; r < block.Count; r++)
        {
            var targetRow = startRow + r;
            if (targetRow >= _document.RowCount)
            {
                break;
            }

            for (var c = 0; c < block[r].Length; c++)
            {
                var targetColumn = startColumn + c;
                if (targetColumn >= _document.ColumnCount)
                {
                    break;
                }

                var before = _document.Rows[targetRow][targetColumn];
                if (!string.Equals(before, block[r][c], StringComparison.Ordinal))
                {
                    edits.Add(new CellEdit(targetRow, targetColumn, before, block[r][c]));
                }
            }
        }

        if (edits.Count == 0)
        {
            Notify("没有需要粘贴的内容");
            return;
        }

        _editor.ApplyEdits(edits, $"粘贴 {edits.Count} 个单元格");
        UpdateStatusBar();
        Notify($"已粘贴 {edits.Count:N0} 个单元格");
    }

    /// <summary>解析剪贴板内容为二维数组（兼容 Excel / 表格软件）。</summary>
    private static List<string[]> ParseClipboardBlock(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case '\t':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    private void ClearSelection()
    {
        var cells = SelectedCellCoordinates();
        if (cells.Count == 0)
        {
            return;
        }

        _editor.ClearCells(cells);
        foreach (var rowIndex in cells.Select(c => c.Row).Distinct())
        {
            _document.Rows[rowIndex].RaiseAllChanged();
        }

        UpdateStatusBar();
        Notify($"已清空 {cells.Count:N0} 个单元格");
    }

    private void SelectAllCells()
    {
        if (!HasDocument)
        {
            return;
        }

        Sheet.SelectAll();
        UpdateSelectionStatus();
    }

    private void GoToCell()
    {
        if (!HasDocument)
        {
            return;
        }

        var current = $"{(CurrentRowIndex >= 0 ? DisplayRowNumber(CurrentRowIndex) : 1)},{(CurrentColumnIndex >= 0 ? CurrentColumnIndex + 1 : 1)}";
        var result = PromptWindow.ShowPair(
            this,
            "跳转到指定单元格",
            "行号（含表头行）",
            current.Split(',')[0],
            "列号",
            current.Split(',')[1],
            "跳转");

        if (result is null)
        {
            return;
        }

        if (!int.TryParse(result.Value.First, out var displayRow) ||
            !int.TryParse(result.Value.Second, out var displayColumn))
        {
            ShowError("请输入有效的数字行列号。");
            return;
        }

        var rowIndex = _document.HasHeaderRow ? displayRow - 2 : displayRow - 1;
        var columnIndex = displayColumn - 1;

        if (rowIndex < 0 || rowIndex >= _document.RowCount || columnIndex < 0 || columnIndex >= _document.ColumnCount)
        {
            ShowError($"超出范围：当前表格共 {_document.RowCount:N0} 行数据、{_document.ColumnCount} 列。");
            return;
        }

        var target = Sheet.Columns[columnIndex];
        var item = _document.Rows[rowIndex];
        Sheet.CurrentCell = new DataGridCellInfo(item, target);
        Sheet.ScrollIntoView(item, target);
        Sheet.SelectedCells.Clear();
        Sheet.SelectedCells.Add(Sheet.CurrentCell);
        Sheet.Focus();
        UpdateSelectionStatus();
    }

    // ================================================================= 行列增删

    private void InsertRows(bool above)
    {
        if (!HasDocument)
        {
            return;
        }

        var index = CurrentRowIndex;
        index = index < 0 ? _document.RowCount : above ? index : index + 1;

        _editor.InsertRows(index);
        UpdateStatusBar();
        Notify($"已在第 {DisplayRowNumber(Math.Min(index, Math.Max(0, _document.RowCount - 1)))} 行前插入空行");
    }

    private void InsertRowAtEnd()
    {
        if (!HasDocument)
        {
            return;
        }

        _editor.InsertRows(_document.RowCount);
        var last = _document.RowCount - 1;
        var item = _document.Rows[last];
        Sheet.CurrentCell = new DataGridCellInfo(item, Sheet.Columns[Math.Max(0, CurrentColumnIndex)]);
        Sheet.ScrollIntoView(item, Sheet.Columns[Math.Max(0, CurrentColumnIndex)]);
        UpdateStatusBar();
        Notify("已在末尾追加空行");
    }

    private void DeleteSelectedRows()
    {
        if (!HasDocument)
        {
            return;
        }

        var rows = SelectedCellCoordinates().Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        if (rows.Count == 0)
        {
            Notify("请先选择要删除的行");
            return;
        }

        if (rows.Count == _document.RowCount)
        {
            var confirm = MessageBox.Show(
                this,
                "这将删除表格中的全部数据行，是否继续？",
                "CSV Editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _editor.DeleteRows(rows);
        UpdateStatusBar();
        Notify($"已删除 {rows.Count:N0} 行");
    }

    private void InsertColumn(bool before)
    {
        if (!HasDocument)
        {
            return;
        }

        var index = CurrentColumnIndex;
        index = index < 0 ? _document.ColumnCount : before ? index : index + 1;

        _editor.InsertColumns(index);
        BuildColumns();
        UpdateStatusBar();
        Notify($"已插入列“{_document.Columns[Math.Min(index, _document.ColumnCount - 1)]}”");
    }

    private void DeleteSelectedColumns()
    {
        if (!HasDocument)
        {
            return;
        }

        var columns = SelectedCellCoordinates().Select(c => c.Column).Distinct().OrderBy(c => c).ToList();
        if (columns.Count == 0)
        {
            Notify("请先选择要删除的列");
            return;
        }

        if (columns.Count >= _document.ColumnCount)
        {
            var confirm = MessageBox.Show(
                this,
                "这将删除表格中的全部列，是否继续？",
                "CSV Editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _editor.DeleteColumns(columns);
        BuildColumns();
        UpdateStatusBar();
        Notify($"已删除 {columns.Count:N0} 列");
    }

    private void RenameCurrentColumn()
    {
        var index = CurrentColumnIndex;
        if (index < 0)
        {
            Notify("请先选择该列中的任意单元格");
            return;
        }

        RenameColumnAt(index);
    }

    private void RenameColumnAt(int index)
    {
        if (index < 0 || index >= _document.ColumnCount)
        {
            return;
        }

        var result = PromptWindow.Show(
            this,
            "重命名列",
            "列名",
            _document.Columns[index],
            "重命名");

        if (result is null)
        {
            return;
        }

        ColumnHeaderRenamed(index, result);
        Sheet.Focus();
    }

    private void AutoFitColumn(int index)
    {
        if (index < 0 || index >= Sheet.Columns.Count)
        {
            return;
        }

        Sheet.Columns[index].Width = new DataGridLength(EstimateColumnWidth(index), DataGridLengthUnitType.Pixel);
        Notify("已按内容调整列宽");
    }

    private void AutoFitAllColumns()
    {
        for (var c = 0; c < Sheet.Columns.Count; c++)
        {
            Sheet.Columns[c].Width = new DataGridLength(EstimateColumnWidth(c), DataGridLengthUnitType.Pixel);
        }

        Notify("已按内容调整所有列宽");
    }

    // ================================================================= 查找 / 替换

    private FindOptions CurrentFindOptions => new(
        FindBox.Text,
        MatchCaseCheck.IsChecked == true,
        WholeWordCheck.IsChecked == true,
        RegexCheck.IsChecked == true);

    private void OpenFindPanel(bool focusReplace = false)
    {
        if (!HasDocument)
        {
            return;
        }

        FindPanel.Visibility = Visibility.Visible;
        FindToggle.IsChecked = true;

        if (focusReplace)
        {
            ReplaceBox.Focus();
            ReplaceBox.SelectAll();
        }
        else
        {
            FindBox.Focus();

            if (!string.IsNullOrEmpty(FindBox.Text))
            {
                FindBox.SelectAll();
            }
        }

        UpdateMatchCount();
    }

    private void CloseFindPanel()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        FindToggle.IsChecked = false;
        Sheet.Focus();
        UpdateSelectionStatus();
    }

    private void UpdateFindInfo()
    {
        if (FindPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        UpdateMatchCount();
    }

    private void UpdateMatchCount()
    {
        var options = CurrentFindOptions;

        if (options.IsEmpty || !HasDocument)
        {
            MatchInfo.Text = "0 个匹配";
            return;
        }

        var count = _editor.CountMatches(options, MatchCountLimit);
        MatchInfo.Text = count >= MatchCountLimit
            ? $"超过 {MatchCountLimit:N0} 个匹配"
            : $"{count:N0} 个匹配";
    }

    private void FindNextMatch()
    {
        var options = CurrentFindOptions;
        if (options.IsEmpty)
        {
            OpenFindPanel();
            return;
        }

        var index = CurrentRowIndex >= 0 && CurrentColumnIndex >= 0
            ? _editor.ToIndex(CurrentRowIndex, CurrentColumnIndex)
            : -1;

        var hit = _editor.ScanMatches(options, ref index, +1);

        if (hit is null)
        {
            Notify($"未找到“{options.Query}”");
            return;
        }

        var (row, column) = _editor.ToCoordinates(hit.Value);
        HighlightMatch(row, column);
    }

    private void FindPreviousMatch()
    {
        var options = CurrentFindOptions;
        if (options.IsEmpty)
        {
            OpenFindPanel();
            return;
        }

        var index = CurrentRowIndex >= 0 && CurrentColumnIndex >= 0
            ? _editor.ToIndex(CurrentRowIndex, CurrentColumnIndex) + 1
            : -1;

        var hit = _editor.ScanMatches(options, ref index, -1);

        if (hit is null)
        {
            Notify($"未找到“{options.Query}”");
            return;
        }

        var (row, column) = _editor.ToCoordinates(hit.Value);
        HighlightMatch(row, column);
    }

    private void HighlightMatch(int row, int column)
    {
        if (row < 0 || row >= _document.RowCount || column < 0 || column >= _document.ColumnCount)
        {
            return;
        }

        var target = Sheet.Columns[column];
        var item = _document.Rows[row];
        Sheet.CurrentCell = new DataGridCellInfo(item, target);
        Sheet.ScrollIntoView(item, target);
        Sheet.SelectedCells.Clear();
        Sheet.SelectedCells.Add(Sheet.CurrentCell);
        Sheet.Focus();
        UpdateSelectionStatus();
    }

    private void ReplaceOne_Click(object sender, RoutedEventArgs e) => ReplaceCurrentMatch();

    private void ReplaceCurrentMatch()
    {
        var options = CurrentFindOptions;
        if (options.IsEmpty)
        {
            return;
        }

        var row = CurrentRowIndex;
        var column = CurrentColumnIndex;

        if (row < 0 || column < 0 || !_editor.Matches(_document.Rows[row][column], options))
        {
            FindNextMatch();
            return;
        }

        var edit = _editor.ReplaceCurrent(options, row, column, ReplaceBox.Text);
        if (edit is not null)
        {
            _document.Rows[row].RaiseAllChanged();
            Notify("已替换 1 处");
            UpdateStatusBar();
        }

        FindNextMatch();
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var options = CurrentFindOptions;
        if (options.IsEmpty)
        {
            Notify("请输入要查找的内容");
            return;
        }

        var count = _editor.ReplaceAll(options, ReplaceBox.Text);
        Notify(count == 0 ? "没有可替换的内容" : $"已替换 {count:N0} 处");
        UpdateStatusBar();
    }

    private void FindToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FindToggle.IsChecked == true)
        {
            OpenFindPanel();
        }
        else
        {
            CloseFindPanel();
        }
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFindPanel();

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNextMatch();

    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindPreviousMatch();

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    FindPreviousMatch();
                }
                else
                {
                    FindNextMatch();
                }

                e.Handled = true;
                break;

            case Key.Escape:
                CloseFindPanel();
                e.Handled = true;
                break;
        }
    }

    private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseFindPanel();
            e.Handled = true;
        }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _findDebounce.Stop();
        _findDebounce.Start();
    }

    private void FindOption_Changed(object sender, RoutedEventArgs e) => UpdateMatchCount();

    // ================================================================= 撤销 / 主题 / 缩放

    private void Undo()
    {
        var description = _editor.Undo();
        if (description is null)
        {
            return;
        }

        if (_document.ColumnCount != Sheet.Columns.Count)
        {
            BuildColumns();
        }

        RefreshVisibleRows();
        UpdateStatusBar();
        Notify($"已撤销：{description}");
    }

    private void Redo()
    {
        var description = _editor.Redo();
        if (description is null)
        {
            return;
        }

        if (_document.ColumnCount != Sheet.Columns.Count)
        {
            BuildColumns();
        }

        RefreshVisibleRows();
        UpdateStatusBar();
        Notify($"已重做：{description}");
    }

    private void RefreshVisibleRows()
    {
        UpdateRowHeaders();
        Sheet.Items.Refresh();
        
    }

    private void ChangeFontSize(double delta) => SetFontSize(Sheet.FontSize + delta);

    private void SetFontSize(double size)
    {
        var clamped = Math.Clamp(size, MinFontSize, MaxFontSize);
        Sheet.FontSize = clamped;
        
    }

    // ================================================================= 拖放与剪贴板入口

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var hasFile = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFile ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            OpenFile(files[0]);
        }

        e.Handled = true;
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument(saveAs: false);

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        string? text = null;

        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
        }
        catch (Exception)
        {
            // 忽略剪贴板占用异常
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Notify("剪贴板中没有文本内容");
            return;
        }

        LoadFromText(text, "粘贴的数据.csv");
    }

    private void Sample_Click(object sender, RoutedEventArgs e) => LoadSample();

    private void DelimiterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendDelimiterEvents || DelimiterBox.SelectedIndex < 0)
        {
            return;
        }

        var index = DelimiterBox.SelectedIndex;

        // 「自定义…」：弹出输入框，选择被取消时回退到当前分隔符
        if (index == CustomDelimiterIndex)
        {
            var custom = PromptWindow.Show(
                this,
                "自定义分隔符",
                "分隔符（单个字符）",
                _document.Delimiter,
                "确定");

            if (string.IsNullOrEmpty(custom) || custom.Length != 1)
            {
                SyncDelimiterBox();
                return;
            }

            ApplyDelimiter(custom);
            return;
        }

        ApplyDelimiter(PresetDelimiters[index]);
    }

    private void ApplyDelimiter(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            SyncDelimiterBox();
            return;
        }

        var changed = !string.Equals(delimiter, _document.Delimiter, StringComparison.Ordinal);

        if (changed)
        {
            _document.Delimiter = delimiter;
            Notify($"分隔符已改为 {DescribeDelimiter(delimiter)}");
        }

        // 无论是否变化都刷新显示，保证下拉框始终反映当前分隔符
        SyncDelimiterBox();
        UpdateStatusBar();

        if (!changed || string.IsNullOrEmpty(_document.FilePath) || !File.Exists(_document.FilePath))
        {
            if (changed && HasDocument)
            {
                Notify("分隔符已记录，将用于保存与导出");
            }

            return;
        }

        // 已保存的文件可以按新分隔符重新解析，得到用户期望的列结构
        if (_document.IsDirty)
        {
            var confirm = MessageBox.Show(
                this,
                "按新的分隔符重新解析文件会丢失尚未保存的修改，是否继续？",
                "CSV Editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (_document.RowCount <= ReparseConfirmRowLimit ||
            MessageBox.Show(
                this,
                $"文件共有 {_document.RowCount:N0} 行，重新解析可能需要一些时间，是否继续？",
                "CSV Editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            OpenFile(_document.FilePath);
        }
    }

    /// <summary>让下拉框显示当前真正生效的分隔符。</summary>
    private void SyncDelimiterBox()
    {
        _suspendDelimiterEvents = true;

        try
        {
            var preset = Array.IndexOf(PresetDelimiters, _document.Delimiter);

            if (preset >= 0)
            {
                DelimiterBox.SelectedIndex = preset;
            }
            else
            {
                // 自定义分隔符：选中「自定义…」，并在提示里显示实际字符
                DelimiterBox.SelectedIndex = CustomDelimiterIndex;
            }

            DelimiterBox.ToolTip = $"当前分隔符：{DescribeDelimiter(_document.Delimiter)}";
        }
        finally
        {
            _suspendDelimiterEvents = false;
        }
    }

    // ================================================================= 菜单辅助

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        const string text = """
            文件
              Ctrl+N            新建表格
              Ctrl+O            打开文件
              Ctrl+S            保存
              Ctrl+Shift+S      另存为

            编辑
              Ctrl+Z / Ctrl+Y   撤销 / 重做
              Ctrl+C / Ctrl+V   复制 / 粘贴（支持从 Excel 粘入整块数据）
              Ctrl+X            剪切
              Delete            清空选中单元格
              F2 / 双击         编辑单元格
              Ctrl+A            全选

            行与列
              Alt+Enter         在下方插入行
              列头双击          重命名列
              列头右键          插入 / 删除 / 调整列宽

            查找与导航
              Ctrl+F            查找
              Ctrl+H            替换
              Ctrl+G            跳转到指定行列
              Enter / Shift+Enter   下一个 / 上一个匹配

            视图
              Ctrl+ + / Ctrl+ -   放大 / 缩小字号
              Ctrl+0              恢复默认字号
            """;

        MessageBox.Show(this, text, "快捷键一览", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            """
            科烽智能 CSV 编辑器

            · 稳定性高，不闪退
            · 性能优秀，十万行表格约 2 秒打开
            · 功能易用，打开 → 修改 → 保存
            · 无需联网，所有处理都在本机完成，数据不会上传
            · 绝不自动转换数据类型（前导零、日期、大数字原样保留）
            · 保存时保留原文件的编码与换行格式

            .NET 10 · WPF
            """,
            "关于 科烽智能 CSV 编辑器",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ================================================================= 工具方法
}
