namespace NetOps.Shared;

public class AgentSummary
{
    public string AgentId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public bool IsOnline { get; set; }
}