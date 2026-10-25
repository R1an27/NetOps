using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Payloads;

/// <summary>
/// Payload cho thông điệp AUTH gửi từ Agent lên Manager ngay sau khi kết nối TCP được thiết lập (Mục 4.2 protocol-v0-spec).
/// Chứa mã Agent, khóa bí mật token và các metadata hệ thống của máy Agent.
/// </summary>
public class AuthRequestPayload
{
    /// <summary>
    /// Mã định danh duy nhất của Agent (ví dụ: agent-linux-01).
    /// </summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Khóa bí mật hoặc mã token dùng để xác thực Agent với Manager.
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Thông tin phiên bản hệ điều hành máy Agent (tùy chọn).
    /// </summary>
    [JsonPropertyName("osInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsInfo { get; set; }

    /// <summary>
    /// Tên máy tính / hostname của Agent (tùy chọn).
    /// </summary>
    [JsonPropertyName("hostname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hostname { get; set; }

    /// <summary>
    /// Khởi tạo một đối tượng AuthRequestPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public AuthRequestPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng AuthRequestPayload với các thông tin xác thực của Agent.
    /// </summary>
    /// <param name="agentId">Mã định danh duy nhất của Agent</param>
    /// <param name="token">Mã token bí mật để xác thực</param>
    /// <param name="osInfo">Thông tin hệ điều hành của máy Agent (tùy chọn)</param>
    /// <param name="hostname">Tên máy chủ của Agent (tùy chọn)</param>
    public AuthRequestPayload(string agentId, string token, string? osInfo = null, string? hostname = null)
    {
        AgentId = agentId;
        Token = token;
        OsInfo = osInfo;
        Hostname = hostname;
    }
}

/// <summary>
/// Bí danh tương thích ngược cho <see cref="AuthRequestPayload"/>.
/// Giữ nguyên cho các phiên bản và mã nguồn hiện tại đang sử dụng tên AuthPayload.
/// </summary>
public class AuthPayload : AuthRequestPayload
{
    /// <summary>
    /// Khởi tạo một đối tượng AuthPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public AuthPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng AuthPayload với các thông tin xác thực của Agent.
    /// </summary>
    /// <param name="agentId">Mã định danh duy nhất của Agent</param>
    /// <param name="token">Mã token bí mật để xác thực</param>
    /// <param name="osInfo">Thông tin hệ điều hành của máy Agent (tùy chọn)</param>
    /// <param name="hostname">Tên máy chủ của Agent (tùy chọn)</param>
    public AuthPayload(string agentId, string token, string? osInfo = null, string? hostname = null)
        : base(agentId, token, osInfo, hostname) { }
}

/// <summary>
/// Payload phản hồi xác thực hợp nhất từ Manager gửi về Agent cho cả trường hợp chấp thuận (AUTH_OK) và từ chối (AUTH_FAILED).
/// Cung cấp cờ Success, mã phiên làm việc SessionId hoặc mã lỗi ErrorCode.
/// </summary>
public class AuthResponsePayload
{
    /// <summary>
    /// Cho biết quá trình xác thực thành công hay thất bại.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Mã định danh phiên làm việc được cấp bởi Manager khi xác thực thành công (tùy chọn).
    /// </summary>
    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; set; }

    /// <summary>
    /// Mã lỗi từ chối xác thực khi thất bại (ví dụ: AUTH_INVALID_TOKEN, AGENT_BLOCKED) (tùy chọn).
    /// </summary>
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Thông điệp giải thích kết quả xác thực từ Manager.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Khởi tạo một đối tượng AuthResponsePayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public AuthResponsePayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng AuthResponsePayload với đầy đủ các thuộc tính.
    /// </summary>
    /// <param name="success">Trạng thái xác thực thành công hay không</param>
    /// <param name="sessionId">Mã phiên làm việc (nếu thành công)</param>
    /// <param name="errorCode">Mã lỗi (nếu thất bại)</param>
    /// <param name="message">Thông điệp phản hồi từ Manager</param>
    public AuthResponsePayload(bool success, string? sessionId = null, string? errorCode = null, string? message = null)
    {
        Success = success;
        SessionId = sessionId;
        ErrorCode = errorCode;
        Message = message ?? (success ? "Authenticated successfully" : "Authentication failed");
    }

    /// <summary>
    /// Factory helper tạo payload phản hồi xác thực thành công (AUTH_OK).
    /// </summary>
    /// <param name="sessionId">Mã phiên làm việc được cấp</param>
    /// <param name="message">Thông điệp chào mừng</param>
    /// <returns>Đối tượng AuthResponsePayload với Success = true</returns>
    public static AuthResponsePayload Ok(string sessionId, string message = "Authenticated successfully")
    {
        return new AuthResponsePayload(true, sessionId: sessionId, message: message);
    }

    /// <summary>
    /// Factory helper tạo payload phản hồi xác thực thất bại (AUTH_FAILED).
    /// </summary>
    /// <param name="errorCode">Mã lỗi xác thực (mặc định AUTH_INVALID_TOKEN)</param>
    /// <param name="message">Thông báo lý do từ chối</param>
    /// <returns>Đối tượng AuthResponsePayload với Success = false</returns>
    public static AuthResponsePayload Failed(string errorCode = "AUTH_INVALID_TOKEN", string message = "Invalid agent secret key")
    {
        return new AuthResponsePayload(false, errorCode: errorCode, message: message);
    }
}

/// <summary>
/// Payload cho thông điệp AUTH_OK khi Manager chấp thuận phiên kết nối của Agent (Mục 4.2 protocol-v0-spec).
/// Kế thừa từ <see cref="AuthResponsePayload"/> để tương thích ngược hoàn hảo.
/// </summary>
public class AuthOkPayload : AuthResponsePayload
{
    /// <summary>
    /// Khởi tạo một đối tượng AuthOkPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public AuthOkPayload() : base(true)
    {
        Message = "Authenticated successfully";
    }

    /// <summary>
    /// Khởi tạo một đối tượng AuthOkPayload với mã phiên làm việc và thông báo.
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên làm việc</param>
    /// <param name="message">Thông điệp phản hồi từ Manager</param>
    public AuthOkPayload(string sessionId, string message = "Authenticated successfully")
        : base(true, sessionId: sessionId, message: message)
    {
    }
}

/// <summary>
/// Payload cho thông điệp AUTH_FAILED khi Manager từ chối Agent (Mục 4.2 protocol-v0-spec).
/// Kế thừa từ <see cref="AuthResponsePayload"/> để tương thích ngược hoàn hảo.
/// </summary>
public class AuthFailedPayload : AuthResponsePayload
{
    /// <summary>
    /// Khởi tạo một đối tượng AuthFailedPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public AuthFailedPayload() : base(false)
    {
        ErrorCode = "AUTH_INVALID_TOKEN";
        Message = "Invalid agent secret key";
    }

    /// <summary>
    /// Khởi tạo một đối tượng AuthFailedPayload với mã lỗi và thông báo chi tiết.
    /// </summary>
    /// <param name="errorCode">Mã lỗi xác thực</param>
    /// <param name="message">Thông điệp mô tả nguyên nhân lỗi</param>
    public AuthFailedPayload(string errorCode, string message = "Invalid agent secret key")
        : base(false, errorCode: errorCode, message: message)
    {
    }
}
