# 启动 CSVEditor，等待主窗口出现后截图；通过 UIA 点击“试用示例数据”再抓取编辑界面。
# 说明：截图前先把窗口挪到屏幕左上角，避免高 DPI 下被屏幕边界裁剪。
param(
    [string]$OutDir = "$PSScriptRoot\..\screenshots"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Shot {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static void Capture(IntPtr hwnd, string path) {
        RECT r; GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        using (var bmp = new System.Drawing.Bitmap(w, h))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$exe = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\CSVEditor.exe'
$proc = Start-Process -FilePath $exe -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "进程提前退出，退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw '未找到主窗口' }

# 移到左上角，确保整个窗口都在屏幕内
[void][Shot]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2

function Get-Texts($handle) {
    $el = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $all = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $names = @()
    foreach ($n in $all) { if ($n.Current.Name) { $names += $n.Current.Name } }
    return $names
}

$shot1 = Join-Path $OutDir '01-welcome.png'
[Shot]::Capture($hwnd, $shot1)
Write-Output "saved $shot1"

# 点击“试用示例数据”按钮
[void][Shot]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 400
$el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, '试用示例数据')
$btn = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)

if ($null -eq $btn) {
    Write-Output '未找到示例数据按钮'
} else {
    $rect = $btn.Current.BoundingRectangle
    [Shot]::Click([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2))
    Start-Sleep -Seconds 5

    # 强制重绘后再截图，避免拿到旧帧
    [void][Shot]::SetForegroundWindow($hwnd)
    Start-Sleep -Seconds 2

    $shot2 = Join-Path $OutDir '02-editor.png'
    [Shot]::Capture($hwnd, $shot2)
    Write-Output "saved $shot2"
    Write-Output ("editor texts: " + ((Get-Texts $hwnd | Select-Object -First 45) -join ' | '))
}

Start-Sleep -Milliseconds 500
Stop-Process -Id $proc.Id -Force
Write-Output 'done'
