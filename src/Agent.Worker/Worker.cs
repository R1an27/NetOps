using Agent.Worker.Services;

namespace Agent.Worker;

public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    TcpAgentClient tcpClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string host = configuration.GetValue<string>("AgentConfig:ServerHost") ?? "127.0.0.1";
        int port = configuration.GetValue<int>("AgentConfig:ServerPort", 5050);
        string agentId = configuration.GetValue<string>("AgentConfig:AgentId") ?? "agent-linux-01";
        int pingSeconds = configuration.GetValue<int>("AgentConfig:PingIntervalSeconds", 5);

        logger.LogInformation("Agent.Worker starting with AgentId: {AgentId}, Target: {Host}:{Port}",
            agentId, host, port);

        await tcpClient.RunAsync(host, port, agentId, TimeSpan.FromSeconds(pingSeconds), stoppingToken);
    }
}