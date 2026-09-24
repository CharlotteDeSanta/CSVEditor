# 无边框窗口 + 自绘标题栏验证：拖拽移动、双击最大化、三个按钮、圆角/描边
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
public static class Tb {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static void Move(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); }
    public static void Down() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(80); }
    public static void Up() { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(200); }
    public static void Click(int x, int y) { Move(x, y); Down(); Up(); }

    public static void Drag(int fromX, int fromY, int toX, int toY) {
        Move(fromX, fromY);
        Down();
        int steps = 12;
        for (int i = 1; i <= steps; i++) {
            SetCursorPos(fromX + (toX - fromX) * i / steps, fromY + (toY - fromY) * i / steps);
            System.Threading.Thread.Sleep(40);
        }
        Up();
        System.Threading.Thread.Sleep(300);
    }

    public static void DoubleClick(int x, int y) {
        Move(x, y);
        Down(); Up();
        System.Threading.Thread.Sleep(60);
        Down(); Up();
        System.Threading.Thread.Sleep(600);
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
function Get-Rect($hwnd) {
    $r = New-Object Tb+RECT
    [void][Tb]::GetWindowRect($hwnd, [ref]$r)
    return $r
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
Start-Sleep -Seconds 2
[void][Tb]::SetForegroundWindow($hwnd)
$root = $A::FromHandle($hwnd)

# ---------- 标题栏按钮是否存在（UIA） ----------
$btnNames = @()
$btns = $root.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
foreach ($b in $btns) { if ($b.Current.Name) { $btnNames += $b.Current.Name } }
Write-Output ("按钮: " + ($btnNames -join ', '))

# ---------- 1) 拖拽标题栏移动窗口 ----------
$r0 = Get-Rect $hwnd
$titleBarX = [int](($r0.Left + $r0.Right) / 2) - 200
$titleBarY = $r0.Top + 20
[Tb]::Drag($titleBarX, $titleBarY, $titleBarX - 120, $titleBarY + 70)
$r1 = Get-Rect $hwnd
$movedX = $r1.Left - $r0.Left
$movedY = $r1.Top - $r0.Top
Write-Output ("拖拽前: ($($r0.Left),$($r0.Top))  拖拽后: ($($r1.Left),$($r1.Top))  位移: ($movedX,$movedY)")
$dragOk = ($movedX -lt -80 -and $movedY -gt 40)

# ---------- 2) 双击标题栏最大化 / 还原 ----------
$screenH = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height
$screenW = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Width
Write-Output ("屏幕工作区: ${screenW}x${screenH}")

[Tb]::DoubleClick(($r1.Left + $r1.Right) / 2 - 200, $r1.Top + 20)
Start-Sleep -Seconds 1
$r2 = Get-Rect $hwnd
# 最大化：高度接近工作区高度
$maximized = (($r2.Bottom - $r2.Top) -ge ($screenH - 30))
Write-Output ("双击后尺寸: $($r2.Right - $r2.Left)x$($r2.Bottom - $r2.Top)（原来 $($r1.Right - $r1.Left)x$($r1.Bottom - $r1.Top)）-> 最大化=$maximized")
[Tb]::Shot(0, 0, 900, 300, (Join-Path $OutDir '41-maximized.png'))
[Tb]::DoubleClick(($r2.Left + $r2.Right) / 2 - 200, $r2.Top + 20)
Start-Sleep -Seconds 1
$r3 = Get-Rect $hwnd
$restored = (($r3.Bottom - $r3.Top) -lt ($screenH - 60))
Write-Output ("再次双击后尺寸: $($r3.Right - $r3.Left)x$($r3.Bottom - $r3.Top) -> 还原=$restored")

# ---------- 3) 三个窗口按钮（用 UIA 名字点击） ----------
function Invoke-Caption($name) {
    $btn = Find-ByName $root $name
    if (-not $btn) { return $false }
    $br = $btn.Current.BoundingRectangle
    [Tb]::Click([int]($br.Left + $br.Width / 2), [int]($br.Top + $br.Height / 2))
    Start-Sleep -Milliseconds 900
    return $true
}

$clickedMax = Invoke-Caption '最大化'
$r4 = Get-Rect $hwnd
$maxBtnOk = $clickedMax -and (($r4.Bottom - $r4.Top) -ge ($screenH - 30))
Write-Output ("点最大化按钮: $($r4.Right - $r4.Left)x$($r4.Bottom - $r4.Top) -> 生效=$maxBtnOk")

$clickedRestore = Invoke-Caption '向下还原'
$r5 = Get-Rect $hwnd
$restoreBtnOk = $clickedRestore -and (($r5.Bottom - $r5.Top) -lt ($screenH - 60))
Write-Output ("点还原按钮: $($r5.Right - $r5.Left)x$($r5.Bottom - $r5.Top) -> 生效=$restoreBtnOk")

# ---------- 4) 载入示例数据后截图（看整体风格） ----------
$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Tb]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
Start-Sleep -Seconds 2
[Tb]::Capture($hwnd, (Join-Path $OutDir '42-custom-titlebar.png'))
Write-Output 'saved 42-custom-titlebar.png'
$rr = Get-Rect $hwnd
[Tb]::Shot($rr.Left, $rr.Top, 560, 60, (Join-Path $OutDir '43-titlebar-zoom.png'))

# ---------- 5) 最小化按钮最后测（会隐藏窗口） ----------
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);' -Name W -Namespace Tb -PassThru | Out-Null
$clickedMin = Invoke-Caption '最小化'
$iconic = [Tb.W]::IsIconic($hwnd)
Write-Output ("点最小化按钮 -> 已最小化=$iconic")

Write-Output ''
Write-Output ("[1] 标题栏拖拽移动窗口 : " + $(if ($dragOk) { 'PASS' } else { 'FAIL' }))
Write-Output ("[2] 双击标题栏最大化   : " + $(if ($maximized) { 'PASS' } else { 'FAIL' }))
Write-Output ("[3] 再次双击还原       : " + $(if ($restored) { 'PASS' } else { 'FAIL' }))
Write-Output ("[4] 最大化按钮生效     : " + $(if ($maxBtnOk) { 'PASS' } else { 'FAIL' }))
Write-Output ("[5] 还原按钮生效       : " + $(if ($restoreBtnOk) { 'PASS' } else { 'FAIL' }))
Write-Output ("[6] 最小化按钮生效     : " + $(if ($iconic) { 'PASS' } else { 'FAIL' }))

Stop-Process -Id $proc.Id -Force

if ($dragOk -and $maximized -and $restored -and $maxBtnOk -and $restoreBtnOk -and $iconic) {
    Write-Output 'TITLEBAR CHECK PASS'
} else {
    Write-Output 'TITLEBAR CHECK FAIL'
    exit 1
}
