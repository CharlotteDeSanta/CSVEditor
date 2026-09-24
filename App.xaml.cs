using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CSVEditor;

/// <summary>
/// 应用程序入口。负责注册代码页编码提供程序并统一处理未捕获异常。
/// </summary>
public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "csveditor-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // GB18030 / GBK 等中文编码在 .NET Core 中需要显式注册
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain", args.ExceptionObject as Exception);

        base.OnStartup(e);

        // 手动创建主窗口（而不是 StartupUri），这样可以在界面构建前应用主题，
        // 同时支持“用本程序打开 CSV 文件”的命令行参数。
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var initialFile = e.Args.FirstOrDefault(a => !a.StartsWith('-'));
        if (!string.IsNullOrWhiteSpace(initialFile))
        {
            window.OpenInitialFile(initialFile);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher", e.Exception);

        MessageBox.Show(
            $"发生未处理的错误：\n\n{e.Exception.Message}",
            "CSV Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    /// <summary>把异常（含内部异常与 XAML 位置）写入临时日志，便于排查。</summary>
    private static void Log(string source, Exception? exception)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}");

            var current = exception;
            var depth = 0;

            while (current is not null && depth < 6)
            {
                builder.AppendLine($"{new string(' ', depth * 2)}{current.GetType().Name}: {current.Message}");

                if (current is System.Windows.Markup.XamlParseException xaml)
                {
                    builder.AppendLine($"{new string(' ', depth * 2)}  XAML 位置：第 {xaml.LineNumber} 行，第 {xaml.LinePosition} 列");
                }

                builder.AppendLine(current.StackTrace);
                current = current.InnerException;
                depth++;
            }

            builder.AppendLine(new string('-', 60));
            File.AppendAllText(LogPath, builder.ToString());
        }
        catch (Exception)
        {
            // 记录失败时忽略
        }
    }
}
