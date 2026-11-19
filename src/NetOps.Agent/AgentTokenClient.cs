using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NetOps.Agent;

internal static class AgentTokenClient
{
    // Keycloak trả token trong trường JSON access_token. Agent chỉ cần đọc trường này.
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    public static async Task<string?> RequestAsync(
        HttpClient keycloak, Uri tokenUri, string agentId, string secretPath, CancellationToken ct)
    {
        // Đọc lại file mỗi lần xin token để quản trị viên có thể thay secret khi xoay khóa.
        var secret = (await File.ReadAllTextAsync(secretPath, ct)).Trim();
        if (secret.Length == 0)
            throw new InvalidDataException($"File secret của Agent đang trống: {secretPath}");

        // client_credentials là cách một thiết bị tự đăng nhập bằng ID và secret đã được cấp.
        // FormUrlEncodedContent mã hóa các ký tự đặc biệt trước khi gửi biểu mẫu đến Keycloak.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = agentId,
            ["client_secret"] = secret
        });
        using var response = await keycloak.PostAsync(tokenUri, form, ct);
        response.EnsureSuccessStatusCode();

        // Chỉ chuyển token sang SignalR sau khi Keycloak trả lời thành công.
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>(ct))?.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidDataException("Keycloak chưa trả access_token cho Agent.");
        return token;
    }
}
