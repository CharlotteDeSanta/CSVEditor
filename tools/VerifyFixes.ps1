# 修复验证：网格线对齐 / 菜单弹出 / 分隔符下拉框 / 预览搜索 / 查找 / 右键菜单
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
public static class Fx {
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
        System.Threading.Thread.Sleep(350);
    }
    public static void CaptureScreenArea(int x, int y, int w, int h, string path) {
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
$CT = [System.Windows.Automation.ControlType]
function Find-ByName($root, $name) {
    return $root.FindFirst($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
}
function Get-AllText($root) {
    $all = $root.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Text)))
    $names = @(); foreach ($n in $all) { if ($n.Current.Name) { $names += $n.Current.Name } }
    return $names
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
[void][Fx]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2
[void][Fx]::SetForegroundWindow($hwnd)
$root = $A::FromHandle($hwnd)

# ---------------------------------------------------------------- 4) 菜单能否弹出
Write-Output '=== 4) 菜单弹出测试 ==='
foreach ($menu in @('文件', '编辑', '行与列', '视图', '帮助')) {
    $el = Find-ByName $root $menu
    if (-not $el) { Write-Output "  $menu : 未找到"; continue }
    $r = $el.Current.BoundingRectangle
    [Fx]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2), $false)
    Start-Sleep -Milliseconds 700
    $items = $A::RootElement.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::MenuItem)))
    $names = @(); foreach ($m in $items) { if ($m.Current.Name -and $m.Current.Name -ne $menu) { $names += $m.Current.Name } }
    Write-Output ("  {0} -> {1} 项: {2}" -f $menu, $names.Count, (($names | Select-Object -First 6) -join ', '))
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 400
}

# ---------------------------------------------------------------- 载入示例数据
$r = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Fx]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2), $false)
Start-Sleep -Seconds 2
Write-Output '=== 已载入示例数据 ==='

# ---------------------------------------------------------------- 1) 分隔符下拉框
Write-Output '=== 1) 分隔符下拉框测试 ==='
$combo = $root.FindFirst($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::ComboBox)))
Write-Output ("  初始显示: " + $combo.Current.Name)
$cr = $combo.Current.BoundingRectangle
[Fx]::Click([int]($cr.Left + $cr.Width / 2), [int]($cr.Top + $cr.Height / 2), $false)
Start-Sleep -Milliseconds 900
$lis = $A::RootElement.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::ListItem)))
$target = $null
foreach ($li in $lis) { if ($li.Current.Name -like '*分号*') { $target = $li } }
if ($target) {
    $lr = $target.Current.BoundingRectangle
    [Fx]::Click([int]($lr.Left + $lr.Width / 2), [int]($lr.Top + $lr.Height / 2), $false)
    Start-Sleep -Seconds 1
    Write-Output ("  选择分号后显示: " + $combo.Current.Name)
    $texts = Get-AllText $root
    Write-Output ("  状态栏: " + (($texts | Where-Object { $_ -like '分隔符*' }) -join ','))
} else {
    Write-Output '  未找到“分号”选项'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}

# ---------------------------------------------------------------- 5) 网格线对齐（截图）
Start-Sleep -Milliseconds 500
[Fx]::Capture($hwnd, (Join-Path $OutDir '20-light-grid.png'))
Write-Output 'saved 20-light-grid.png'

# ---------------------------------------------------------------- 3) 查找面板
$r = (Find-ByName $root '查找 / 替换').Current.BoundingRectangle
[Fx]::Click([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2), $false)
Start-Sleep -Seconds 1
$edits = $root.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Edit)))
if ($edits.Count -gt 0) {
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('深圳')
    Start-Sleep -Milliseconds 800
    $texts = Get-AllText $root
    Write-Output ("=== 3) 查找: " + (($texts | Where-Object { $_ -like '*匹配*' }) -join ','))
}
[Fx]::Capture($hwnd, (Join-Path $OutDir '21-light-find.png'))
Write-Output 'saved 21-light-find.png'

# ---------------------------------------------------------------- 2) 列头右键菜单（截全屏，含弹出层）
$header = Find-ByName $root '产品名称'
if ($header) {
    $hr = $header.Current.BoundingRectangle
    [Fx]::Click([int]($hr.Left + $hr.Width / 2), [int]($hr.Top + $hr.Height / 2), $true)
    Start-Sleep -Milliseconds 900
    $items = $A::RootElement.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::MenuItem)))
    $names = @(); foreach ($m in $items) { if ($m.Current.Name -like '*列*' -or $m.Current.Name -like '*重命名*' -or $m.Current.Name -like '*列宽*') { $names += $m.Current.Name } }
    Write-Output ("=== 2) 列头菜单: " + ($names -join ','))
    [Fx]::CaptureScreenArea(0, 0, 900, 620, (Join-Path $OutDir '22-header-menu.png'))
    Write-Output 'saved 22-header-menu.png'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 400
}

Stop-Process -Id $proc.Id -Force
Write-Output 'done'
