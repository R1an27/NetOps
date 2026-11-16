namespace NetOps.Shared;

public sealed class RegisterAgentRequest
{
    public string AgentId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public int CpuCores { get; set; }
    public long TotalMemoryBytes { get; set; }
}

public sealed class RegisterAgentResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public sealed class HeartbeatMessage
{
    public string AgentId { get; set; } = "";
    public long TimestampUnixMs { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long DiskFreeBytes { get; set; }
}

public sealed class CommandRequest
{
    public string CommandId { get; set; } = "";
    public string CommandText { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}
