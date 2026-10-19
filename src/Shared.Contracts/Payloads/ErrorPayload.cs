using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Payloads;

/// <summary>
/// Payload cho thông điệp ERROR báo lỗi chuẩn hóa (Mục 4.5 protocol-v0-spec).
/// Chứa mã lỗi tiêu chuẩn, thông báo giải thích và chi tiết bổ sung tùy chọn.
/// </summary>
public class ErrorMessagePayload
{
    /// <summary>
    /// Mã lỗi đại diện (ví dụ: INVALID_MESSAGE_TYPE, TIMEOUT, INTERNAL_ERROR, UNSUPPORTED_MESSAGE_TYPE).
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Thông báo mô tả nguyên nhân lỗi dễ đọc đối với người dùng hoặc hệ thống.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Chi tiết lỗi bổ sung, stacktrace hoặc ngữ cảnh phát sinh lỗi (tùy chọn).
    /// </summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Details { get; set; }

    /// <summary>
    /// Khởi tạo một đối tượng ErrorMessagePayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public ErrorMessagePayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng ErrorMessagePayload với mã lỗi, thông điệp và chi tiết tùy chọn.
    /// </summary>
    /// <param name="errorCode">Mã lỗi tiêu chuẩn</param>
    /// <param name="message">Thông điệp giải thích lỗi</param>
    /// <param name="details">Chi tiết bổ sung về lỗi (tùy chọn)</param>
    public ErrorMessagePayload(string errorCode, string message, string? details = null)
    {
        ErrorCode = errorCode;
        Message = message;
        Details = details;
    }
}

/// <summary>
/// Bí danh tương thích ngược cho <see cref="ErrorMessagePayload"/>.
/// Giữ nguyên cho các phiên bản và mã nguồn hiện tại đang sử dụng tên ErrorPayload.
/// </summary>
public class ErrorPayload : ErrorMessagePayload
{
    /// <summary>
    /// Khởi tạo một đối tượng ErrorPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public ErrorPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng ErrorPayload với mã lỗi, thông điệp và chi tiết tùy chọn.
    /// </summary>
    /// <param name="errorCode">Mã lỗi tiêu chuẩn</param>
    /// <param name="message">Thông điệp giải thích lỗi</param>
    /// <param name="details">Chi tiết bổ sung về lỗi (tùy chọn)</param>
    public ErrorPayload(string errorCode, string message, string? details = null)
        : base(errorCode, message, details) { }
}
