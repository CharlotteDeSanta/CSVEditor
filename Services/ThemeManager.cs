using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CSVEditor.Services;

/// <summary>
/// 窗口外观设置：让系统窗口外观与程序内的浅色风格保持一致。
/// 无边框窗口下负责设置 Windows 11 的圆角与投影，并确保标题栏使用浅色配色。
/// </summary>
public static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaWindowCornerPreference = 33;

    /// <summary>Windows 11（支持窗口圆角）起始版本号。</summary>
    private static readonly bool SupportsRoundedCorners =
        Environment.OSVersion.Version.Build >= 22000;

    /// <summary>窗口内容圆角半径（与 DWM 圆角保持一致）。</summary>
    public static CornerRadius WindowCornerRadius =>
        SupportsRoundedCorners ? new CornerRadius(8) : new CornerRadius(0);

    /// <summary>应用标题栏配色与窗口圆角。</summary>
    public static void ApplyWindowAppearance(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // 1) 标题栏固定浅色（系统处于深色模式时也不会变成黑条）
            var lightTitleBar = 0;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref lightTitleBar, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref lightTitleBar, sizeof(int));
            }

            // 2) 圆角：DWMWCP_ROUND(2)。旧系统会返回错误码，忽略即可。
            if (SupportsRoundedCorners)
            {
                var round = 2;
                DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
            }
        }
        catch (Exception)
        {
            // 不支持相关属性的系统上忽略：窗口仍可正常使用，只是没有圆角
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
