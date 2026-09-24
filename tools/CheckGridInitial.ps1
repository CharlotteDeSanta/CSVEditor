# 网格线对齐验证（纯测量，不依赖应用内诊断）：
#   1) 载入示例数据，截图 A（此时网格线已经画好）
#   2) 打开「查找 / 替换」面板触发一次布局重画，截图 B
#   3) 断言 A 与 B 中每一列的列边界像素完全一致 ——
#      若首帧画错（例如漏掉行号列偏移），A 与 B 必然不同。
#   这样正好复现用户报告的场景：“初始不对，点查找替换后才对”。
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
public static class Cm2 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
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

# 在一行里找出“列边界像素列”：该像素明显暗于左右各 3px
function Get-BoundaryColumns($bmp, $y, $fromX, $toX) {
    $result = @()
    for ($x = $fromX; $x -lt $toX; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $l = $bmp.GetPixel($x - 4, $y)
        $r = $bmp.GetPixel($x + 4, $y)
        $cm = ($c.R + $c.G + $c.B) / 3.0
        $lm = ($l.R + $l.G + $l.B) / 3.0
        $rm = ($r.R + $r.G + $r.B) / 3.0
        if ($cm -lt 245 -and $cm -lt $lm -and $cm -lt $rm) { $result += $x }
    }
    return $result
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
[void][Cm2]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2
[void][Cm2]::SetForegroundWindow($hwnd)
$root = $A::FromHandle($hwnd)

# ---------- 步骤 1：载入示例数据后立刻截图 ----------
$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Cm2]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
Start-Sleep -Seconds 3
$shotA = Join-Path $OutDir '36-initial.png'
[Cm2]::Capture($hwnd, $shotA)
Write-Output "saved 36-initial.png"

# 读取最后一列的右边界（UIA），据此确定扫描范围
$headerCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::HeaderItem)
$headers = @()
foreach ($h in $root.FindAll($S::Descendants, $headerCond)) {
    if ($h.Current.Name -and $h.Current.Name -notmatch '^\d+$' -and $h.Current.BoundingRectangle.Width -gt 20) { $headers += $h }
}
$winRect = New-Object Cm2+RECT
[void][Cm2]::GetWindowRect($hwnd, [ref]$winRect)
$colRights = @($headers | ForEach-Object { [int][Math]::Round($_.Current.BoundingRectangle.Right - $winRect.Left) })
Write-Output ("UIA 列头右边界: " + ($colRights -join ', '))

# ---------- 步骤 2：打开查找面板（触发布局重画）后截图 ----------
$r = (Find-ByName $root '查找 / 替换').Current.BoundingRectangle
[Cm2]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
Start-Sleep -Seconds 2
$shotB = Join-Path $OutDir '37-after-find.png'
[Cm2]::Capture($hwnd, $shotB)
Write-Output "saved 37-after-find.png"

# ---------- 比对 ----------
$bmpA = [System.Drawing.Bitmap]::FromFile($shotA)
$bmpB = [System.Drawing.Bitmap]::FromFile($shotB)
"截图 A: $($bmpA.Width)x$($bmpA.Height)  B: $($bmpB.Width)x$($bmpB.Height)"

# 表头带定位（用 A）：在多个 x 上扫描表头底色 #F4F6FA，取出现次数最多的连续 y 区间
$rowCounts = @{}
foreach ($x in 200, 300, 400, 500, 600, 700, 800, 900, 1000) {
    for ($y = 40; $y -lt 400; $y++) {
        $c = $bmpA.GetPixel($x, $y)
        if ($c.R -eq 0xF4 -and $c.G -eq 0xF6 -and $c.B -eq 0xFA) {
            if (-not $rowCounts.ContainsKey($y)) { $rowCounts[$y] = 0 }
            $rowCounts[$y]++
        }
    }
}
$headerRows = @($rowCounts.Keys | Where-Object { $rowCounts[$_] -ge 4 } | Sort-Object)
if ($headerRows.Count -eq 0) { $bmpA.Dispose(); $bmpB.Dispose(); $proc | Stop-Process -Force; throw '未找到表头带' }
$top = $headerRows[0]
$bottom = $headerRows[-1] + 1
$headerMid = [int](($top + $bottom) / 2)
$rowMid = $bottom + 15
"表头带 y=$top..$bottom；采样行 y=$headerMid（表头） / y=$rowMid（数据）"

$maxX = [Math]::Min($bmpA.Width - 4, ($colRights | Measure-Object -Maximum).Maximum + 60)
$hdrA = Get-BoundaryColumns $bmpA $headerMid 90 $maxX
$rowA = Get-BoundaryColumns $bmpA $rowMid 90 $maxX
$rowB = Get-BoundaryColumns $bmpB $rowMid 90 $maxX

Write-Output ("表头竖线(A): " + ($hdrA -join ', '))
Write-Output ("数据行竖线(A, 首次绘制): " + ($rowA -join ', '))
Write-Output ("数据行竖线(B, 重画后)  : " + ($rowB -join ', '))

# 每一条 UIA 列头右边界，都应当能在 A 的数据行里找到（±2px）
$missA = @(); $missB = @()
foreach ($x in $colRights) {
    $hitA = $false; $hitB = $false
    foreach ($d in -2, -1, 0, 1, 2) {
        if ($rowA -contains ($x + $d)) { $hitA = $true }
        if ($rowB -contains ($x + $d)) { $hitB = $true }
    }
    if (-not $hitA) { $missA += $x }
    if (-not $hitB) { $missB += $x }
}

$identical = (($rowA -join ',') -eq ($rowB -join ','))

Write-Output ''
Write-Output ("[1] 首次绘制即与列边界吻合 : " + $(if ($missA.Count -eq 0) { 'PASS' } else { "FAIL 未命中 $($missA -join ',')" }))
Write-Output ("[2] 重画后仍然吻合         : " + $(if ($missB.Count -eq 0) { 'PASS' } else { "FAIL 未命中 $($missB -join ',')" }))
Write-Output ("[3] 首帧与重画帧完全一致   : " + $(if ($identical) { 'PASS' } else { 'FAIL（说明首帧画错，需要重画才修正）' }))

$bmpA.Dispose(); $bmpB.Dispose()
$proc | Stop-Process -Force

if ($missA.Count -eq 0 -and $missB.Count -eq 0 -and $identical) {
    Write-Output 'GRID CHECK PASS'
} else {
    Write-Output 'GRID CHECK FAIL'
    exit 1
}
