using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetOps.Shared;

namespace NetOps.Desktop;

public sealed class MetricsApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _getTokenAsync;

    public MetricsApiClient(string baseAddress, Func<CancellationToken, Task<string?>> getTokenAsync)
    {
        _getTokenAsync = getTokenAsync;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public Task<HostMetricsSummaryDto?> GetHostMetricsAsync(string agentId, CancellationToken ct = default) =>
        GetAsync<HostMetricsSummaryDto>($"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/summary", ct);

    public Task<HostMetricsHistoryDto?> GetHostMetricsHistoryAsync(string agentId, string range, CancellationToken ct = default) =>
        GetAsync<HostMetricsHistoryDto>($"/api/hosts/{Uri.EscapeDataString(agentId)}/metrics/history?range={Uri.EscapeDataString(range)}", ct);

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            var token = await _getTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                : default;
        }
        catch (Exception)
        {
            return default;
        }
    }

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