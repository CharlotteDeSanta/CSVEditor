# 交互自检：加载示例数据 -> 查找面板 -> 浅色主题 -> 列头右键菜单 -> 单元格编辑
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
public static class Ui {
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
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(150);
    }

    public static void RightClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0008, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0010, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(200);
    }
}
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$A = [System.Windows.Automation.AutomationElement]
$S = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$PC = [System.Windows.Automation.PropertyCondition]

function Find-ByName($root, $name) {
    return $root.FindFirst($S::Descendants, (New-Object $PC($A::NameProperty, $name)))
}

function Get-AllText($root) {
    $all = $root.FindAll($S::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Text)))
    $names = @()
    foreach ($n in $all) { if ($n.Current.Name) { $names += $n.Current.Name } }
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
if ($hwnd -eq [IntPtr]::Zero) { throw '未找到主窗口' }

[void][Ui]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)
Start-Sleep -Seconds 2
[void][Ui]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 400

$root = $A::FromHandle($hwnd)

# 1. 载入示例数据
$rect = (Find-ByName $root '试用示例数据').Current.BoundingRectangle
[Ui]::Click([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2))
Start-Sleep -Seconds 2
Write-Output ("after-sample rows: " + ((Get-AllText $root | Where-Object { $_ -like '*行' }) -join ','))

# 2. 打开查找面板并输入关键字
$rect = (Find-ByName $root '查找 / 替换').Current.BoundingRectangle
[Ui]::Click([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2))
Start-Sleep -Seconds 1

$edits = $root.FindAll($S::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit)))
Write-Output ("edit boxes: " + $edits.Count)
if ($edits.Count -gt 0) {
    $findBox = $edits[0]
    $findBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('深圳')
    Start-Sleep -Milliseconds 600
    $findBox.SetFocus()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 1
}

$texts = Get-AllText $root
Write-Output ("find info: " + (($texts | Where-Object { $_ -like '*匹配*' }) -join ','))
[Ui]::Capture($hwnd, (Join-Path $OutDir '03-find.png'))
Write-Output 'saved 03-find.png'

# 3. 列头右键菜单
$header = Find-ByName $root '产品名称'
if ($header) {
    $r = $header.Current.BoundingRectangle
    [Ui]::RightClick([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2))
    Start-Sleep -Seconds 1
    $desktop = $A::RootElement
    $menuTexts = Get-AllText $desktop
    Write-Output ("menu texts: " + (($menuTexts | Where-Object { $_ -like '*插入*' -or $_ -like '*重命名*' -or $_ -like '*删除*' -or $_ -like '*列宽*' }) -join ','))
    [Ui]::Capture($hwnd, (Join-Path $OutDir '05-menu.png'))
    Write-Output 'saved 05-menu.png'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
}

Start-Sleep -Milliseconds 500
Stop-Process -Id $proc.Id -Force
Write-Output 'done'
