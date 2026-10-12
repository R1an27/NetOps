namespace ServerManager.Shared.Contracts.Models;

/// <summary>
/// Định nghĩa các loại thông điệp chuẩn trong giao thức mạng NetOps
/// </summary>
public static class MessageType
{
    public const string Ping = "PING";
    public const string Pong = "PONG";

    public const string Auth = "AUTH";
    public const string AuthOk = "AUTH_OK";
    public const string AuthFailed = "AUTH_FAILED";

    public const string Heartbeat = "HEARTBEAT";
    public const string HeartbeatAck = "HEARTBEAT_ACK";

    public const string Metrics = "METRICS";

    public const string Command = "COMMAND";
    public const string CommandOutput = "COMMAND_OUTPUT";
    public const string CommandDone = "COMMAND_DONE";

    public const string Error = "ERROR";
}