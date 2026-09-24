# 抓取提示气泡（气泡只显示 2.2 秒，需在点击后立即截图）
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
public static class Ts {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Click(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(100);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
    public static void Capture(IntPtr hwnd, string path) {
        RECT r; GetWindowRect(hwnd, out r);
        using (var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$A = [System.Windows.Automation.AutomationElement]
$S = [System.Windows.Automation.TreeScope]
function Find-ByName($root, $name) {
    return $root.FindFirst($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
}

$exe = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\CSVEditor.exe'
$proc = Start-Process -FilePath $exe -PassThru
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 400
    $proc.Refresh()
    if ($proc.HasExited) { throw "退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 2
[void][Ts]::SetForegroundWindow($hwnd)
$root = $A::FromHandle($hwnd)

$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Ts]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
Start-Sleep -Milliseconds 900   # 气泡已出现但还没消失
$shot = Join-Path $OutDir '45-toast.png'
[Ts]::Capture($hwnd, $shot)
Write-Output "saved 45-toast.png"

$bmp = [System.Drawing.Bitmap]::FromFile($shot)
$cropW = 640
$crop = New-Object System.Drawing.Bitmap($cropW, 90)
$g = [System.Drawing.Graphics]::FromImage($crop)
$rect = New-Object System.Drawing.Rectangle(0, 0, $cropW, 90)
$x = [int]((($bmp.Width - $cropW) / 2))
$y = [int](($bmp.Height - 160))
$srcRect = New-Object System.Drawing.Rectangle $x, $y, $cropW, 90
$g.DrawImage($bmp, $rect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$crop.Save((Join-Path $OutDir '45-toast-crop.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$crop.Dispose(); $bmp.Dispose()
Write-Output "saved 45-toast-crop.png"

Stop-Process -Id $proc.Id -Force
