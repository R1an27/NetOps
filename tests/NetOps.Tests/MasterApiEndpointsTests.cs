using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Master.Api.Endpoints;
using Master.Api.Options;
using Master.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử Unit & Functional cho các route Minimal API trong MasterApiEndpoints.
/// Kiểm chứng các route GET (/api/health, /api/agents, /api/agents/{sessionId})
/// và các route Unicast POST (/api/agents/{agentId}/command, /api/agents/{agentId}/message).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Master")]
public class MasterApiEndpointsTests
{
    private static async Task<(WebApplication App, HttpClient Client, IConnectionManager ConnectionManager)> CreateTestApiAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var connectionManager = new ConnectionManager();
        builder.Services.AddSingleton<IConnectionManager>(connectionManager);
        builder.Services.Configure<MasterServerOptions>(opt =>
        {
            opt.Port = 5050;
            opt.WatchdogIntervalSeconds = 5;
            opt.SessionTimeoutSeconds = 15;
        });

        // Sử dụng Kestrel Loopback cổng 0 ngẫu nhiên
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        app.MapMasterApiEndpoints();
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        return (app, client, connectionManager);
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithServerMetrics()
    {
        var (app, client, _) = await CreateTestApiAsync();
        try
        {
            var response = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Healthy", root.GetProperty("status").GetString());
            Assert.Equal(5050, root.GetProperty("tcpPort").GetInt32());
            Assert.Equal(0, root.GetProperty("activeConnections").GetInt32());
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetAgents_And_GetAgentById_ReturnsCorrectSessionData()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var ms1 = new MemoryStream();
        using var stream1 = new SafeFrameStream(ms1);
        var session1 = new AgentSession("sess-001", stream1, "127.0.0.1:4001", DateTimeOffset.UtcNow, agentId: "agent-alpha");

        connectionManager.TryRegister(session1);

        try
        {
            // 1. GET /api/agents
            var allResp = await client.GetAsync("/api/agents");
            Assert.Equal(HttpStatusCode.OK, allResp.StatusCode);

            var list = await allResp.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(list);
            Assert.Single(list);
            Assert.Equal("agent-alpha", list[0].GetProperty("agentId").GetString());
            Assert.Equal("sess-001", list[0].GetProperty("sessionId").GetString());

            // 2. GET /api/agents/{sessionId} - Success
            var singleResp = await client.GetAsync("/api/agents/sess-001");
            Assert.Equal(HttpStatusCode.OK, singleResp.StatusCode);

            var singleDoc = await singleResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("sess-001", singleDoc.GetProperty("sessionId").GetString());
            Assert.Equal("agent-alpha", singleDoc.GetProperty("agentId").GetString());

            // 3. GET /api/agents/{sessionId} - Not Found
            var notFoundResp = await client.GetAsync("/api/agents/unknown-session");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PostCommand_Success_SendsUnicastMessageToTargetAgent()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var msTarget = new MemoryStream();
        using var streamTarget = new SafeFrameStream(msTarget);
        var sessionTarget = new AgentSession("sess-target", streamTarget, "127.0.0.1:4002", DateTimeOffset.UtcNow, agentId: "agent-target");

        connectionManager.TryRegister(sessionTarget);

        try
        {
            var commandReq = new SendCommandRequest(
                CommandLine: "ipconfig /all",
                CommandId: "cmd-unit-01",
                TimeoutSeconds: 15);

            var response = await client.PostAsJsonAsync("/api/agents/agent-target/command", commandReq);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var respDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(respDoc.GetProperty("success").GetBoolean());
            Assert.Equal("agent-target", respDoc.GetProperty("agentId").GetString());
            Assert.Equal("cmd-unit-01", respDoc.GetProperty("commandId").GetString());
            Assert.Equal("ipconfig /all", respDoc.GetProperty("commandLine").GetString());

            // Kiểm tra stream của session đích đã nhận được frame dữ liệu nhị phân
            Assert.True(msTarget.Length > 0, "Dữ liệu lệnh phải được ghi vào luồng stream của Agent");
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PostCommand_AgentNotFound_Returns404()
    {
        var (app, client, _) = await CreateTestApiAsync();
        try
        {
            var commandReq = new SendCommandRequest(CommandLine: "dir");
            var response = await client.PostAsJsonAsync("/api/agents/agent-offline/command", commandReq);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var respDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(respDoc.GetProperty("success").GetBoolean());
            Assert.Contains("not connected or not found", respDoc.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PostCommand_EmptyCommandLine_Returns400BadRequest()
    {
        var (app, client, _) = await CreateTestApiAsync();
        try
        {
            var invalidReq = new SendCommandRequest(CommandLine: "   ");
            var response = await client.PostAsJsonAsync("/api/agents/agent-01/command", invalidReq);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PostMessage_SuccessAndNotFound_DispatchesUnicastNetOpsMessage()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var ms = new MemoryStream();
        using var stream = new SafeFrameStream(ms);
        var session = new AgentSession("sess-peer", stream, "127.0.0.1:4003", DateTimeOffset.UtcNow, agentId: "agent-peer");

        connectionManager.TryRegister(session);

        try
        {
            var testMsg = NetOpsMessage.Create(MessageType.Ping, "UNICAST_API_TEST", requestId: "req-api-99");

            // 1. Gửi thành công tới agent-peer
            var okResp = await client.PostAsJsonAsync("/api/agents/agent-peer/message", testMsg);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

            var okDoc = await okResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(okDoc.GetProperty("success").GetBoolean());
            Assert.Equal("agent-peer", okDoc.GetProperty("agentId").GetString());
            Assert.Equal("PING", okDoc.GetProperty("messageType").GetString(), ignoreCase: true);
            Assert.True(ms.Length > 0);

            // 2. Gửi tới Agent không tồn tại -> 404
            var notFoundResp = await client.PostAsJsonAsync("/api/agents/agent-ghost/message", testMsg);
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task BroadcastCommand_WithActiveAgents_Returns200WithResult()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        using var s1 = new SafeFrameStream(ms1);
        using var s2 = new SafeFrameStream(ms2);

        var session1 = new AgentSession("sess-bc-1", s1, "127.0.0.1:4011", DateTimeOffset.UtcNow, agentId: "agent-bc-1");
        var session2 = new AgentSession("sess-bc-2", s2, "127.0.0.1:4012", DateTimeOffset.UtcNow, agentId: "agent-bc-2");

        connectionManager.TryRegister(session1);
        connectionManager.TryRegister(session2);

        try
        {
            var req = new SendCommandRequest(CommandLine: "uptime", CommandId: "cmd-broadcast-01");
            var response = await client.PostAsJsonAsync("/api/broadcast/command", req);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(doc.GetProperty("success").GetBoolean());
            Assert.Equal("cmd-broadcast-01", doc.GetProperty("commandId").GetString());
            Assert.Equal("uptime", doc.GetProperty("commandLine").GetString());
            Assert.Equal(2, doc.GetProperty("totalTargeted").GetInt32());
            Assert.Equal(2, doc.GetProperty("successCount").GetInt32());
            Assert.Equal(0, doc.GetProperty("failedCount").GetInt32());

            Assert.True(ms1.Length > 0, "Agent 1 phải nhận được gói tin broadcast");
            Assert.True(ms2.Length > 0, "Agent 2 phải nhận được gói tin broadcast");
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task BroadcastCommand_WithZeroAgents_Returns200WithZeroCount()
    {
        var (app, client, _) = await CreateTestApiAsync();
        try
        {
            var req = new SendCommandRequest(CommandLine: "sync-time");
            var response = await client.PostAsJsonAsync("/api/broadcast/command", req);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(doc.GetProperty("success").GetBoolean());
            Assert.Equal(0, doc.GetProperty("totalTargeted").GetInt32());
            Assert.Equal(0, doc.GetProperty("successCount").GetInt32());
            Assert.Equal(0, doc.GetProperty("failedCount").GetInt32());
            Assert.Contains("No online agents", doc.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task BroadcastCommand_EmptyCommandLine_Returns400BadRequest()
    {
        var (app, client, _) = await CreateTestApiAsync();
        try
        {
            var invalidReq = new SendCommandRequest(CommandLine: "");
            var response = await client.PostAsJsonAsync("/api/broadcast/command", invalidReq);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task BroadcastMessage_ValidMessage_Returns200WithResult()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        using var s1 = new SafeFrameStream(ms1);
        using var s2 = new SafeFrameStream(ms2);

        var session1 = new AgentSession("sess-m-1", s1, "127.0.0.1:4021", DateTimeOffset.UtcNow, agentId: "agent-m-1");
        var session2 = new AgentSession("sess-m-2", s2, "127.0.0.1:4022", DateTimeOffset.UtcNow, agentId: "agent-m-2");

        connectionManager.TryRegister(session1);
        connectionManager.TryRegister(session2);

        try
        {
            var msg = NetOpsMessage.Create(MessageType.Ping, "TEST_BC_MSG", requestId: "req-bc-99");
            var response = await client.PostAsJsonAsync("/api/broadcast/message", msg);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(doc.GetProperty("success").GetBoolean());
            Assert.Equal("PING", doc.GetProperty("messageType").GetString(), ignoreCase: true);
            Assert.Equal(2, doc.GetProperty("totalTargeted").GetInt32());
            Assert.Equal(2, doc.GetProperty("successCount").GetInt32());
            Assert.Equal(0, doc.GetProperty("failedCount").GetInt32());

            Assert.True(ms1.Length > 0);
            Assert.True(ms2.Length > 0);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetAgents_WithStatusFilter_And_GetAgentStatus_ReturnsCorrectRecords()
    {
        var (app, client, connectionManager) = await CreateTestApiAsync();
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        using var s1 = new SafeFrameStream(ms1);
        using var s2 = new SafeFrameStream(ms2);

        var session1 = new AgentSession("sess-live", s1, "127.0.0.1:4050", DateTimeOffset.UtcNow, agentId: "node-online");
        var session2 = new AgentSession("sess-dead", s2, "127.0.0.1:4051", DateTimeOffset.UtcNow, agentId: "node-offline");

        connectionManager.TryRegister(session1);
        connectionManager.TryRegister(session2);

        // Giả lập node-offline bị watchdog purge do timeout nhịp tim
        connectionManager.TryUnregister("sess-dead", out _, DisconnectReason.HeartbeatTimeout);

        try
        {
            // 1. GET /api/agents?status=offline
            var offlineResp = await client.GetAsync("/api/agents?status=offline");
            Assert.Equal(HttpStatusCode.OK, offlineResp.StatusCode);
            var offlineList = await offlineResp.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(offlineList);
            Assert.Single(offlineList);
            Assert.Equal("node-offline", offlineList[0].GetProperty("agentId").GetString());
            Assert.Equal("Offline", offlineList[0].GetProperty("status").GetString());
            Assert.Equal("HeartbeatTimeout", offlineList[0].GetProperty("disconnectReason").GetString());

            // 2. GET /api/agents?status=online
            var onlineResp = await client.GetAsync("/api/agents?status=online");
            Assert.Equal(HttpStatusCode.OK, onlineResp.StatusCode);
            var onlineList = await onlineResp.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(onlineList);
            Assert.Single(onlineList);
            Assert.Equal("node-online", onlineList[0].GetProperty("agentId").GetString());
            Assert.Equal("Online", onlineList[0].GetProperty("status").GetString());

            // 3. GET /api/agents/status/{agentId}
            var statusResp = await client.GetAsync("/api/agents/status/node-offline");
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
            var statusDoc = await statusResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("node-offline", statusDoc.GetProperty("agentId").GetString());
            Assert.Equal("Offline", statusDoc.GetProperty("status").GetString());
            Assert.Equal("HeartbeatTimeout", statusDoc.GetProperty("disconnectReason").GetString());

            // 4. GET /api/agents/status/{agentId} - Not Found
            var notFoundResp = await client.GetAsync("/api/agents/status/unknown-agent");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
