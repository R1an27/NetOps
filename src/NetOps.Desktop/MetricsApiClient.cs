using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetOps.Shared;

namespace NetOps.Desktop;

// Lớp chuyên gọi HTTP API lên NetOps.Server để lấy thông số phần cứng (CPU, RAM, Disk) từ Prometheus.
// - sealed: khóa lớp này lại, không cho lớp khác kế thừa.
// - IDisposable: cam kết có hàm Dispose() để đóng sạch HttpClient khi tắt Form1, tránh rò rỉ kết nối mạng.
public sealed class MetricsApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    // Func<CancellationToken, Task<string?>> là gì?
    // Đây là một "hàm bất đồng bộ" nhận vào CancellationToken (để hủy nếu người dùng đổi máy/đóng cửa sổ)
    // và trả về Task<string?> chứa chuỗi Access Token mới nhất.
    // Vì sao dùng hàm bất đồng bộ (Task<string?>) thay vì truyền cứng 1 chuỗi string?
    // Vì trước khi gửi request, Form1 có thể cần gọi mạng lên Keycloak gia hạn (refresh) token nếu sắp hết hạn.
    private readonly Func<CancellationToken, Task<string?>> _getTokenAsync;

    public MetricsApiClient(string baseAddress, Func<CancellationToken, Task<string?>> getTokenAsync)
    {
        _getTokenAsync = getTokenAsync;

        // Dùng HttpClient mặc định để Windows tự kiểm tra chứng chỉ HTTPS (TLS) hợp lệ của Server.
        // KHÔNG dùng DangerousAcceptAnyServerCertificateValidator vì app gửi JWT Bearer token qua Internet công khai.
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            // Nếu Server quá 5 giây không phản hồi thì tự ngắt, tránh treo giao diện chờ mãi.
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    // Lấy chỉ số tức thời mới nhất (CPU %, RAM đang dùng, Disk còn trống) của 1 Agent.
    // Uri.EscapeDataString(agentId) mã hóa ký tự đặc biệt nếu có để không làm hỏng cấu trúc URL.
    public Task<HostMetricsSummaryDto?> GetHostMetricsAsync(string agentId, CancellationToken ct = default) =>
        GetAsync<HostMetricsSummaryDto>($"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/summary", ct);

    // Lấy chuỗi dữ liệu lịch sử để vẽ đồ thị theo khung thời gian (5m, 15m, 1h, 3h, 6h).
    public Task<HostMetricsHistoryDto?> GetHostMetricsHistoryAsync(string agentId, string range, CancellationToken ct = default) =>
        GetAsync<HostMetricsHistoryDto>($"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/history?range={Uri.EscapeDataString(range)}", ct);

    // Hàm dùng chung cho cả 2 API trên để tránh viết lặp lại đoạn code lấy token + gửi HTTP + đọc JSON.
    // <T> (Generic) là "kiểu dữ liệu linh hoạt":
    //   - Khi gọi từ GetHostMetricsAsync         -> T là HostMetricsSummaryDto
    //   - Khi gọi từ GetHostMetricsHistoryAsync  -> T là HostMetricsHistoryDto
    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            // 1. Xin Access Token còn hạn từ Form1 (nếu sắp hết hạn 5 phút thì Form1 tự refresh ngầm ở đây).
            var token = await _getTokenAsync(ct);

            // 2. Tạo gói yêu cầu HTTP GET và kẹp thẻ xác thực "Authorization: Bearer <token>" vào header.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 3. Gửi request lên Server; nếu Server trả 2xx (thành công) thì giải mã JSON sang kiểu T.
            // Ngược lại (ví dụ 401 chưa đăng nhập, 403 thiếu quyền, 500 lỗi server) thì trả default (tức null).
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                : default;
        }
        catch (Exception)
        {
            // Bắt mọi lỗi mạng tạm thời, timeout hoặc người dùng hủy request (OperationCanceledException)
            // rồi trả null để giao diện tự hiển thị trạng thái không có dữ liệu thay vì crash app.
            return default;
        }
    }

    // Giải phóng HttpClient khi Form1 đóng.
    public void Dispose() => _httpClient.Dispose();
}

// BỘ CHUYỂN ĐỔI ĐƠN VỊ 
public static class MetricsFormatter
{
    /// <summary>
    /// Chuyển đổi số byte thô thành chuỗi thân thiện (ví dụ: 4.5 GB, 512 MB).
    /// </summary>
    /// Ex: 5,368,709,120 Bytes --> 5.0 GB hoặc 512.0 MB.
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            unitIndex++;
            size /= 1024;
        }

        return $"{size:F1} {units[unitIndex]}";
    }
}