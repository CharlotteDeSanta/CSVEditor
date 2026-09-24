# 验证：最大化时状态栏底边应紧贴工作区底边（无多余空白）
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Gap {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
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
        mouse_event(2,0,0,0,IntPtr.Zero); mouse_event(4,0,0,0,IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(2,0,0,0,IntPtr.Zero); mouse_event(4,0,0,0,IntPtr.Zero);
        System.Threading.Thread.Sleep(900);
    }
    public static void Shot(IntPtr h, string path) {
        RECT r; GetWindowRect(h, out r);
        using (var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top))
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(h, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
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
[void][Gap]::SetForegroundWindow($hwnd)
$win0 = New-Object Gap+RECT; [void][Gap]::GetWindowRect($hwnd, [ref]$win0)
[Gap]::DoubleClick((($win0.Left + $win0.Right) / 2) - 200, $win0.Top + 18)
Start-Sleep -Seconds 1

$mon = [Gap]::Mon($hwnd)
$win = New-Object Gap+RECT; [void][Gap]::GetWindowRect($hwnd, [ref]$win)
$cli = New-Object Gap+RECT; [void][Gap]::GetClientRect($hwnd, [ref]$cli)

Write-Output ("窗口(最大化)      : ({0},{1})-({2},{3})" -f $win.Left, $win.Top, $win.Right, $win.Bottom)
Write-Output ("屏幕 rcMonitor    : {0}x{1}" -f ($mon.rcMonitor.Right - $mon.rcMonitor.Left), ($mon.rcMonitor.Bottom - $mon.rcMonitor.Top))
Write-Output ("工作区 rcWork     : {0}x{1} (工作区底边 y={2})" -f ($mon.rcWork.Right - $mon.rcWork.Left), ($mon.rcWork.Bottom - $mon.rcWork.Top), $mon.rcWork.Bottom)

$shot = Join-Path $PSScriptRoot '..\screenshots\48-max-gap.png'
[Gap]::Shot($hwnd, $shot)
$bmp = [System.Drawing.Bitmap]::FromFile($shot)
$dpi = $bmp.Width / ($win.Right - $win.Left)

# 找出窗口内容在截图里的底边（最后一处非背景像素）
$bg = @(0xF5, 0xF7, 0xFA)
$lastContent = -1
for ($y = $bmp.Height - 1; $y -ge 0; $y--) {
    for ($x = 10; $x -lt $bmp.Width - 10; $x += 8) {
        $c = $bmp.GetPixel($x, $y)
        if (-not ($c.R -eq $bg[0] -and $c.G -eq $bg[1] -and $c.B -eq $bg[2])) { $lastContent = $y; break }
    }
    if ($lastContent -ge 0) { break }
}

# 在"实际渲染区域"里找状态栏的上边界（Surface 白 #FFFFFF 条带）
$statusTop = -1
for ($y = $lastContent; $y -ge 0; $y--) {
    $c = $bmp.GetPixel(400, $y)
    if (-not ($c.R -eq 0xFF -and $c.G -eq 0xFF -and $c.B -eq 0xFF)) { $statusTop = $y + 1; break }
}

Write-Output ("截图 {0}x{1}，缩放 {2:N3}" -f $bmp.Width, $bmp.Height, $dpi)
Write-Output ("内容底边在截图 y={0}；状态栏白底上边界 y={1}；状态栏高≈{2}px" -f $lastContent, $statusTop, ($lastContent - $statusTop + 1))

# 内容底边换算到屏幕坐标
$contentBottomOnScreen = $win.Top + $lastContent
$gap = $mon.rcWork.Bottom - $contentBottomOnScreen
Write-Output ("内容底边屏幕坐标 y={0}；工作区底边 y={1}" -f $contentBottomOnScreen, $mon.rcWork.Bottom)
Write-Output ("=> 与工作区底边的空白 = {0} px" -f $gap)
Write-Output ("=> 与屏幕底边的空白   = {0} px（其中属于任务栏的 {1} px）" -f ($mon.rcMonitor.Bottom - $contentBottomOnScreen), ($mon.rcMonitor.Bottom - $mon.rcWork.Bottom))

$bmp.Dispose()
Stop-Process -Id $proc.Id -Force
