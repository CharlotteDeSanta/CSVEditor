# 通过命令行参数打开大文件，测量“进程启动到表格可交互”的端到端耗时
param(
    [string]$CsvPath = "$env:TEMP\csveditor-probe\big.csv",
    [string]$OutDir = "$PSScriptRoot\..\screenshots"
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Lf {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
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
}
'@

$A = [System.Windows.Automation.AutomationElement]
$S = [System.Windows.Automation.TreeScope]
function Get-AllText($root) {
    $all = $root.FindAll($S::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $names = @(); foreach ($n in $all) { if ($n.Current.Name) { $names += $n.Current.Name } }
    return $names
}

$exe = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\CSVEditor.exe'
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $exe -ArgumentList "`"$CsvPath`"" -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 200
    $proc.Refresh()
    if ($proc.HasExited) { throw "进程提前退出，退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
[void][Lf]::MoveWindow($hwnd, 0, 0, 1260, 780, $true)

$root = $A::FromHandle($hwnd)
$rows = ''
while ($timer.Elapsed.TotalSeconds -lt 90) {
    Start-Sleep -Milliseconds 200
    $texts = Get-AllText $root
    $match = $texts | Where-Object { $_ -match '^[\d,]+ 行$' } | Select-Object -First 1
    if ($match -and $match -ne '0 行') { $rows = $match; break }
}
$timer.Stop()

"rows loaded : $rows"
"elapsed     : $([int]$timer.Elapsed.TotalMilliseconds) ms (进程启动 + 解析 + 渲染)"
Start-Sleep -Seconds 1
[Lf]::Capture($hwnd, (Join-Path $OutDir '12-large-file.png'))
"working set : $([math]::Round($proc.WorkingSet64 / 1MB, 0)) MB"
Write-Output 'saved 12-large-file.png'

Stop-Process -Id $proc.Id -Force
Write-Output 'done'
