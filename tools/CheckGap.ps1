# 诊断：留白到底应该补多少？（区分"客户区超出工作区"与"看得见的超出"）
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Ov {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
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
[void][Ov]::SetForegroundWindow($hwnd)
$win = New-Object Ov+RECT; [void][Ov]::GetWindowRect($hwnd, [ref]$win)
[Ov]::DoubleClick((($win.Left + $win.Right) / 2) - 200, $win.Top + 18)
Start-Sleep -Seconds 1

$mon = [Ov]::Mon($hwnd)
$cli = New-Object Ov+RECT; [void][Ov]::GetClientRect($hwnd, [ref]$cli)
$dpi = [Ov]::GetDpiForWindow($hwnd); $scale = $dpi / 96.0

# 客户区底边在屏幕坐标中的位置
$pt = New-Object Ov+POINT; $pt.X = 0; $pt.Y = $cli.Bottom
[void][Ov]::ClientToScreen($hwnd, [ref]$pt)
$clientBottomOnScreen = $pt.Y

$winR = New-Object Ov+RECT; [void][Ov]::GetWindowRect($hwnd, [ref]$winR)

Write-Output "===== 屏幕与窗口（物理像素）====="
Write-Output ("屏幕底边 rcMonitor.Bottom : {0}" -f $mon.rcMonitor.Bottom)
Write-Output ("工作区底边 rcWork.Bottom  : {0}   (任务栏 {1}px)" -f $mon.rcWork.Bottom, ($mon.rcMonitor.Bottom - $mon.rcWork.Bottom))
Write-Output ("窗口矩形                  : ({0},{1}) - ({2},{3})" -f $winR.Left, $winR.Top, $winR.Right, $winR.Bottom)
Write-Output ("客户区底边(屏幕坐标)      : {0}" -f $clientBottomOnScreen)
Write-Output ("DPI {0} / 缩放 {1:N2}" -f $dpi, $scale)
Write-Output ""
$clientH = $cli.Bottom - $cli.Top
$workH = $mon.rcWork.Bottom - $mon.rcWork.Top
Write-Output "===== 两种算法的对比 ====="
Write-Output ("【旧算法】客户区高 - 工作区高")
Write-Output ("   物理 {0} - {1} = {2} px  ->  {3:N2} DIP" -f $clientH, $workH, ($clientH - $workH), (($clientH - $workH) / $scale))
Write-Output ("   其中藏在屏幕外的部分: {0} px（窗口下边 {1} 超出屏幕底 {2}）" -f ($winR.Bottom - $mon.rcMonitor.Bottom), $winR.Bottom, $mon.rcMonitor.Bottom)
Write-Output ("【新算法】客户区底边(屏幕) - 工作区底边")
$visible = $clientBottomOnScreen - $mon.rcWork.Bottom
Write-Output ("   {0} - {1} = {2} px  ->  {3:N2} DIP" -f $clientBottomOnScreen, $mon.rcWork.Bottom, $visible, ($visible / $scale))
Write-Output ""
Write-Output ("=> 旧算法多补了 {0:N2} DIP（就是你说的那条多余空白）" -f ((($clientH - $workH) - $visible) / $scale))

$shot = Join-Path $PSScriptRoot '..\screenshots\47-gap.png'
[Ov]::Shot($hwnd, $shot)
$bmp = [System.Drawing.Bitmap]::FromFile($shot)
# 从截图最底部往上找最后一行"有内容"的像素（跳过纯背景色）
$bg = @(0xF5, 0xF7, 0xFA)
$lastContent = -1
for ($y = $bmp.Height - 1; $y -ge 0; $y--) {
    $found = $false
    for ($x = 10; $x -lt $bmp.Width - 10; $x += 12) {
        $c = $bmp.GetPixel($x, $y)
        if (-not ($c.R -eq $bg[0] -and $c.G -eq $bg[1] -and $c.B -eq $bg[2])) { $found = $true; break }
    }
    if ($found) { $lastContent = $y; break }
}
$waBottomInShot = $mon.rcWork.Bottom - $winR.Top
Write-Output ("截图高 {0}；工作区底边对应截图 y={1}" -f $bmp.Height, $waBottomInShot)
Write-Output ("截图里最后一行内容 y={0}" -f $lastContent)
Write-Output ("=> 内容底边距工作区底边的空白 = {0} px 物理 = {1:N2} DIP" -f ($waBottomInShot - $lastContent), (($waBottomInShot - $lastContent) / $scale))
$bmp.Dispose()
Write-Output "saved 47-gap.png"

Stop-Process -Id $proc.Id -Force
