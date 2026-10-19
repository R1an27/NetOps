// Thư viện này dùng để chuyển C# object thành chuỗi JSON và ngược lại
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Cấu trúc khung thông điệp tiêu chuẩn trao đổi giữa Manager và Agent theo chuẩn NetOps Protocol v0.
/// </summary>
public class NetOpsMessage
{
    /// <summary>
    /// Cấu hình JsonSerializerOptions tiêu chuẩn cho toàn bộ quá trình mã hóa/giải mã payload của NetOps Protocol v0.
    /// Bỏ qua trường null để tối ưu băng thông wire size và hỗ trợ case-insensitive property matching.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static JsonSerializerOptions ErrorJsonOptions => DefaultJsonOptions;

    /// <summary>
    /// Mã định danh duy nhất của gói tin (32 ký tự GUID dạng 'N').
    /// </summary>
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Mã định danh của luồng tác vụ logic (Request correlation ID).
    /// Cho phép đối chiếu luồng lệnh bất đồng bộ, ghép đôi yêu cầu và phản hồi
    /// khi có nhiều tác vụ chạy song song trên cùng một kết nối full-duplex.
    /// </summary>
    [JsonPropertyName("requestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; set; }

    /// <summary>
    /// Mã tham chiếu phản hồi, lưu MessageId của thông điệp gốc khi phản hồi.
    /// </summary>
    [JsonPropertyName("inReplyTo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InReplyTo { get; set; }

    /// <summary>
    /// Loại bản tin (ví dụ: PING, PONG, COMMAND, HEARTBEAT).
    /// </summary>
    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// Dấu thời gian Unix timestamp (mili-giây UTC) tại thời điểm tạo gói tin.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Nội dung dữ liệu chính (chuỗi văn bản hoặc JSON payload).
    /// </summary>
    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Payload { get; set; }

    /// <summary>
    /// Giải mã chuỗi JSON trong Payload thành đối tượng DTO strongly-typed.
    /// Trả về default(T) nếu Payload là null hoặc rỗng.
    /// Hỗ trợ an toàn khi T là string (trả về chuỗi Payload trực tiếp hoặc unquoted).
    /// </summary>
    /// <typeparam name="T">Kiểu DTO mục tiêu</typeparam>
    /// <param name="options">Tùy chọn JsonSerializerOptions (nếu null sẽ dùng DefaultJsonOptions)</param>
    /// <returns>Đối tượng DTO giải mã được hoặc default(T)</returns>
    public T? GetPayload<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(Payload))
        {
            return default;
        }

        var effectiveOptions = options ?? DefaultJsonOptions;

        if (typeof(T) == typeof(string))
        {
            var trimmed = Payload.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            {
                try
                {
                    return (T?)(object?)JsonSerializer.Deserialize<string>(Payload, effectiveOptions);
                }
                catch
                {
                    return (T)(object)Payload;
                }
            }

            return (T)(object)Payload;
        }

        return JsonSerializer.Deserialize<T>(Payload, effectiveOptions);
    }

    /// <summary>
    /// Thử giải mã chuỗi JSON trong Payload thành đối tượng DTO strongly-typed một cách an toàn.
    /// Trả về false và gán default cho payload nếu Payload là null, rỗng hoặc xảy ra JsonException.
    /// </summary>
    /// <typeparam name="T">Kiểu DTO mục tiêu</typeparam>
    /// <param name="payload">Biến chứa đối tượng DTO giải mã được (hoặc default nếu thất bại)</param>
    /// <param name="options">Tùy chọn JsonSerializerOptions (nếu null sẽ dùng DefaultJsonOptions)</param>
    /// <returns>True nếu giải mã thành công; False nếu chuỗi rỗng hoặc định dạng JSON không hợp lệ</returns>
    public bool TryGetPayload<T>(out T? payload, JsonSerializerOptions? options = null)
    {
        payload = default;

        if (string.IsNullOrWhiteSpace(Payload))
        {
            return false;
        }

        try
        {
            payload = GetPayload<T>(options);
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = default;
            return false;
        }
    }

    /// <summary>
    /// Mã hóa đối tượng DTO strongly-typed thành chuỗi JSON và gán vào Payload.
    /// Gán null nếu đối tượng đầu vào là null.
    /// </summary>
    /// <typeparam name="T">Kiểu DTO nguồn</typeparam>
    /// <param name="payload">Đối tượng DTO cần mã hóa</param>
    /// <param name="options">Tùy chọn JsonSerializerOptions (nếu null sẽ dùng DefaultJsonOptions)</param>
    public void SetPayload<T>(T? payload, JsonSerializerOptions? options = null)
    {
        if (payload is null)
        {
            Payload = null;
            return;
        }

        Payload = JsonSerializer.Serialize(payload, options ?? DefaultJsonOptions);
    }

    /// <summary>
    /// Tạo thông điệp phản hồi kế thừa InReplyTo và RequestId từ thông điệp hiện tại
    /// </summary>
    /// <param name="responseType">Loại thông điệp phản hồi (ví dụ MessageType.Pong)</param>
    /// <param name="payload">Nội dung dữ liệu đính kèm (tùy chọn)</param>
    /// <returns>Đối tượng NetOpsMessage phản hồi đã được chuẩn hóa</returns>
    public NetOpsMessage CreateResponse(string responseType, string? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseType);

        return new NetOpsMessage
        {
            MessageType = responseType,
            InReplyTo = MessageId,
            RequestId = RequestId ?? MessageId,
            Payload = payload
        };
    }

    /// <summary>
    /// Tạo thông điệp phản hồi với Typed Payload
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu Payload</typeparam>
    /// <param name="responseType">Loại thông điệp phản hồi</param>
    /// <param name="payload">Dữ liệu strongly-typed payload</param>
    /// <returns>Đối tượng NetOpsMessage phản hồi đã được chuẩn hóa</returns>
    public NetOpsMessage CreateResponse<T>(string responseType, T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseType);

        return new NetOpsMessage
        {
            MessageType = responseType,
            InReplyTo = MessageId,
            RequestId = RequestId ?? MessageId,
            Payload = payload is null ? null : JsonSerializer.Serialize(payload, ErrorJsonOptions)
        };
    }

    /// <summary>
    /// Factory method tĩnh tạo thông điệp phản hồi từ một request
    /// </summary>
    /// <param name="request">Thông điệp yêu cầu gốc</param>
    /// <param name="responseType">Loại thông điệp phản hồi</param>
    /// <param name="payload">Chuỗi dữ liệu payload (tùy chọn)</param>
    /// <returns>Đối tượng NetOpsMessage phản hồi</returns>
    public static NetOpsMessage CreateResponse(NetOpsMessage request, string responseType, string? payload = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.CreateResponse(responseType, payload);
    }

    /// <summary>
    /// Factory method tĩnh tạo thông điệp phản hồi với Typed Payload
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu Payload</typeparam>
    /// <param name="request">Thông điệp yêu cầu gốc</param>
    /// <param name="responseType">Loại thông điệp phản hồi</param>
    /// <param name="payload">Dữ liệu strongly-typed payload</param>
    /// <returns>Đối tượng NetOpsMessage phản hồi</returns>
    public static NetOpsMessage CreateResponse<T>(NetOpsMessage request, string responseType, T payload)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.CreateResponse(responseType, payload);
    }

    /// <summary>
    /// Tạo thông điệp báo lỗi chuẩn hóa (theo protocol-v0-spec) phản hồi cho thông điệp hiện tại
    /// </summary>
    /// <param name="errorCode">Mã lỗi (ví dụ: INVALID_MESSAGE_TYPE, TIMEOUT)</param>
    /// <param name="message">Thông báo mô tả lỗi</param>
    /// <param name="details">Chi tiết lỗi bổ sung (tùy chọn)</param>
    /// <returns>Đối tượng NetOpsMessage loại ERROR</returns>
    public NetOpsMessage CreateError(string errorCode, string message, string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        string errorPayload = JsonSerializer.Serialize(new
        {
            errorCode,
            message,
            details
        }, ErrorJsonOptions);

        return new NetOpsMessage
        {
            MessageType = ServerManager.Shared.Contracts.Models.MessageType.Error,
            InReplyTo = MessageId,
            RequestId = RequestId ?? MessageId,
            Payload = errorPayload
        };
    }

    /// <summary>
    /// Tạo thông điệp báo lỗi chuẩn hóa từ đối tượng ErrorMessagePayload phản hồi cho thông điệp hiện tại
    /// </summary>
    /// <param name="errorPayload">Đối tượng ErrorMessagePayload chứa thông tin lỗi</param>
    /// <returns>Đối tượng NetOpsMessage loại ERROR</returns>
    public NetOpsMessage CreateError(ServerManager.Shared.Contracts.Payloads.ErrorMessagePayload errorPayload)
    {
        ArgumentNullException.ThrowIfNull(errorPayload);
        return CreateError(errorPayload.ErrorCode, errorPayload.Message, errorPayload.Details);
    }

    /// <summary>
    /// Factory method tĩnh tạo thông điệp báo lỗi phản hồi từ một request
    /// </summary>
    /// <param name="request">Thông điệp yêu cầu gốc</param>
    /// <param name="errorCode">Mã lỗi</param>
    /// <param name="message">Thông điệp mô tả lỗi</param>
    /// <param name="details">Chi tiết lỗi bổ sung (tùy chọn)</param>
    /// <returns>Đối tượng NetOpsMessage loại ERROR</returns>
    public static NetOpsMessage CreateError(NetOpsMessage request, string errorCode, string message, string? details = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.CreateError(errorCode, message, details);
    }

    /// <summary>
    /// Factory method tĩnh tạo thông điệp báo lỗi từ request và đối tượng ErrorMessagePayload
    /// </summary>
    /// <param name="request">Thông điệp yêu cầu gốc</param>
    /// <param name="errorPayload">Đối tượng ErrorMessagePayload</param>
    /// <returns>Đối tượng NetOpsMessage loại ERROR</returns>
    public static NetOpsMessage CreateError(NetOpsMessage request, ServerManager.Shared.Contracts.Payloads.ErrorMessagePayload errorPayload)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errorPayload);
        return request.CreateError(errorPayload);
    }

    /// <summary>
    /// Factory method tĩnh tạo thông điệp báo lỗi độc lập (khi chưa có hoặc không có request hợp lệ)
    /// </summary>
    /// <param name="errorCode">Mã lỗi</param>
    /// <param name="message">Thông điệp mô tả lỗi</param>
    /// <param name="details">Chi tiết lỗi bổ sung (tùy chọn)</param>
    /// <param name="requestId">Mã định danh yêu cầu liên quan (tùy chọn)</param>
    /// <param name="inReplyTo">Mã thông điệp gốc liên quan (tùy chọn)</param>
    /// <returns>Đối tượng NetOpsMessage loại ERROR</returns>
    public static NetOpsMessage CreateError(string errorCode, string message, string? details = null, string? requestId = null, string? inReplyTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        string errorPayload = JsonSerializer.Serialize(new
        {
            errorCode,
            message,
            details
        }, ErrorJsonOptions);

        return new NetOpsMessage
        {
            MessageType = ServerManager.Shared.Contracts.Models.MessageType.Error,
            InReplyTo = inReplyTo,
            RequestId = requestId,
            Payload = errorPayload
        };
    }

    /// <summary>
    /// Factory method khởi tạo một thông điệp yêu cầu (request) mới với RequestId tự sinh hoặc chỉ định
    /// </summary>
    /// <param name="messageType">Loại thông điệp yêu cầu (ví dụ: MessageType.Ping, MessageType.Command)</param>
    /// <param name="payload">Nội dung gói tin (tùy chọn)</param>
    /// <param name="requestId">Mã định danh luồng yêu cầu (nếu để trống sẽ tự lấy theo MessageId)</param>
    /// <returns>Đối tượng NetOpsMessage yêu cầu mới</returns>
    public static NetOpsMessage CreateRequest(string messageType, string? payload = null, string? requestId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        var message = new NetOpsMessage
        {
            MessageType = messageType,
            Payload = payload
        };

        message.RequestId = string.IsNullOrWhiteSpace(requestId) ? message.MessageId : requestId;
        return message;
    }

    /// <summary>
    /// Factory method khởi tạo thông điệp yêu cầu (request) mới với Typed Payload
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu Payload</typeparam>
    /// <param name="messageType">Loại thông điệp yêu cầu</param>
    /// <param name="payload">Đối tượng strongly-typed payload</param>
    /// <param name="requestId">Mã định danh luồng yêu cầu (nếu để trống sẽ tự lấy theo MessageId)</param>
    /// <returns>Đối tượng NetOpsMessage yêu cầu mới</returns>
    public static NetOpsMessage CreateRequest<T>(string messageType, T payload, string? requestId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        var message = new NetOpsMessage
        {
            MessageType = messageType,
            Payload = payload is null ? null : JsonSerializer.Serialize(payload, ErrorJsonOptions)
        };

        message.RequestId = string.IsNullOrWhiteSpace(requestId) ? message.MessageId : requestId;
        return message;
    }

    /// <summary>
    /// Factory method tĩnh khởi tạo một thông điệp NetOpsMessage tổng quát với nội dung chuỗi (hoặc null).
    /// Khác với CreateRequest, phương thức này tôn trọng RequestId được truyền vào và không tự động gán MessageId khi null.
    /// </summary>
    /// <param name="messageType">Loại thông điệp (ví dụ: MessageType.Ping, MessageType.Heartbeat)</param>
    /// <param name="payload">Nội dung gói tin chuỗi văn bản (tùy chọn)</param>
    /// <param name="requestId">Mã định danh luồng yêu cầu liên kết (tùy chọn, mặc định null)</param>
    /// <returns>Đối tượng NetOpsMessage mới</returns>
    /// <exception cref="ArgumentException">Ném ra khi messageType là null hoặc rỗng</exception>
    public static NetOpsMessage Create(string messageType, string? payload = null, string? requestId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        return new NetOpsMessage
        {
            MessageType = messageType,
            Payload = payload,
            RequestId = requestId
        };
    }

    /// <summary>
    /// Factory method tĩnh khởi tạo một thông điệp NetOpsMessage tổng quát với Typed Payload.
    /// Đối tượng payload sẽ được tự động tuần tự hóa sang chuỗi JSON (hoặc null nếu payload là null).
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu Payload</typeparam>
    /// <param name="messageType">Loại thông điệp</param>
    /// <param name="payload">Đối tượng strongly-typed payload (tùy chọn)</param>
    /// <param name="requestId">Mã định danh luồng yêu cầu liên kết (tùy chọn, mặc định null)</param>
    /// <returns>Đối tượng NetOpsMessage mới chứa payload đã được tuần tự hóa</returns>
    /// <exception cref="ArgumentException">Ném ra khi messageType là null hoặc rỗng</exception>
    public static NetOpsMessage Create<T>(string messageType, T? payload = default, string? requestId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        return new NetOpsMessage
        {
            MessageType = messageType,
            Payload = payload is null ? null : JsonSerializer.Serialize(payload, ErrorJsonOptions),
            RequestId = requestId
        };
    }
}