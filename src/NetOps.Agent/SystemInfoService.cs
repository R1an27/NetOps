// Namespace chứa các class xử lý mạng cấp cao trong .NET (như DNS, HTTP, địa chỉ IP)
// Để dùng class Dns và IPAddress
using NetOps.Shared;
using System.Net;
// Chứa NetworkInterface để quét trực tiếp các card mạng đang hoạt động trên cả Windows lẫn Linux
using System.Net.NetworkInformation;
// Chứa các công cụ tầng mạng Socket (TCP, UDP và các giao thức mạng khác).
// Để dùng được bảng phân loại AddressFamily (AddressFamily.InterNetwork - IPv4)
using System.Net.Sockets;
// Interop stand by Inter-operability (khả năng tương tác)
// Để dùng class RuntimeInformation
using System.Runtime.InteropServices;


namespace NetOps.Agent;

// Tạo một class với phạm vị truy cập public
// Static biến class này thành công cụ dùng chung thay vì là một blueprint
// Có thể gọi trực tiếp luôn mà không cần thông qua việc tạo đối tượng mới
// Những method bên trong buộc phải có static
public static class SystemInfoService
{
    // Lấy hostname
    public static string GetHostName()
    {
        return Dns.GetHostName();
    }

    // Lấy OS
    public static string GetOsDescription()
    {
        return RuntimeInformation.OSDescription;
    }

    // Lấy địa chỉ IPv4 cục bộ của máy bằng cách quét trực tiếp các card mạng đang bật.
    // Vì sao KHÔNG dùng Dns.GetHostEntry(Dns.GetHostName()).First(...) như cũ?
    // - Trên nhiều VM Linux (Ubuntu/Debian), /etc/hosts hay ánh xạ hostname về 127.0.1.1 hoặc không resolve được -> dễ crash hoặc lấy sai IP.
    // - Quét NetworkInterface lấy đúng IP thật của card mạng đang Up (bỏ qua loopback 127.0.0.1), và nếu không có mạng thì trả về 127.0.0.1 chứ không crash.
    public static string GetLocalIpAddress()
    {
        var ip = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                       && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(info => info.Address)
            .FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork
                                 && !IPAddress.IsLoopback(addr));

        return ip?.ToString() ?? IPAddress.Loopback.ToString();
    }

    // Lấy số nhân CPU
    public static int GetCpuCores()
    {
        return Environment.ProcessorCount;
    }

    // 5. Lấy tổng dung lượng RAM (đơn vị Bytes)
    // GB đổi ra bytes rất lớn nên dùng long là hợp lý
    public static long GetTotalMemoryBytes()
    {
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    // Agent đọc agent-id.txt và client-secret.txt từ cùng thư mục riêng của máy.
    // Thứ tự chọn thư mục:
    //   1. Biến môi trường NETOPS_DATA_DIR (ví dụ /var/lib/netops-agent khi chạy systemd service trên Linux).
    //   2. Thư mục LocalApplicationData (trên Windows là %LOCALAPPDATA%\NetOps\Agent).
    //   3. Dự phòng AppContext.BaseDirectory nếu chạy dưới service Linux không có biến $HOME (tránh đường dẫn rỗng).
    public static string GetDataDirectory()
    {
        var customDir = Environment.GetEnvironmentVariable("NETOPS_DATA_DIR");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return !string.IsNullOrWhiteSpace(customDir)
            ? customDir.Trim()
            : !string.IsNullOrWhiteSpace(localAppData)
                ? Path.Combine(localAppData, "NetOps", "Agent")
                : Path.Combine(AppContext.BaseDirectory, "data");
    }

    // Giữ ID cũ qua các lần nâng cấp vì quản trị viên dùng chính ID này làm Client ID trong Keycloak.
    public static string GetAgentId()
    {
        var folder = GetDataDirectory();
        Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "agent-id.txt");

        if (File.Exists(filePath))
        {
            var savedId = File.ReadAllText(filePath).Trim();

            if (!string.IsNullOrWhiteSpace(savedId))
                return savedId;
        }

        var agentId = $"agent-{Guid.NewGuid():N}";
        File.WriteAllText(filePath, agentId);

        return agentId;
    }

    // Hàm đóng gói toàn bộ thông tin máy tính để sẵn sàng gửi qua SignalR
    public static RegisterAgentRequest GetRegistrationInfo()
    {
        return new RegisterAgentRequest
        {
            AgentId = GetAgentId(),
            Hostname = GetHostName(),
            IpAddress = GetLocalIpAddress(),
            OsDescription = GetOsDescription(),
            CpuCores = GetCpuCores(),
            TotalMemoryBytes = GetTotalMemoryBytes()
        };
    }

}
