using NetOps.Shared;
// Để sử dụng AuthenticationHeaderValue giúp đính kèm Bearer Token vào header HTTP
using System.Net.Http.Headers;
// Thư viện xử lý đọc ghi JSON qua HTTP
using System.Net.Http.Json;

namespace NetOps.Desktop;

public class MetricsApiClient
{
    private readonly HttpClient _httpClient;
    // Hàm delegate để lấy token mới nhất từ Form chính
    private readonly Func<string?> _tokenProvider;

    public MetricsApiClient(string baseAddress, Func<string?> tokenProvider)
    {
        _tokenProvider = tokenProvider;

        // Bỏ qua lỗi SSL Certificate tự ký trong môi trường phát triển cục bộ (localhost)
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        // Khởi tạo HttpClient có sử dụng handler bỏ qua SSL
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(5) // Quá 5 giây không phản hồi thì ngắt
        };
    }

    // Hàm gọi API lấy metrics của một Agent, tự động gắn Bearer Token
    public async Task<HostMetricsSummaryDto?> GetHostMetricsAsync(string agentId, CancellationToken ct = default)
    {
        try
        {
            var token = _tokenProvider();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/summary");

            // Gắn token xác thực nếu đã đăng nhập
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<HostMetricsSummaryDto>(cancellationToken: ct);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null; // Khi người dùng đổi máy hoặc tắt form thì hủy tác vụ
        }
        catch (Exception)
        {
            return null; // Server không online hoặc lỗi mạng
        }
    }

    // Hàm gọi API lấy chuỗi dữ liệu lịch sử theo dải thời gian (5m, 15m, 1h, 3h, 6h)
    public async Task<HostMetricsHistoryDto?> GetHostMetricsHistoryAsync(
        string agentId,
        string range,
        CancellationToken ct = default)
    {
        try
        {
            var token = _tokenProvider();
            string url = $"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/history?range={Uri.EscapeDataString(range)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Gắn Bearer Token để vượt qua chính sách ReadMetrics của Server
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<HostMetricsHistoryDto>(cancellationToken: ct);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null; // Khi người dùng bấm đổi dải nhanh thì hủy an toàn
        }
        catch (Exception)
        {
            return null; // Lỗi mạng hoặc server không phản hồi
        }
    }
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