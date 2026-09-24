# 最大化几何检查：窗口矩形 vs 屏幕工作区（任务栏区域）
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Mg {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
    }
    public static MONITORINFO MonitorOf(IntPtr hwnd) {
        var info = new MONITORINFO();
        info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
        return info;
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(800);
    }
    public static void DoubleClick(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(800);
    }
}
'@

$exe = Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows\CSVEditor.exe'
$proc = Start-Process -FilePath $exe -PassThru
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { throw "退出码 $($proc.ExitCode)" }
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 2
[void][Mg]::SetForegroundWindow($hwnd)

$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$ba = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
"Screen.Bounds      : $($ba.Width)x$($ba.Height) at ($($ba.X),$($ba.Y))"
"Screen.WorkingArea : $($wa.Width)x$($wa.Height) at ($($wa.X),$($wa.Y))  (底部任务栏高 $($ba.Height - $wa.Height))"

$mi = [Mg]::MonitorOf($hwnd)
"MonitorInfo rcMonitor: $($mi.rcMonitor.Right - $mi.rcMonitor.Left)x$($mi.rcMonitor.Bottom - $mi.rcMonitor.Top) at ($($mi.rcMonitor.Left),$($mi.rcMonitor.Top))"
"MonitorInfo rcWork   : $($mi.rcWork.Right - $mi.rcWork.Left)x$($mi.rcWork.Bottom - $mi.rcWork.Top) at ($($mi.rcWork.Left),$($mi.rcWork.Top))"

$r0 = New-Object Mg+RECT
[void][Mg]::GetWindowRect($hwnd, [ref]$r0)
"窗口(还原): ($($r0.Left),$($r0.Top)) - ($($r0.Right),$($r0.Bottom))  $($r0.Right-$r0.Left)x$($r0.Bottom-$r0.Top)"

# 双击标题栏最大化
[Mg]::DoubleClick(($r0.Left + $r0.Right) / 2 - 200, $r0.Top + 18)
Start-Sleep -Seconds 1
$r1 = New-Object Mg+RECT
[void][Mg]::GetWindowRect($hwnd, [ref]$r1)
"窗口(最大化): ($($r1.Left),$($r1.Top)) - ($($r1.Right),$($r1.Bottom))  $($r1.Right-$r1.Left)x$($r1.Bottom-$r1.Top)"

$bottomOverflow = $r1.Bottom - $mi.rcWork.Bottom
"窗口底边 - 工作区底边 = $bottomOverflow px" + $(if ($bottomOverflow -gt 0) { "  (系统最大化矩形按屏幕算，多出的部分由内容底部留白补偿)" } else { "  (正常)" })

# 视觉验证：截图后检查状态栏是否完整落在工作区内
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Cap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Save(IntPtr hwnd, string path) {
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
$shot = Join-Path $PSScriptRoot '..\screenshots\41-maximized.png'
[Cap]::Save($hwnd, $shot)
"captured 41-maximized.png"

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($shot)
$dpi = $bmp.Width / ($r1.Right - $r1.Left)
# 状态栏是窗口最底部那一条（背景 Surface=白），检查窗口底边之内、工作区底边之上的内容
$waBottomInShot = [int](($mi.rcWork.Bottom - $r1.Top))
"截图高 $($bmp.Height)，工作区底边对应截图 y=$waBottomInShot"
# 统计工作区底边以下（会被任务栏遮住）的内容像素：跳过窗口自身描边（左右各 8px）。
# 该区域里若只剩"窗口背景色"，说明内容已全部落在工作区内。
$below = 0; $total = 0
for ($y = [Math]::Max(0, $waBottomInShot); $y -lt $bmp.Height; $y += 2) {
    for ($x = 10; $x -lt $bmp.Width - 10; $x += 20) {
        $c = $bmp.GetPixel($x, $y)
        $total++
        # 背景 #F5F7FA 视为空白
        if (-not ($c.R -eq 0xF5 -and $c.G -eq 0xF7 -and $c.B -eq 0xFA)) { $below++ }
    }
}
"被遮挡区域内的内容像素: $below / $total"

# 内容真正结束的位置（从底部往上找最后一行非背景像素）
$lastContent = -1
for ($y = $bmp.Height - 1; $y -ge 0; $y--) {
    $hit = $false
    for ($x = 10; $x -lt $bmp.Width - 10; $x += 8) {
        $c = $bmp.GetPixel($x, $y)
        if (-not ($c.R -eq 0xF5 -and $c.G -eq 0xF7 -and $c.B -eq 0xFA)) { $hit = $true; break }
    }
    if ($hit) { $lastContent = $y; break }
}
"内容最后一行 y=$lastContent；工作区底边对应 y=$waBottomInShot"
$contentGap = $waBottomInShot - $lastContent
"内容底边与工作区底边的距离 = $contentGap px"

$bmp.Dispose()
$occluded = ($below -gt 2)
"判定: 遮挡=" + $(if ($occluded) { 'FAIL' } else { 'PASS（未被任务栏遮住）' }) +
    "；贴合度=" + $(if ($contentGap -le 24) { "PASS（间隙 ${contentGap}px，含状态栏自身内边距）" } else { "注意：间隙 ${contentGap}px 偏大" })

Stop-Process -Id $proc.Id -Force

if ($occluded) { exit 1 }
