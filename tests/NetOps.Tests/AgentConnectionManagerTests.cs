using Microsoft.Extensions.Logging.Abstractions;
using NetOps.Server.Services;
using NetOps.Shared;
using Xunit;

namespace NetOps.Tests;

public class AgentConnectionManagerTests
{
    [Fact]
    public void OldSessionCannotRemoveOrUpdateNewSession()
    {
        var manager = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
        var info = new RegisterAgentRequest
        {
            AgentId = "agent-1",
            Hostname = "lab",
            IpAddress = "127.0.0.1",
            OsDescription = "Linux"
        };
        manager.Register("old-session", info);
        manager.Register("new-session", info);

        Assert.False(manager.Unregister(info.AgentId, "old-session"));
        Assert.False(manager.UpdateHeartbeat(info.AgentId, "old-session", new HeartbeatMessage()));
        Assert.True(manager.TryGetConnection(info.AgentId, out var connection));
        Assert.Equal("new-session", connection!.ConnectionId);
        Assert.Null(connection.LatestHeartbeat);

        Assert.Single(manager.GetAgents(), a => a.AgentId == info.AgentId && a.IsOnline);

        Assert.True(manager.Unregister(info.AgentId, "new-session"));
        Assert.False(manager.TryGetConnection(info.AgentId, out _));
        Assert.Single(manager.GetAgents(), a => a.AgentId == info.AgentId && !a.IsOnline);
    }
}
