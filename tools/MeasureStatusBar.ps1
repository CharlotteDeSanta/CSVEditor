# 精确测量：最大化时"应用自报的窗口几何"与"真实像素"的对应关系
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class M {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MI i);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    // DWMWA_EXTENDED_FRAME_BOUNDS = 9：真实可见边界（不含 DWM 阴影）
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MI { public int cbSize; public RECT mon; public RECT work; public uint flags; }
    public static MI Mon(IntPtr h) { var i = new MI(); i.cbSize = Marshal.SizeOf(typeof(MI)); GetMonitorInfo(MonitorFromWindow(h,2), ref i); return i; }
    public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }
    public static void Dbl(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
        mouse_event(2,0,0,0,IntPtr.Zero); mouse_event(4,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80);
        mouse_event(2,0,0,0,IntPtr.Zero); mouse_event(4,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(1200);
    }
    public static void Save(IntPtr h, string p) {
        RECT r; GetWindowRect(h, out r);
        using (var b = new System.Drawing.Bitmap(r.Right-r.Left, r.Bottom-r.Top))
        using (var g = System.Drawing.Graphics.FromImage(b)) {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(h, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            b.Save(p, System.Drawing.Imaging.ImageFormat.Png);
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
    if ($proc.HasExited) { throw "exit $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 2
$w0 = New-Object M+RECT; [void][M]::GetWindowRect($hwnd, [ref]$w0)
[M]::Dbl([int](($w0.Left + $w0.Right)/2) - 200, $w0.Top + 18)
Start-Sleep -Seconds 2

$mon = [M]::Mon($hwnd)
$win = New-Object M+RECT; [void][M]::GetWindowRect($hwnd, [ref]$win)
$cli = New-Object M+RECT; [void][M]::GetClientRect($hwnd, [ref]$cli)
$frm = [M]::Frame($hwnd)
$dpi = [M]::GetDpiForWindow($hwnd)

Write-Output "===== 探针（本会话）看到的 ====="
Write-Output ("DPI={0}  系统度量 SM_CYSCREEN={1}" -f $dpi, [M]::GetSystemMetrics(1))
Write-Output ("rcMonitor : {0}x{1}  ({2},{3})-({4},{5})" -f ($mon.mon.Right-$mon.mon.Left), ($mon.mon.Bottom-$mon.mon.Top), $mon.mon.Left, $mon.mon.Top, $mon.mon.Right, $mon.mon.Bottom)
Write-Output ("rcWork    : {0}x{1}  底边 y={2}" -f ($mon.work.Right-$mon.work.Left), ($mon.work.Bottom-$mon.work.Top), $mon.work.Bottom)
Write-Output ("GetWindowRect     : ({0},{1})-({2},{3})  {4}x{5}" -f $win.Left, $win.Top, $win.Right, $win.Bottom, ($win.Right-$win.Left), ($win.Bottom-$win.Top))
Write-Output ("GetClientRect     : h={0}" -f ($cli.Bottom-$cli.Top))
Write-Output ("DWM 扩展边框(DWM) : ({0},{1})-({2},{3})  {4}x{5}" -f $frm.Left, $frm.Top, $frm.Right, $frm.Bottom, ($frm.Right-$frm.Left), ($frm.Bottom-$frm.Top))

$shot = Join-Path $PSScriptRoot '..\screenshots\54-measure.png'
[M]::Save($hwnd, $shot)
$bmp = [System.Drawing.Bitmap]::FromFile($shot)
Write-Output ("截图: {0}x{1}" -f $bmp.Width, $bmp.Height)

# 从底部往上找"状态栏"：状态栏底色是白 #FFFFFF
function Row-WhiteRatio($bmp, $y) {
    $n = 0; $t = 0
    for ($x = 10; $x -lt $bmp.Width - 10; $x += 4) {
        $c = $bmp.GetPixel($x, $y); $t++
        if ($c.R -eq 0xFF -and $c.G -eq 0xFF -and $c.B -eq 0xFF) { $n++ }
    }
    return $n / $t
}
$whiteRows = @()
for ($y = 0; $y -lt $bmp.Height; $y++) {
    if ((Row-WhiteRatio $bmp $y) -gt 0.5) { $whiteRows += $y }
}
if ($whiteRows.Count -gt 0) {
    $sbTop = $whiteRows[0]; $sbBottom = $whiteRows[-1]
    Write-Output ("状态栏(白底>50%) 截图 y={0}..{1}  高={2}px" -f $sbTop, $sbBottom, ($sbBottom-$sbTop+1))
    $sbBottomOnScreen = $win.Top + $sbBottom
    Write-Output ("状态栏底边 -> 屏幕 y={0}；工作区底边 y={1} -> 空白 {2}px" -f $sbBottomOnScreen, $mon.work.Bottom, ($mon.work.Bottom - $sbBottomOnScreen))
} else {
    Write-Output "未找到状态栏白底行"
}
$bmp.Dispose()

Stop-Process -Id $proc.Id -Force
