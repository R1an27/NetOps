using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Agent.Worker.Services;
using Master.Api.Endpoints;
using Master.Api.Options;
using Master.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp End-to-End kiểm chứng khả năng của TcpManagerServer trong việc
/// duy trì đồng thời >= 3 Agent kết nối socket TCP thật cùng lúc.
/// Kiểm chứng:
/// 1. Duy trì ổn định kết nối của 3 Agent (agent-alpha, agent-beta, agent-gamma) cùng lúc.
/// 2. Tự động nhận diện AgentId từ PING payload trên từng session.
/// 3. Định tuyến thông điệp và lệnh shell độc lập tới đúng Agent đích.
/// 4. Phát sóng đồng loạt (Broadcast) tới toàn bộ các Agent đang kết nối.
/// 5. Mở rộng kiểm chứng với 5 Agent kết nối đồng thời.
/// 6. Cô lập sự cố khi 1 Agent ngắt kết nối (Fault Isolation) và tự động phục hồi khi Reconnect.
/// 7. Tích hợp xác thực các endpoint Minimal API Quản Trị (GET /api/health, GET /api/agents, GET /api/agents/{sessionId}).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Master")]
public class MultiAgentIntegrationTests
{
    private static async Task WaitForActiveCountAsync(TcpManagerServer server, int expectedCount, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (server.ConnectionManager.ActiveCount != expectedCount && !timeoutCts.IsCancellationRequested)
        {
            await Task.Delay(25, timeoutCts.Token);
        }

        Assert.Equal(expectedCount, server.ConnectionManager.ActiveCount);
    }

    private static async Task<AgentSession> WaitForAgentSessionAsync(TcpManagerServer server, string agentId, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!timeoutCts.IsCancellationRequested)
        {
            var session = server.ConnectionManager.GetSessionByAgentId(agentId);
            if (session != null)
            {
                return session;
            }

            try
            {
                await Task.Delay(25, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var found = server.ConnectionManager.GetSessionByAgentId(agentId);
        Assert.NotNull(found);
        return found;
    }

    [Fact]
    public async Task Manager_MaintainsThreeConcurrentAgents_AllConnectedAndIdentifiedSimultaneously()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, "agent-beta", TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, "agent-gamma", TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        try
        {
            // 1. Chờ cả 3 Agent kết nối thành công và được nhận diện
            var sessionAlpha = await WaitForAgentSessionAsync(server, "agent-alpha", TimeSpan.FromSeconds(5));
            var sessionBeta = await WaitForAgentSessionAsync(server, "agent-beta", TimeSpan.FromSeconds(5));
            var sessionGamma = await WaitForAgentSessionAsync(server, "agent-gamma", TimeSpan.FromSeconds(5));

            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));

            // 2. Kiểm chứng trạng thái kết nối
            Assert.True(clientAlpha.IsConnected);
            Assert.True(clientBeta.IsConnected);
            Assert.True(clientGamma.IsConnected);

            Assert.Equal(3, server.ConnectionManager.ActiveCount);
            Assert.Equal("agent-alpha", sessionAlpha.AgentId);
            Assert.Equal("agent-beta", sessionBeta.AgentId);
            Assert.Equal("agent-gamma", sessionGamma.AgentId);

            // 3. Kiểm chứng các phiên kết nối là độc lập (SessionId khác nhau)
            var sessionIds = new HashSet<string> { sessionAlpha.SessionId, sessionBeta.SessionId, sessionGamma.SessionId };
            Assert.Equal(3, sessionIds.Count);
        }
        finally
        {
            ctsAlpha.Cancel();
            ctsBeta.Cancel();
            ctsGamma.Cancel();

            await Task.WhenAll(taskAlpha, taskBeta, taskGamma);
        }
    }

    [Fact]
    public async Task Manager_CanRouteCommandsIndependently_ToSpecificAgentAmongThree()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, "agent-beta", TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, "agent-gamma", TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        try
        {
            // Chờ cả 3 Agent sẵn sàng
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var sessionBeta = await WaitForAgentSessionAsync(server, "agent-beta", TimeSpan.FromSeconds(5));

            var betaCommandTcs = new TaskCompletionSource<CommandRequestPayload>();
            var otherCommands = new ConcurrentBag<string>();

            clientAlpha.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) otherCommands.Add($"alpha:{payload.CommandId}");
            };
            clientBeta.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) betaCommandTcs.TrySetResult(payload);
            };
            clientGamma.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) otherCommands.Add($"gamma:{payload.CommandId}");
            };

            // Manager chỉ gửi lệnh COMMAND tới agent-beta
            var cmdPayload = new CommandRequestPayload("cmd-target-beta-01", "uptime", timeoutSeconds: 15);
            var cmdMsg = NetOpsMessage.CreateRequest(MessageType.Command, cmdPayload, requestId: "req-cmd-beta");

            await sessionBeta.SendMessageAsync(cmdMsg);

            // Kiểm chứng chỉ agent-beta nhận được lệnh
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (timeoutCts.Token.Register(() => betaCommandTcs.TrySetCanceled()))
            {
                var receivedPayload = await betaCommandTcs.Task;
                Assert.NotNull(receivedPayload);
                Assert.Equal("cmd-target-beta-01", receivedPayload.CommandId);
                Assert.Equal("uptime", receivedPayload.CommandLine);
            }

            // Đợi một khoảng ngắn để xác nhận không có agent nào khác nhận nhầm lệnh
            await Task.Delay(100);
            Assert.Empty(otherCommands);
        }
        finally
        {
            ctsAlpha.Cancel();
            ctsBeta.Cancel();
            ctsGamma.Cancel();

            await Task.WhenAll(taskAlpha, taskBeta, taskGamma);
        }
    }

    [Fact]
    public async Task Manager_CanBroadcastMessage_ToAllThreeAgentsSimultaneously()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, "agent-beta", TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, "agent-gamma", TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        try
        {
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-alpha", TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-beta", TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-gamma", TimeSpan.FromSeconds(5));

            var alphaTcs = new TaskCompletionSource<CommandRequestPayload>();
            var betaTcs = new TaskCompletionSource<CommandRequestPayload>();
            var gammaTcs = new TaskCompletionSource<CommandRequestPayload>();

            clientAlpha.MessageControl.CommandReceived += (_, payload) => { if (payload != null) alphaTcs.TrySetResult(payload); };
            clientBeta.MessageControl.CommandReceived += (_, payload) => { if (payload != null) betaTcs.TrySetResult(payload); };
            clientGamma.MessageControl.CommandReceived += (_, payload) => { if (payload != null) gammaTcs.TrySetResult(payload); };

            // Server phát sóng đồng loạt lệnh tới toàn bộ 3 Agent
            var broadcastCmd = new CommandRequestPayload("broadcast-cmd-all", "sync-clocks", timeoutSeconds: 5);
            var broadcastMsg = NetOpsMessage.Create(MessageType.Command, broadcastCmd, requestId: "req-broadcast-01");

            await server.ConnectionManager.BroadcastAsync(broadcastMsg);

            // Kiểm chứng cả 3 Agent đều nhận được thông điệp phát sóng
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (timeoutCts.Token.Register(() =>
            {
                alphaTcs.TrySetCanceled();
                betaTcs.TrySetCanceled();
                gammaTcs.TrySetCanceled();
            }))
            {
                var results = await Task.WhenAll(alphaTcs.Task, betaTcs.Task, gammaTcs.Task);
                Assert.Equal(3, results.Length);

                foreach (var res in results)
                {
                    Assert.Equal("broadcast-cmd-all", res.CommandId);
                    Assert.Equal("sync-clocks", res.CommandLine);
                }
            }
        }
        finally
        {
            ctsAlpha.Cancel();
            ctsBeta.Cancel();
            ctsGamma.Cancel();

            await Task.WhenAll(taskAlpha, taskBeta, taskGamma);
        }
    }

    [Fact]
    public async Task Manager_MaintainsFiveConcurrentAgents_ScalesBeyondMinimumRequirement()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const int agentCount = 5;
        var ctsList = new List<CancellationTokenSource>();
        var clients = new List<AgentNetworkClient>();
        var tasks = new List<Task>();

        for (int i = 1; i <= agentCount; i++)
        {
            var cts = new CancellationTokenSource();
            ctsList.Add(cts);

            var client = new AgentNetworkClient(NullLogger.Instance);
            clients.Add(client);

            string agentId = $"agent-scale-{i:D2}";
            var task = client.RunAsync(serverHelper.Host, serverHelper.Port, agentId, TimeSpan.FromMilliseconds(200), cts.Token);
            tasks.Add(task);
        }

        try
        {
            // Chờ cả 5 Agent kết nối và được nhận diện
            await WaitForActiveCountAsync(server, agentCount, TimeSpan.FromSeconds(10));

            for (int i = 1; i <= agentCount; i++)
            {
                string agentId = $"agent-scale-{i:D2}";
                var session = await WaitForAgentSessionAsync(server, agentId, TimeSpan.FromSeconds(5));
                Assert.NotNull(session);
                Assert.Equal(agentId, session.AgentId);
            }

            Assert.Equal(agentCount, server.ConnectionManager.ActiveCount);

            foreach (var client in clients)
            {
                Assert.True(client.IsConnected);
            }
        }
        finally
        {
            foreach (var cts in ctsList)
            {
                cts.Cancel();
            }

            await Task.WhenAll(tasks);

            foreach (var cts in ctsList)
            {
                cts.Dispose();
            }
        }
    }

    [Fact]
    public async Task Manager_HandlesAgentDisconnectGracefully_AndIsolatesFault_WithoutImpactingOtherAgents()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, "agent-beta", TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, "agent-gamma", TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        CancellationTokenSource? ctsAlphaReconnect = null;
        Task? taskAlphaReconnect = null;

        try
        {
            // 1. Chờ cả 3 Agent kết nối thành công và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-alpha", TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-beta", TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, "agent-gamma", TimeSpan.FromSeconds(5));

            // 2. Ngắt kết nối agent-alpha chủ động
            ctsAlpha.Cancel();
            await taskAlpha; // Chờ agent-alpha ngắt xong hoàn toàn

            // 3. Manager phát hiện ngắt kết nối và tự động gỡ bỏ session của agent-alpha (ActiveCount giảm về 2)
            await WaitForActiveCountAsync(server, 2, TimeSpan.FromSeconds(5));
            Assert.Null(server.ConnectionManager.GetSessionByAgentId("agent-alpha"));

            // 4. Kiểm chứng 2 Agent còn lại (beta và gamma) vẫn kết nối và hoạt động bình thường
            Assert.True(clientBeta.IsConnected);
            Assert.True(clientGamma.IsConnected);

            var sessionBeta = server.ConnectionManager.GetSessionByAgentId("agent-beta");
            var sessionGamma = server.ConnectionManager.GetSessionByAgentId("agent-gamma");
            Assert.NotNull(sessionBeta);
            Assert.NotNull(sessionGamma);

            // Gửi lệnh độc lập tới agent-beta để xác nhận kết nối và định tuyến vẫn hoàn hảo
            var betaCommandTcs = new TaskCompletionSource<CommandRequestPayload>();
            clientBeta.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) betaCommandTcs.TrySetResult(payload);
            };

            var testCmd = new CommandRequestPayload("cmd-post-isolation", "ping 127.0.0.1", timeoutSeconds: 5);
            await sessionBeta.SendMessageAsync(NetOpsMessage.CreateRequest(MessageType.Command, testCmd, "req-post-iso"));

            using var cmdTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (cmdTimeoutCts.Token.Register(() => betaCommandTcs.TrySetCanceled()))
            {
                var cmdReceived = await betaCommandTcs.Task;
                Assert.Equal("cmd-post-isolation", cmdReceived.CommandId);
            }

            // 5. Kết nối lại agent-alpha (Reconnection) -> ActiveCount tự động phục hồi về 3
            ctsAlphaReconnect = new CancellationTokenSource();
            var clientAlphaReconnect = new AgentNetworkClient(NullLogger.Instance);
            taskAlphaReconnect = clientAlphaReconnect.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlphaReconnect.Token);

            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var sessionAlphaRecovered = await WaitForAgentSessionAsync(server, "agent-alpha", TimeSpan.FromSeconds(5));
            Assert.NotNull(sessionAlphaRecovered);
            Assert.Equal("agent-alpha", sessionAlphaRecovered.AgentId);
            Assert.True(clientAlphaReconnect.IsConnected);
        }
        finally
        {
            ctsAlpha.Cancel();
            ctsBeta.Cancel();
            ctsGamma.Cancel();
            ctsAlphaReconnect?.Cancel();

            var cleanupTasks = new List<Task> { taskAlpha, taskBeta, taskGamma };
            if (taskAlphaReconnect != null) cleanupTasks.Add(taskAlphaReconnect);
            await Task.WhenAll(cleanupTasks);

            ctsAlphaReconnect?.Dispose();
        }
    }

    [Fact]
    public async Task MasterApi_Endpoints_ReflectAccurateState_WithThreeConcurrentAgents()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, "agent-alpha", TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, "agent-beta", TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, "agent-gamma", TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        // Khởi động WebApplication in-memory lắng nghe trên loopback port ngẫu nhiên
        var webBuilder = WebApplication.CreateBuilder();
        webBuilder.Logging.ClearProviders();
        webBuilder.Services.AddSingleton<IConnectionManager>(server.ConnectionManager);
        webBuilder.Services.Configure<MasterServerOptions>(opt =>
        {
            opt.Port = serverHelper.Port;
            opt.WatchdogIntervalSeconds = 10;
            opt.SessionTimeoutSeconds = 45;
        });
        webBuilder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0));

        await using var webApp = webBuilder.Build();
        webApp.MapMasterApiEndpoints();
        await webApp.StartAsync();

        var webAddress = webApp.Urls.First();
        using var httpClient = new HttpClient { BaseAddress = new Uri(webAddress) };

        try
        {
            // 1. Chờ cả 3 Agent kết nối socket TCP thật và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var sessionAlpha = await WaitForAgentSessionAsync(server, "agent-alpha", TimeSpan.FromSeconds(5));
            var sessionBeta = await WaitForAgentSessionAsync(server, "agent-beta", TimeSpan.FromSeconds(5));
            var sessionGamma = await WaitForAgentSessionAsync(server, "agent-gamma", TimeSpan.FromSeconds(5));

            // 2. Xác thực GET /api/health
            var healthResp = await httpClient.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            var healthJson = await healthResp.Content.ReadAsStringAsync();
            using var healthDoc = JsonDocument.Parse(healthJson);
            var healthRoot = healthDoc.RootElement;

            Assert.Equal("Healthy", healthRoot.GetProperty("status").GetString());
            Assert.Equal(serverHelper.Port, healthRoot.GetProperty("tcpPort").GetInt32());
            Assert.Equal(3, healthRoot.GetProperty("activeConnections").GetInt32());
            Assert.Equal(10, healthRoot.GetProperty("watchdog").GetProperty("intervalSeconds").GetInt32());
            Assert.Equal(45, healthRoot.GetProperty("watchdog").GetProperty("timeoutSeconds").GetInt32());

            // 3. Xác thực GET /api/agents
            var agentsResp = await httpClient.GetAsync("/api/agents");
            Assert.Equal(HttpStatusCode.OK, agentsResp.StatusCode);
            var agentsJson = await agentsResp.Content.ReadAsStringAsync();
            using var agentsDoc = JsonDocument.Parse(agentsJson);
            var agentsArray = agentsDoc.RootElement;

            Assert.Equal(JsonValueKind.Array, agentsArray.ValueKind);
            Assert.Equal(3, agentsArray.GetArrayLength());

            var returnedAgentIds = new HashSet<string>();
            foreach (var agentElem in agentsArray.EnumerateArray())
            {
                var agentId = agentElem.GetProperty("agentId").GetString();
                Assert.NotNull(agentId);
                returnedAgentIds.Add(agentId);

                Assert.False(string.IsNullOrEmpty(agentElem.GetProperty("sessionId").GetString()));
                Assert.False(string.IsNullOrEmpty(agentElem.GetProperty("remoteEndPoint").GetString()));
                Assert.True(agentElem.TryGetProperty("connectedAt", out _));
                Assert.True(agentElem.TryGetProperty("lastSeenAt", out _));
                Assert.True(agentElem.GetProperty("idleSeconds").GetDouble() >= 0);
            }

            Assert.Contains("agent-alpha", returnedAgentIds);
            Assert.Contains("agent-beta", returnedAgentIds);
            Assert.Contains("agent-gamma", returnedAgentIds);

            // 4. Xác thực GET /api/agents/{sessionId} với session tồn tại
            var singleAgentResp = await httpClient.GetAsync($"/api/agents/{sessionAlpha.SessionId}");
            Assert.Equal(HttpStatusCode.OK, singleAgentResp.StatusCode);
            var singleAgentJson = await singleAgentResp.Content.ReadAsStringAsync();
            using var singleAgentDoc = JsonDocument.Parse(singleAgentJson);
            var singleRoot = singleAgentDoc.RootElement;

            Assert.Equal(sessionAlpha.SessionId, singleRoot.GetProperty("sessionId").GetString());
            Assert.Equal("agent-alpha", singleRoot.GetProperty("agentId").GetString());

            // 5. Xác thực GET /api/agents/{sessionId} với session không tồn tại -> 404 Not Found
            var notFoundResp = await httpClient.GetAsync("/api/agents/non-existent-session-id");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }
        finally
        {
            ctsAlpha.Cancel();
            ctsBeta.Cancel();
            ctsGamma.Cancel();

            await Task.WhenAll(taskAlpha, taskBeta, taskGamma);
            await webApp.StopAsync();
        }
    }
}

