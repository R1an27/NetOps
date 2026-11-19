using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace NetOps.Agent;

// Đọc chỉ số phần cứng thật của máy (CPU, RAM, Disk) hỗ trợ cả Windows lẫn Linux.
public static class MetricsExportService
{
    // Lưu lại mốc đo CPU của lần trước để tính phần trăm bận trong khoảng thời gian giữa 2 lần đo.
    // Vì hệ điều hành chỉ cung cấp "tổng thời gian CPU đã chạy từ lúc bật máy", muốn biết % tức thời
    // ta phải lấy: (Mốc mới - Mốc cũ) để ra phần chênh lệch (delta).
    private static ulong _prevCpuTotal;
    private static ulong _prevCpuIdle;
    private static bool _hasPrevCpu;
    private static double _cachedCpuUsage;
    private static DateTime _lastCpuCheck = DateTime.MinValue;

    // [1] Đo CPU toàn hệ thống (Windows: GetSystemTimes | Linux: /proc/stat)
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static ulong ToUInt64(FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    public static double GetCpuUsagePercent()
    {
        var now = DateTime.UtcNow;
        // Nếu 2 lần gọi cách nhau chưa đủ 500ms thì trả lại kết quả cũ để tránh số nhảy loạn (nhiễu).
        if (_hasPrevCpu && (now - _lastCpuCheck).TotalMilliseconds < 500)
            return _cachedCpuUsage;

        // Thử đọc cặp (tổng thời gian CPU, thời gian rảnh) tương ứng với hệ điều hành đang chạy.
        if (!TryReadCpuTicks(out var total, out var idle))
            return _cachedCpuUsage;

        // Lần đo đầu tiên chưa có mốc cũ để trừ -> chỉ lưu mốc làm gốc và trả 0.0% (không bịa số giả).
        if (!_hasPrevCpu)
        {
            _prevCpuTotal = total;
            _prevCpuIdle = idle;
            _hasPrevCpu = true;
            _lastCpuCheck = now;
            return 0.0;
        }

        // Tính phần chênh lệch giữa lần đo này và lần đo trước.
        var totalDelta = total - _prevCpuTotal;
        var idleDelta = idle - _prevCpuIdle;
        _prevCpuTotal = total;
        _prevCpuIdle = idle;
        _lastCpuCheck = now;

        if (totalDelta > 0)
        {
            // Thời gian bận = Tổng thời gian - Thời gian rảnh.
            var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
            _cachedCpuUsage = Math.Round(Math.Clamp((double)busy * 100.0 / totalDelta, 0.0, 100.0), 1);
        }

        return _cachedCpuUsage;
    }

    // Gộp chung cách lấy (total, idle) của Windows và Linux vào 1 hàm duy nhất
    // để công thức tính % CPU ở trên dùng chung cho cả 2 hệ điều hành, không bị lặp code.
    private static bool TryReadCpuTicks(out ulong total, out ulong idle)
    {
        total = 0;
        idle = 0;

        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return false;

            // Lưu ý đặc thù của Windows API: kernelFt ĐÃ BAO GỒM luôn cả thời gian idleFt bên trong,
            // nên tổng thời gian CPU chỉ là kernel + user (không cộng thêm idle lần nữa).
            idle = ToUInt64(idleFt);
            total = ToUInt64(kernelFt) + ToUInt64(userFt);
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            // Trên Linux, dòng đầu của /proc/stat có dạng:
            //   cpu  user nice system idle iowait irq softirq steal ...
            // Trong đó: tổng tất cả các số = total; (idle + iowait) = thời gian CPU không bận tính toán.
            var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
                return false;

            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return false;

            for (int i = 1; i < parts.Length; i++)
            {
                if (ulong.TryParse(parts[i], out var val))
                    total += val;
            }

            ulong.TryParse(parts[4], out var idleTicks);
            var iowaitTicks = parts.Length > 5 && ulong.TryParse(parts[5], out var io) ? io : 0UL;
            idle = idleTicks + iowaitTicks;
            return total > 0;
        }

        return false;
    }

    // [2] Đo RAM đang sử dụng toàn hệ thống (Windows: GlobalMemoryStatusEx | Linux: /proc/meminfo)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;  // Tổng RAM vật lý (Bytes)
        public ulong ullAvailPhys;  // RAM vật lý còn trống (Bytes)
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static long GetMemoryUsedBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
                return (long)(mem.ullTotalPhys - mem.ullAvailPhys);
        }

        if (OperatingSystem.IsLinux())
        {
            // Đọc đúng chuẩn giống lệnh `free` của Linux từ /proc/meminfo (đơn vị trong file là kB):
            //   RAM đang dùng = (MemTotal - MemAvailable) * 1024 bytes.
            // Dùng MemAvailable thay vì MemFree vì Linux tận dụng RAM rảnh làm cache ổ đĩa và sẵn sàng trả lại khi cần.
            ulong totalKb = 0, availKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    totalKb = ParseMemInfoKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    availKb = ParseMemInfoKb(line);

                if (totalKb > 0 && availKb > 0)
                    break;
            }

            if (totalKb >= availKb && totalKb > 0)
                return (long)(totalKb - availKb) * 1024L;
        }

        // Dự phòng cho môi trường khác (ví dụ macOS).
        return Math.Max(0, GC.GetGCMemoryInfo().MemoryLoadBytes);
    }

    // Tách con số kB ở cột thứ 2 từ dòng dạng "MemTotal:       16384000 kB".
    private static ulong ParseMemInfoKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && ulong.TryParse(parts[1], out var kb) ? kb : 0;
    }

    // [3] Đo dung lượng ổ đĩa hệ điều hành còn trống
    public static long GetSystemDriveFreeBytes()
    {
        // Trên Linux, Environment.SystemDirectory trả về chuỗi rỗng "" (nếu truyền vào DriveInfo sẽ crash ngay),
        // nên phải phân biệt rõ: Windows lấy ổ chứa hệ điều hành (thường là "C:\"), còn Linux lấy gốc "/".
        var root = OperatingSystem.IsWindows()
            ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
            : "/";

        return new DriveInfo(root).AvailableFreeSpace;
    }
}
