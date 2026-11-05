// Namespace chứa các class xử lý mạng cấp cao trong .NET (như DNS, HTTP, địa chỉ IP)
// Để dùng class Dns
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

}
