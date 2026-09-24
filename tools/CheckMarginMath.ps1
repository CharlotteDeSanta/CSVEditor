# 打印最大化时"底栏留白"的完整推导过程（逐项实测值）
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Derive {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
    }
    public static MONITORINFO Mon(IntPtr h) {
        var i = new MONITORINFO(); i.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfo(MonitorFromWindow(h, 2), ref i); return i;
    }
    public static void DoubleClick(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); mouse_event(0x0004,0,0,0,IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002,0,0,0,IntPtr.Zero); mouse_event(0x0004,0,0,0,IntPtr.Zero);
        System.Threading.Thread.Sleep(900);
    }
}
'@

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
[void][Derive]::SetForegroundWindow($hwnd)

$win = New-Object Derive+RECT; [void][Derive]::GetWindowRect($hwnd, [ref]$win)
[Derive]::DoubleClick((($win.Left + $win.Right) / 2) - 200, $win.Top + 18)
Start-Sleep -Seconds 1

$mon = [Derive]::Mon($hwnd)
$cli = New-Object Derive+RECT; [void][Derive]::GetClientRect($hwnd, [ref]$cli)
$dpi = [Derive]::GetDpiForWindow($hwnd)
$scale = $dpi / 96.0

$monH = $mon.rcMonitor.Bottom - $mon.rcMonitor.Top
$workH = $mon.rcWork.Bottom - $mon.rcWork.Top
$clientH = $cli.Bottom - $cli.Top
$overflowPx = $clientH - $workH
$overflowDip = $overflowPx / $scale

Write-Output "===== 各项实测值（物理像素）====="
Write-Output ("屏幕(rcMonitor) 高      : {0}" -f $monH)
Write-Output ("工作区(rcWork)  高      : {0}   <- 已排除任务栏，任务栏高 {1}" -f $workH, ($monH - $workH))
Write-Output ("窗口客户区(GetClientRect)高: {0}" -f $clientH)
Write-Output ("窗口 DPI                : {0}  (缩放 {1:N2}x)" -f $dpi, $scale)
Write-Output ""
Write-Output "===== 留白推导 ====="
Write-Output ("① 客户区高(物理)   = {0} px" -f $clientH)
Write-Output ("② 换算成 DIP       = {0} / {1:N2} = {2:N2} DIP" -f $clientH, $scale, ($clientH / $scale))
Write-Output ("③ 工作区高(DIP)    = {0} / {1:N2} = {2:N2} DIP" -f $workH, $scale, ($workH / $scale))
Write-Output ("④ 溢出 = ② - ③     = {0:N2} DIP" -f (($clientH / $scale) - ($workH / $scale)))
Write-Output ("   等价于物理差 {0} px / {1:N2} = {2:N2} DIP" -f $overflowPx, $scale, $overflowDip)
Write-Output ""
Write-Output ("⑤ Clamp(·, 0, 120)  = {0:N2} DIP   <- 最终底部留白" -f ([Math]::Min(120, [Math]::Max(0, $overflowDip))))
Write-Output ("   等于任务栏高度({0}px)换算: {1:N2} DIP" -f ($monH - $workH), (($monH - $workH) / $scale))

Stop-Process -Id $proc.Id -Force
