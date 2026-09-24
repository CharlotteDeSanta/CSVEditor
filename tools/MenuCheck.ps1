# 验证列头右键菜单：加载示例数据 -> 右键“产品名称”列头 -> 截图
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
public static class Cm {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Capture(IntPtr hwnd, string path) {
        RECT r; GetWindowRect(hwnd, out r);
        using (var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    public static void Click(int x, int y, bool right) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(right ? 0x0008u : 0x0002u, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(right ? 0x0010u : 0x0004u, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(300);
    }
}
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Remove-Item "$env:LOCALAPPDATA\CSVEditor\settings.json" -ErrorAction SilentlyContinue
Remove-Item "$env:APPDATA\CSVEditor\settings.json" -ErrorAction SilentlyContinue

$A = [System.Windows.Automation.AutomationElement]
$S = [System.Windows.Automation.TreeScope]
function Find-ByName($root, $name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    return $root.FindFirst($S::Descendants, $cond)
}

$exe = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\CSVEditor.exe'
$proc = Start-Process -FilePath $exe -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "进程提前退出，退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}

[void][Cm]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2
[void][Cm]::SetForegroundWindow($hwnd)

$root = $A::FromHandle($hwnd)
$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Cm]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2), $false)
Start-Sleep -Seconds 2

# 截图前记录基准，右键后再比对差异
$before = Join-Path $OutDir 'tmp-before.png'
$after = Join-Path $OutDir '10-header-menu.png'
[Cm]::Capture($hwnd, $before)

$header = Find-ByName $root '产品名称'
if (-not $header) { throw '未找到列头元素' }
$hr = $header.Current.BoundingRectangle
[Cm]::Click([int]($hr.Left + $hr.Width / 2), [int]($hr.Top + $hr.Height / 2), $true)
Start-Sleep -Seconds 1
[Cm]::Capture($hwnd, $after)
Write-Output 'saved 10-header-menu.png'

# 菜单项文本
$menuTexts = @()
$all = $A::RootElement.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
foreach ($m in $all) { if ($m.Current.Name) { $menuTexts += $m.Current.Name } }
Write-Output ("menu items: " + ($menuTexts -join ','))

Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::FromFile($before)
$b = [System.Drawing.Bitmap]::FromFile($after)
$diff = 0
for ($x = 0; $x -lt $a.Width; $x += 7) {
    for ($y = 0; $y -lt $a.Height; $y += 7) {
        if ($a.GetPixel($x,$y).ToArgb() -ne $b.GetPixel($x,$y).ToArgb()) { $diff++ }
    }
}
$a.Dispose(); $b.Dispose()
Remove-Item $before -Force
Write-Output "pixel diff samples: $diff"

Stop-Process -Id $proc.Id -Force
Write-Output 'done'
