// Namespace chứa các class xử lý mạng cấp cao trong .NET (như DNS, HTTP, địa chỉ IP)
// Để dùng class Dns
using NetOps.Shared;
using System.Net;
// Chứa các công cụ tầng mạng Socket (TCP, UDP và các giao thức mạng khác).
// Để dùng được bảng phân loại AddressFamily (AddressFamily.InterNetwork - ma
using System.Net.Sockets;
// Interop stand by Inter-operability (khả năng tương tác)
// Để dung class RuntimeInformation
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

    public static string GetLocalIpAddress()
    {
        return Dns.GetHostEntry(Dns.GetHostName())
                  .AddressList
                  .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                  .ToString();
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

    public static double GetTotalMemoryGb()
    {
        return GetTotalMemoryBytes() / (1024.0 * 1024 * 1024);
    }

    // 6. Lấy hoặc tạo Agent ID
    // Lần đầu chạy: tạo ID và lưu vào agent-id.txt.
    // Những lần sau: đọc lại ID đã lưu.
    // Trên Windows, vị trí lưu là %LOCALAPPDATA%\NetOps\Agent\agent-id.txt.
    public static string GetAgentId()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NetOps", "Agent");

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

    // Hàm đóng gói toàn bộ thông tin máy tính để sẵn sàng gửi qua gRPC
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
