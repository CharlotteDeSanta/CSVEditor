# CSV Editor

一个 WPF（.NET 10）桌面 CSV 编辑器，界面设计参考 [csveditor.com](https://csveditor.com/zh/)：
现代深色界面、拖放即开、表格直接编辑，**所有解析与编辑都在本机完成，数据不会上传**。

与 Excel 最大的区别：**把每个字段都当作文本**，绝不自动转换类型。前导零（`00123`）、
日期（`2024-03-15`）、超大数字都会原样保留。

---

## 功能

### 打开数据
- **拖放文件**到窗口任意位置即可打开
- 工具栏「打开」/ `Ctrl+O`，支持 `*.csv`、`*.tsv`、`*.txt`
- **粘贴**：从剪贴板直接载入文本（自动识别分隔符）
- **试用示例数据**：内置覆盖各种边界情况的示例表
- 命令行：`CSVEditor.exe "D:\data\orders.csv"`

### 编辑
- 单元格直接编辑（双击 / `F2` / 直接打字），`Enter` 提交并下移
- 行：上方 / 下方插入、末尾追加、删除选中行（`Alt+Enter` 插入）
- 列：左侧 / 右侧插入、删除、**双击列头重命名**、按内容自动列宽
- 复制 / 剪切 / 粘贴：与 Excel 等表格软件互通（`Tab` 分隔的二维块）
- `Delete` 清空选中单元格
- 完整的**撤销 / 重做**（`Ctrl+Z` / `Ctrl+Y`），含行列增删、批量替换、粘贴

### 查找替换
- `Ctrl+F` 查找、`Ctrl+H` 替换
- 支持**区分大小写**、**全字匹配**、**正则表达式**
- 实时显示匹配数量，`Enter` / `Shift+Enter` 上下跳转，`Esc` 关闭

### 格式与导出
- 编码自动识别：UTF-8（含 BOM）/ UTF-16 / GBK / GB18030 / Big5
- 换行符（CRLF / LF / CR）与引号风格保留
- 分隔符可切换：逗号、制表符、分号、竖线或「自定义…」单字符；下拉框始终显示当前生效的分隔符
- 导出 CSV / TSV / JSON / Excel `.xlsx`（所有单元格以文本写入，Excel 打开也不会变形）

### 界面
- **无边框窗口 + 自绘标题栏**：标题栏与程序内风格统一（浅色、圆角、细描边），
  同时保留系统级窗口行为——拖拽移动、双击最大化/还原、边缘缩放、贴边分屏、Win+方向键
- Windows 11 下自动使用系统圆角与投影，旧系统自动退化为直角
- 浅色主题，文字对比度按 WCAG AA 调整（正文约 16:1，次要文字 ≥ 5:1）
- 大文件虚拟滚动：10 万行实测「进程启动 → 可交互」约 **1.8 秒**，内存约 300 MB
- 行号、斑马纹、列分隔线、字号缩放（`Ctrl+ +` / `Ctrl+ -` / `Ctrl+0`）

---

## 快速开始

```powershell
# 需要 .NET 10 SDK
dotnet build CSVEditor.csproj
dotnet run --project CSVEditor.csproj

# 直接打开某个文件
dotnet run --project CSVEditor.csproj -- "D:\data\orders.csv"
```

### 发布（x64 单文件，框架依赖）

```powershell
# 一条命令即可；发布配置写在 CSVEditor.csproj 的 Release 段里
dotnet publish CSVEditor.csproj -c Release
```

产物：`bin\Release\net10.0-windows\win-x64\publish\CSVEditor.exe`（**单文件，约 0.31 MB**）

- **x64**：`RuntimeIdentifier=win-x64` + `PlatformTarget=x64`
- **单文件**：`PublishSingleFile=true`，原生依赖也打进 exe（`IncludeNativeLibrariesForSelfExtract`）
- **不含框架**：`SelfContained=false` —— 目标机器需预装
  **.NET 10 桌面运行时（Microsoft.WindowsDesktop.App 10.x）**，下载：
  <https://dotnet.microsoft.com/download/dotnet/10.0>
- 未启用裁剪（`PublishTrimmed=false`）：WPF 依赖反射，裁剪会破坏 XAML 加载
- 调试构建不受影响：`RuntimeIdentifier` 只在 Release 配置下生效

Visual Studio 用户也可右键项目 → 发布 → 选择 `FolderProfile` 配置
（定义在 `Properties\PublishProfiles\FolderProfile.pubxml`，与本命令等价）。

---

## 快捷键

| 操作 | 快捷键 |
| --- | --- |
| 新建 / 打开 / 保存 / 另存为 | `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| 撤销 / 重做 | `Ctrl+Z` / `Ctrl+Y`（或 `Ctrl+Shift+Z`） |
| 复制 / 粘贴 / 剪切 | `Ctrl+C` / `Ctrl+V` / `Ctrl+X` |
| 编辑单元格 | 双击 / `F2` |
| 清空选中单元格 | `Delete` |
| 在当前行下方插入行 | `Alt+Enter` |
| 下一个单元格 / 下一行 | `Tab` / `Enter` |
| 查找 / 替换 / 跳转 | `Ctrl+F` / `Ctrl+H` / `Ctrl+G` |
| 全选 | `Ctrl+A` |
| 放大 / 缩小 / 重置字号 | `Ctrl+ +` / `Ctrl+ -` / `Ctrl+0` |

---

## 项目结构

```
CSVEditor.csproj            主程序（WPF, net10.0-windows）
App.xaml(.cs)               应用入口：注册代码页编码、异常日志、命令行参数
MainWindow.xaml(.cs)        主窗口：菜单栏 / 工具栏 / 欢迎页 / 表格 / 查找面板 / 状态栏
EditorCommands.cs           自定义 RoutedCommand
RowNumberConverter.cs       行号显示转换器

Models/
  CsvRow.cs                 一行数据（字符串数组 + 索引器绑定 + 列增删）
  Enums.cs                  引号策略、换行符风格

Services/
  CsvParser.cs              字符状态机解析器（引号转义 / 字段内换行 / 分隔符探测）
  CsvFileService.cs         CSV 读写（保留原始格式）
  TextFileService.cs        编码探测与按原样写回
  CsvDocument.cs            文档模型（行 / 列 / 格式 / 脏标记）
  UndoStack.cs              撤销栈与保存点
  Commands.cs               各类可撤销操作
  DocumentEditor.cs         所有修改的统一入口 + 查找替换引擎
  ExportService.cs          TSV / JSON / Excel(.xlsx) 导出
  ThemeManager.cs           主题切换
  UserSettings.cs           主题与字号持久化
  SampleData.cs             内置示例数据

Themes/
  Palette.Light.xaml        浅色调色板（本程序仅浅色主题）
  Typography.xaml           字体、字号、图标路径
  Controls.xaml             按钮 / 输入框 / 下拉框 / 表格 / 菜单 / 滚动条样式
  Widgets.xaml              工具栏按钮、卡片、徽标等组合样式

Controls/RoundedBorder.cs      圆角裁剪容器（无边框窗口下裁掉溢出内容）

Views/PromptWindow.xaml(.cs)  轻量输入对话框（重命名列 / 跳转行列 / 自定义分隔符）

tools/                      开发期自检（不参与主程序编译）
  RoundTripProbe/           67 项核心层自检：解析往返 / 撤销 / 查找 / 导出 / 编码
  Capture.ps1               截图：欢迎页与编辑界面
  Interact.ps1              交互自检：示例数据 / 查找面板 / 右键菜单
  VerifyFixes.ps1           回归自检：菜单弹出 / 分隔符下拉 / 查找 / 列头菜单
  CheckTitleBar.ps1         标题栏自检：拖拽移动 / 双击最大化还原 / 三个窗口按钮
  CheckMaximize.ps1         最大化几何自检：窗口矩形 vs 显示器工作区，底部内容不得被任务栏遮挡
  CaptureToast.ps1          抓取提示气泡（气泡只显示 2.2 秒）
  CheckDelimiter.ps1        分隔符下拉框选中效果（截屏）
  CheckGridInitial.ps1      网格线对齐：断言首帧绘制即与列边界一致（对照 UIA 列头矩形）
  MenuCheck.ps1             列头右键菜单检查
  LargeFile.ps1             10 万行大文件加载耗时统计
```

---

## 实现要点

- **无边框窗口**：`WindowStyle="None"` + `WindowChrome`（而不是 `AllowsTransparency`），
  这样既没有系统标题栏，又保留了系统的窗口行为与性能。要点：
  - `WindowChrome.CaptionHeight` 内的区域自动可拖拽、双击最大化，**无需手写拖拽逻辑**；
  - 标题栏里的按钮必须设 `WindowChrome.IsHitTestVisibleInChrome="True"`，否则点击会被 Chrome 吃掉；
  - `WindowChrome.CornerRadius` 保持 0，圆角交给 DWM（`DWMWA_WINDOW_CORNER_PREFERENCE`），
    否则最大化时会露出透明缺口；
  - 内容用自写的 `RoundedBorder` 裁剪——WPF 只按圆角画背景，**不会裁剪子元素**。
- **最大化不被任务栏遮挡**：系统给出的最大化矩形按**屏幕边界**计算，底部会多出任务栏那一条。
  WPF 的 `MaxWidth` / `MaxHeight` 对最大化矩形**不生效**，因此在 `StateChanged` 里算出留白
  加到内容区底部。全部计算只依赖一个参考点，避免多坐标系混用：

  ```csharp
  // 参考点：内容左上角的屏幕坐标（与「屏幕高/工作区高」同口径）
  var origin = PointToScreen(WindowFrame.TransformToAncestor(this).Transform(new Point(0, 0)));
  var dipScale = VisualTreeHelper.GetDpi(this).DpiScaleY;

  var contentHeight  = ActualHeight - WindowFrame.Margin.Bottom;      // 去掉留白后铺满客户区
  var contentBottom  = origin.Y + contentHeight * dipScale;
  var taskbarHeight  = (SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height) * dipScale;
  var workBottom     = SystemParameters.PrimaryScreenHeight * dipScale - taskbarHeight;

  WindowFrame.Margin = new Thickness(0, 0, 0, (contentBottom - workBottom) / dipScale);
  ```

  两个必须注意的点：
  1. **不要混用坐标系**。同一窗口同时存在 DWM 真实像素（1920x1200）、Win32 虚拟化像素
     （1549x973）与 WPF 布局单位（1548.8x972.8）三套数值，任意两者相减都会得到错误偏移；
     上面只使用 `PointToScreen` + `SystemParameters`，两者天然同口径。
  2. **留白只能算一次**。`ActualHeight` 会随留白变化，重复计算会来回震荡
     （实测在两轮之间 48 ↔ 116.8 反复跳）；因此先清空留白、等布局稳定后算一次并锁定。
  留白区域填的是窗口自身背景色，视觉上无差别；任务栏自动隐藏或停靠在侧边时留白自然为 0。
- **解析器**：单遍字符状态机，支持 `""` 转义引号、字段内换行、多字符分隔符；
  载入时按首行候选分隔符的「出现次数 × 各行一致性」自动判断分隔符。
- **编码识别**：先看 BOM，再用严格 UTF-8 解码校验，失败则依次尝试 GB18030 / GBK /
  Big5 / Windows-1252（`CodePagesEncodingProvider` 已在启动时注册）。
- **虚拟化**：`DataGrid` 开启行/列虚拟化与容器回收，行模型为紧凑的 `string[]`，
  因此 10 万行仍可流畅滚动。
- **列分隔线**：由列头单元格与数据单元格**自身的右边框**绘制，而不是另做一层覆盖层。
  覆盖层（`Adorner` / `Canvas`）的坐标原点与 `DataGrid` 并不重合，需要来回做坐标变换，
  稍有不慎整条线就会偏移（表现为“线落在字段中间”），而且首帧常在布局完成前绘制出错误位置。
  用边框绘制时线的位置由布局直接决定，**不可能与列边界错开**，也不需要任何重画时机控制。
  行号列右描边用更深的颜色，避免与第一列的线并成两像素。
- **撤销模型**：所有修改（含整列删除、批量替换）都实现为可逆命令并携带完整备份，
  撤销栈通过「保存点」判断文档是否有未保存修改。
- **菜单**：`MenuItem` 的 `ControlTemplate` 内必须提供名为 `PART_Popup` 的 `Popup`，
  否则子菜单无法弹出（点击菜单标题没有任何反应）。

---

## 已知边界

- 仅提供浅色主题（按需求移除深色模式）。
- 目前不做列排序 / 筛选（当前版本聚焦「打开 → 编辑 → 保存」主链路）。
- 打开超大文件（>100 万行）会占用较多内存（每格一个字符串对象）。
- 单元格不支持富文本 / 多行编辑框，字段内的换行符会原样保存，但在单元格内显示为单行。

---

## 自检

```powershell
# 核心层（67 项，全部应通过）
dotnet run --project tools/RoundTripProbe/RoundTripProbe.csproj

# 界面截图与交互（需要桌面会话）
powershell -ExecutionPolicy Bypass -File tools/Capture.ps1
powershell -ExecutionPolicy Bypass -File tools/Interact.ps1
powershell -ExecutionPolicy Bypass -File tools/LargeFile.ps1
```

运行期若出现未处理异常，会写入 `%TEMP%\csveditor-crash.log`（含 XAML 行列定位信息）。
