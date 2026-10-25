using System.Text.Json.Serialization;

namespace ServerManager.Shared.Contracts.Payloads;

/// <summary>
/// Payload cho thông điệp COMMAND gửi từ Manager xuống Agent để thực thi lệnh shell (Mục 4.4 protocol-v0-spec).
/// Chứa mã lệnh, chuỗi câu lệnh shell, thời hạn thực thi tối đa và thư mục làm việc tùy chọn.
/// </summary>
public class CommandRequestPayload
{
    /// <summary>
    /// Mã định danh duy nhất của tác vụ thực thi lệnh (UUID hoặc ID nghiệp vụ).
    /// </summary>
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi câu lệnh shell cần thực thi trên máy Agent.
    /// </summary>
    [JsonPropertyName("commandLine")]
    public string CommandLine { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian chờ tối đa cho phép thực thi lệnh (tính bằng giây), mặc định là 30 giây.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Thư mục làm việc trên máy Agent khi thực thi lệnh (tùy chọn).
    /// </summary>
    [JsonPropertyName("workingDir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDir { get; set; }

    /// <summary>
    /// Khởi tạo một đối tượng CommandRequestPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public CommandRequestPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng CommandRequestPayload với đầy đủ tham số thực thi.
    /// </summary>
    /// <param name="commandId">Mã định danh duy nhất của lệnh</param>
    /// <param name="commandLine">Chuỗi lệnh shell cần thực thi</param>
    /// <param name="timeoutSeconds">Thời gian hết hạn tối đa (giây)</param>
    /// <param name="workingDir">Thư mục làm việc thực thi lệnh (tùy chọn)</param>
    public CommandRequestPayload(string commandId, string commandLine, int timeoutSeconds = 30, string? workingDir = null)
    {
        CommandId = commandId;
        CommandLine = commandLine;
        TimeoutSeconds = timeoutSeconds;
        WorkingDir = workingDir;
    }
}

/// <summary>
/// Bí danh tương thích ngược cho <see cref="CommandRequestPayload"/>.
/// Giữ nguyên cho các phiên bản và mã nguồn hiện tại đang sử dụng tên CommandPayload.
/// </summary>
public class CommandPayload : CommandRequestPayload
{
    /// <summary>
    /// Khởi tạo một đối tượng CommandPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public CommandPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng CommandPayload với đầy đủ tham số thực thi.
    /// </summary>
    /// <param name="commandId">Mã định danh duy nhất của lệnh</param>
    /// <param name="commandLine">Chuỗi lệnh shell cần thực thi</param>
    /// <param name="timeoutSeconds">Thời gian hết hạn tối đa (giây)</param>
    /// <param name="workingDir">Thư mục làm việc thực thi lệnh (tùy chọn)</param>
    public CommandPayload(string commandId, string commandLine, int timeoutSeconds = 30, string? workingDir = null)
        : base(commandId, commandLine, timeoutSeconds, workingDir) { }
}

/// <summary>
/// Payload cho thông điệp COMMAND_OUTPUT truyền từng phần kết quả stdout/stderr từ Agent (Mục 4.4 protocol-v0-spec).
/// </summary>
public class CommandOutputPayload
{
    /// <summary>
    /// Mã định danh của lệnh đang truyền phát kết quả.
    /// </summary>
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi đoạn xuất dữ liệu của tiến trình (stdout hoặc stderr).
    /// </summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Cho biết đoạn dữ liệu xuất này có phải từ luồng lỗi tiêu chuẩn (stderr) hay không.
    /// </summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    /// <summary>
    /// Khởi tạo một đối tượng CommandOutputPayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public CommandOutputPayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng CommandOutputPayload với dữ liệu đoạn xuất và phân loại luồng.
    /// </summary>
    /// <param name="commandId">Mã định danh của lệnh</param>
    /// <param name="output">Đoạn dữ liệu xuất</param>
    /// <param name="isError">Cờ chỉ định dữ liệu thuộc luồng lỗi stderr</param>
    public CommandOutputPayload(string commandId, string output, bool isError = false)
    {
        CommandId = commandId;
        Output = output;
        IsError = isError;
    }
}

/// <summary>
/// Payload cho thông điệp COMMAND_DONE thông báo đã hoàn tất thực thi lệnh (Mục 4.4 protocol-v0-spec).
/// </summary>
public class CommandDonePayload
{
    /// <summary>
    /// Mã định danh của lệnh đã hoàn thành thực thi.
    /// </summary>
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// Mã thoát của tiến trình (0 biểu thị thành công, khác 0 biểu thị lỗi).
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }

    /// <summary>
    /// Tổng thời gian thực thi lệnh thực tế tính bằng mili-giây.
    /// </summary>
    [JsonPropertyName("totalExecutionTimeMs")]
    public long TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Cho biết lệnh đã thực thi thành công hay không (ExitCode == 0).
    /// Bỏ qua khi tuần tự hóa JSON.
    /// </summary>
    [JsonIgnore]
    public bool Success => ExitCode == 0;

    /// <summary>
    /// Khởi tạo một đối tượng CommandDonePayload rỗng phục vụ JSON deserialization.
    /// </summary>
    public CommandDonePayload() { }

    /// <summary>
    /// Khởi tạo một đối tượng CommandDonePayload với mã thoát và tổng thời gian thực thi.
    /// </summary>
    /// <param name="commandId">Mã định danh của lệnh</param>
    /// <param name="exitCode">Mã thoát của tiến trình</param>
    /// <param name="totalExecutionTimeMs">Tổng thời gian thực thi (mili-giây)</param>
    public CommandDonePayload(string commandId, int exitCode, long totalExecutionTimeMs)
    {
        CommandId = commandId;
        ExitCode = exitCode;
        TotalExecutionTimeMs = totalExecutionTimeMs;
    }
}
