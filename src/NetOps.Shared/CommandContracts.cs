namespace NetOps.Shared;

public sealed class SubmitCommandRequest
{
    public string AgentId { get; set; } = "";
    public string CommandText { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class SubmitCommandResponse
{
    public string CommandId { get; set; } = "";
}

public sealed class CommandOutput
{
    public string AgentId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string OutputLine { get; set; } = "";
    public bool IsError { get; set; }
    public bool IsCompleted { get; set; }
    public int ExitCode { get; set; }
}
