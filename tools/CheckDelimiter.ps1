# 聚焦验证：分隔符下拉框显示 + 菜单弹出截图
param(
    [string]$OutDir = "$PSScriptRoot\..\screenshots"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Dd {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(350);
    }
    public static void Shot(int x, int y, int w, int h, string path) {
        using (var bmp = new System.Drawing.Bitmap(w, h))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
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
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "进程提前退出，退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
[void][Dd]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2
[void][Dd]::SetForegroundWindow($hwnd)
$root = $A::FromHandle($hwnd)

# 载入示例数据
$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Dd]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
Start-Sleep -Seconds 2

# ---- 分隔符下拉框 ----
$combo = $root.FindFirst($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))
$cr = $combo.Current.BoundingRectangle
Write-Output ("combo rect: {0},{1} {2}x{3}" -f [int]$cr.Left, [int]$cr.Top, [int]$cr.Width, [int]$cr.Height)

# 展开下拉并按名字点选“分号 (;)”
[Dd]::Click([int]($cr.Left + $cr.Width / 2), [int]($cr.Top + $cr.Height / 2))
Start-Sleep -Seconds 1
$lis = $A::RootElement.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
$chosen = $null
foreach ($li in $lis) { if ($li.Current.Name -like '*分号*') { $chosen = $li; break } }
if ($chosen) {
    $lr = $chosen.Current.BoundingRectangle
    [Dd]::Click([int]($lr.Left + $lr.Width / 2), [int]($lr.Top + $lr.Height / 2))
    Write-Output ("选中项: " + $chosen.Current.Name)
} else {
    Write-Output ("未找到分号项；可见项: " + (($lis | ForEach-Object { $_.Current.Name }) -join ','))
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}
Start-Sleep -Seconds 1
[Dd]::Shot([int]($cr.Left) - 110, [int]($cr.Top) - 6, 420, 40, (Join-Path $OutDir '24-delimiter.png'))
Write-Output 'saved 24-delimiter.png'

# ---- 文件菜单截图 ----
$m = (Find-ByName $root '文件').Current.BoundingRectangle
[Dd]::Click([int]($m.Left + $m.Width / 2), [int]($m.Top + $m.Height / 2))
Start-Sleep -Milliseconds 900
[Dd]::Shot([int]$m.Left - 10, [int]$m.Top - 6, 460, 420, (Join-Path $OutDir '25-menu.png'))
Write-Output 'saved 25-menu.png'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 400

Stop-Process -Id $proc.Id -Force
Write-Output 'done'
