using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NetOps.Server.Services;
using NetOps.Shared;
using Xunit;

namespace NetOps.Tests;

public class AgentHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _server;
    private readonly AgentConnectionManager _connections;
    private readonly IMemoryCache _cache;

    public AgentHubTests(WebApplicationFactory<Program> server)
    {
        _server = server;
        _connections = server.Services.GetRequiredService<AgentConnectionManager>();
        _cache = server.Services.GetRequiredService<IMemoryCache>();
    }

    [Fact]
    public async Task RegistrationAndHeartbeatAreStoredThroughWebSockets()
    {
        await using var client = CreateClient();
        await client.StartAsync();
        var info = CreateRegistration();

        var reply = await client.InvokeAsync<RegisterAgentResponse>("Register", info);
        Assert.True(reply.Success);

        var heartbeat = new HeartbeatMessage
        {
            AgentId = info.AgentId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CpuUsagePercent = 12.5,
            MemoryUsedBytes = 1024
        };
        await client.InvokeAsync("Heartbeat", heartbeat);

        Assert.True(_connections.TryGetConnection(info.AgentId, out var connection));
        Assert.Equal(info.Hostname, connection!.Info.Hostname);
        Assert.Equal(heartbeat.TimestampUnixMs, connection.LatestHeartbeat!.TimestampUnixMs);
        Assert.Equal(12.5, connection.LatestHeartbeat.CpuUsagePercent);
    }

    [Fact]
    public async Task HeartbeatRequiresRegistration()
    {
        await using var client = CreateClient();
        await client.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync("Heartbeat",
            new HeartbeatMessage { AgentId = "unregistered" }));
    }

    [Fact]
    public async Task HeartbeatCannotUpdateAnotherAgent()
    {
        await using var first = CreateClient();
        await using var second = CreateClient();
        await first.StartAsync();
        await second.StartAsync();
        var firstInfo = CreateRegistration();
        var secondInfo = CreateRegistration();
        await first.InvokeAsync<RegisterAgentResponse>("Register", firstInfo);
        await second.InvokeAsync<RegisterAgentResponse>("Register", secondInfo);

        await Assert.ThrowsAsync<HubException>(() => first.InvokeAsync("Heartbeat",
            new HeartbeatMessage { AgentId = secondInfo.AgentId }));

        Assert.True(_connections.TryGetConnection(secondInfo.AgentId, out var connection));
        Assert.Null(connection!.LatestHeartbeat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegistrationRejectsEmptyAgentId(string agentId)
    {
        await using var client = CreateClient();
        await client.StartAsync();
        var info = CreateRegistration();
        info.AgentId = agentId;

        await Assert.ThrowsAsync<HubException>(() =>
            client.InvokeAsync<RegisterAgentResponse>("Register", info));
        Assert.False(_connections.TryGetConnection(agentId, out _));
    }

    [Fact]
    public async Task OneConnectionCannotSwitchAgentIdentity()
    {
        await using var client = CreateClient();
        await client.StartAsync();
        var firstInfo = CreateRegistration();
        var secondInfo = CreateRegistration();
        await client.InvokeAsync<RegisterAgentResponse>("Register", firstInfo);

        await Assert.ThrowsAsync<HubException>(() =>
            client.InvokeAsync<RegisterAgentResponse>("Register", secondInfo));
        Assert.False(_connections.TryGetConnection(secondInfo.AgentId, out _));
    }

    [Fact]
    public async Task ReconnectedAgentRejectsHeartbeatFromOldSession()
    {
        await using var oldClient = CreateClient();
        await using var newClient = CreateClient();
        await oldClient.StartAsync();
        await newClient.StartAsync();
        var info = CreateRegistration();
        await oldClient.InvokeAsync<RegisterAgentResponse>("Register", info);
        await newClient.InvokeAsync<RegisterAgentResponse>("Register", info);

        await Assert.ThrowsAsync<HubException>(() => oldClient.InvokeAsync("Heartbeat",
            new HeartbeatMessage { AgentId = info.AgentId }));
        await oldClient.StopAsync();

        await newClient.InvokeAsync("Heartbeat", new HeartbeatMessage { AgentId = info.AgentId });
        Assert.True(_connections.TryGetConnection(info.AgentId, out _));
    }

    [Fact]
    public async Task DisconnectRemovesRegisteredAgent()
    {
        await using var client = CreateClient();
        await client.StartAsync();
        var info = CreateRegistration();
        await client.InvokeAsync<RegisterAgentResponse>("Register", info);

        await client.StopAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_connections.TryGetConnection(info.AgentId, out _))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task SendCommandOutputRejectsForgedAgentIdentityAndCleansCompletedRoute()
    {
        await using var client = CreateClient();
        await client.StartAsync();
        var info = CreateRegistration();
        await client.InvokeAsync<RegisterAgentResponse>("Register", info);

        var commandId = Guid.NewGuid().ToString("N");
        var routeKey = $"command:{commandId}";
        _cache.Set(routeKey, "desktop-conn-1");

        await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync("SendCommandOutput", new CommandOutput
        {
            AgentId = "forged-agent-id",
            CommandId = commandId,
            OutputLine = "bad"
        }));
        Assert.True(_cache.TryGetValue(routeKey, out _));

        await client.InvokeAsync("SendCommandOutput", new CommandOutput
        {
            AgentId = info.AgentId,
            CommandId = commandId,
            OutputLine = "done",
            IsCompleted = true,
            ExitCode = 0
        });
        Assert.False(_cache.TryGetValue(routeKey, out _));
    }

    [Fact]
    public async Task AgentHubContextDeliversCommandRequestToRegisteredAgent()
    {
        await using var client = CreateClient();
        var received = new TaskCompletionSource<CommandRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.On<CommandRequest>("ReceiveCommand", req => received.TrySetResult(req));
        await client.StartAsync();

        var info = CreateRegistration();
        await client.InvokeAsync<RegisterAgentResponse>("Register", info);
        Assert.True(_connections.TryGetConnection(info.AgentId, out var active));

        var hubContext = _server.Services.GetRequiredService<IHubContext<AgentHubService>>();
        var expected = new CommandRequest
        {
            CommandId = Guid.NewGuid().ToString("N"),
            CommandText = "echo netops",
            TimeoutSeconds = 15
        };
        await hubContext.Clients.Client(active!.ConnectionId).SendAsync("ReceiveCommand", expected);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected.CommandId, actual.CommandId);
        Assert.Equal(expected.CommandText, actual.CommandText);
        Assert.Equal(expected.TimeoutSeconds, actual.TimeoutSeconds);
    }

    private HubConnection CreateClient()
    {
        return new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/agents", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => _server.Server.CreateHandler();
                options.WebSocketFactory = async (context, token) =>
                    await _server.Server.CreateWebSocketClient().ConnectAsync(context.Uri, token);
            })
            .Build();
    }

    private static RegisterAgentRequest CreateRegistration() => new()
    {
        AgentId = $"test-{Guid.NewGuid():N}",
        Hostname = "test-machine",
        IpAddress = "127.0.0.1",
        OsDescription = "Test OS",
        CpuCores = 2,
        TotalMemoryBytes = 1024 * 1024 * 1024
    };
}
