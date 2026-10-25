using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
/// Bộ kiểm thử tích hợp chuyên sâu kiểm chứng cơ chế định tuyến thông điệp Unicast
/// từ máy chủ trung tâm Master Server (TcpManagerServer) tới đích danh từng Agent.
/// Xác thực:
/// 1. Manager gửi Unicast tới 1 Agent mục tiêu thành công; các Agent khác tuyệt đối không nhận nhầm.
/// 2. Manager gửi đồng thời nhiều thông điệp Unicast tới nhiều Agent; mỗi Agent nhận đúng thông điệp của mình.
/// 3. Gửi Unicast tới Agent không tồn tại hoặc đã disconnect trả về false an toàn, không ảnh hưởng các kết nối khác.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Master")]
public class ManagerUnicastRoutingTests
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
    public async Task Manager_CanSendUnicastCommand_ToSingleAgent_WithoutOtherAgentsReceivingIt()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string targetAgent = "agent-target";
        const string peerAgent1 = "agent-peer-01";
        const string peerAgent2 = "agent-peer-02";

        using var ctsTarget = new CancellationTokenSource();
        using var ctsPeer1 = new CancellationTokenSource();
        using var ctsPeer2 = new CancellationTokenSource();

        var clientTarget = new AgentNetworkClient(NullLogger.Instance);
        var clientPeer1 = new AgentNetworkClient(NullLogger.Instance);
        var clientPeer2 = new AgentNetworkClient(NullLogger.Instance);

        var taskTarget = clientTarget.RunAsync(serverHelper.Host, serverHelper.Port, targetAgent, TimeSpan.FromMilliseconds(200), ctsTarget.Token);
        var taskPeer1 = clientPeer1.RunAsync(serverHelper.Host, serverHelper.Port, peerAgent1, TimeSpan.FromMilliseconds(200), ctsPeer1.Token);
        var taskPeer2 = clientPeer2.RunAsync(serverHelper.Host, serverHelper.Port, peerAgent2, TimeSpan.FromMilliseconds(200), ctsPeer2.Token);

        try
        {
            // 1. Chờ cả 3 Agent kết nối socket TCP thật và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var sessionTarget = await WaitForAgentSessionAsync(server, targetAgent, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, peerAgent1, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, peerAgent2, TimeSpan.FromSeconds(5));

            var targetTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var otherReceivedCommands = new ConcurrentBag<string>();

            clientTarget.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) targetTcs.TrySetResult(payload);
            };
            clientPeer1.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) otherReceivedCommands.Add($"{peerAgent1}:{payload.CommandId}");
            };
            clientPeer2.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) otherReceivedCommands.Add($"{peerAgent2}:{payload.CommandId}");
            };

            // 2. Manager gửi lệnh Unicast tới agent-target qua SendUnicastAsync(agentId, message)
            var targetPayload = new CommandRequestPayload("cmd-target-99", "whoami", timeoutSeconds: 10);
            var targetMsg = NetOpsMessage.CreateRequest(MessageType.Command, targetPayload, requestId: "req-unicast-target");

            bool sentResult = await server.ConnectionManager.SendUnicastAsync(targetAgent, targetMsg);
            Assert.True(sentResult, "SendUnicastAsync phải trả về true khi Agent trực tuyến");

            // 3. Xác thực agent-target nhận được đúng lệnh
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitCts.Token.Register(() => targetTcs.TrySetCanceled()))
            {
                var received = await targetTcs.Task;
                Assert.NotNull(received);
                Assert.Equal("cmd-target-99", received.CommandId);
                Assert.Equal("whoami", received.CommandLine);
            }

            // 4. Kiểm tra thêm gửi Unicast qua SendUnicastBySessionIdAsync(sessionId, message)
            var sessionTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientTarget.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-target-session-88")
                {
                    sessionTcs.TrySetResult(payload);
                }
            };

            var sessionPayload = new CommandRequestPayload("cmd-target-session-88", "hostname", timeoutSeconds: 10);
            var sessionMsg = NetOpsMessage.CreateRequest(MessageType.Command, sessionPayload, requestId: "req-session-target");

            bool sentBySession = await server.ConnectionManager.SendUnicastBySessionIdAsync(sessionTarget.SessionId, sessionMsg);
            Assert.True(sentBySession);

            using var waitSessionCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitSessionCts.Token.Register(() => sessionTcs.TrySetCanceled()))
            {
                var receivedSessionCmd = await sessionTcs.Task;
                Assert.Equal("cmd-target-session-88", receivedSessionCmd.CommandId);
            }

            // 5. Đợi 100ms và khẳng định 2 Agent còn lại hoàn toàn không nhận được bất kỳ lệnh nào (Zero Cross-Talk)
            await Task.Delay(100);
            Assert.Empty(otherReceivedCommands);
        }
        finally
        {
            ctsTarget.Cancel();
            ctsPeer1.Cancel();
            ctsPeer2.Cancel();

            await Task.WhenAll(taskTarget, taskPeer1, taskPeer2);
        }
    }

    [Fact]
    public async Task Manager_CanSendConcurrentUnicastMessages_ToMultipleAgents_EachReceivesItsOwnMessageStrictly()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentA = "agent-multi-a";
        const string agentB = "agent-multi-b";
        const string agentC = "agent-multi-c";

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();
        using var ctsC = new CancellationTokenSource();

        var clientA = new AgentNetworkClient(NullLogger.Instance);
        var clientB = new AgentNetworkClient(NullLogger.Instance);
        var clientC = new AgentNetworkClient(NullLogger.Instance);

        var taskA = clientA.RunAsync(serverHelper.Host, serverHelper.Port, agentA, TimeSpan.FromMilliseconds(200), ctsA.Token);
        var taskB = clientB.RunAsync(serverHelper.Host, serverHelper.Port, agentB, TimeSpan.FromMilliseconds(200), ctsB.Token);
        var taskC = clientC.RunAsync(serverHelper.Host, serverHelper.Port, agentC, TimeSpan.FromMilliseconds(200), ctsC.Token);

        try
        {
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentA, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentB, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentC, TimeSpan.FromSeconds(5));

            var tcsA = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcsB = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcsC = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

            var bagA = new ConcurrentBag<string>();
            var bagB = new ConcurrentBag<string>();
            var bagC = new ConcurrentBag<string>();

            clientA.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null)
                {
                    bagA.Add(payload.CommandId);
                    tcsA.TrySetResult(payload);
                }
            };
            clientB.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null)
                {
                    bagB.Add(payload.CommandId);
                    tcsB.TrySetResult(payload);
                }
            };
            clientC.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null)
                {
                    bagC.Add(payload.CommandId);
                    tcsC.TrySetResult(payload);
                }
            };

            // Chuẩn bị 3 thông điệp Unicast mang token độc nhất
            var msgA = NetOpsMessage.CreateRequest(MessageType.Command, new CommandRequestPayload("cmd-unique-A", "tasklist", 10), "req-A");
            var msgB = NetOpsMessage.CreateRequest(MessageType.Command, new CommandRequestPayload("cmd-unique-B", "netstat", 10), "req-B");
            var msgC = NetOpsMessage.CreateRequest(MessageType.Command, new CommandRequestPayload("cmd-unique-C", "systeminfo", 10), "req-C");

            // Phát đồng thời cả 3 thông điệp Unicast qua ConnectionManager
            var sendTasks = new[]
            {
                server.ConnectionManager.SendUnicastAsync(agentA, msgA),
                server.ConnectionManager.SendUnicastAsync(agentB, msgB),
                server.ConnectionManager.SendUnicastAsync(agentC, msgC)
            };

            var sendResults = await Task.WhenAll(sendTasks);
            Assert.All(sendResults, Assert.True);

            // Chờ nhận kết quả
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitCts.Token.Register(() =>
            {
                tcsA.TrySetCanceled();
                tcsB.TrySetCanceled();
                tcsC.TrySetCanceled();
            }))
            {
                await Task.WhenAll(tcsA.Task, tcsB.Task, tcsC.Task);
            }

            await Task.Delay(100);

            // Xác thực đối soát chéo tuyệt đối
            Assert.Single(bagA);
            Assert.Contains("cmd-unique-A", bagA);
            Assert.DoesNotContain("cmd-unique-B", bagA);
            Assert.DoesNotContain("cmd-unique-C", bagA);

            Assert.Single(bagB);
            Assert.Contains("cmd-unique-B", bagB);
            Assert.DoesNotContain("cmd-unique-A", bagB);
            Assert.DoesNotContain("cmd-unique-C", bagB);

            Assert.Single(bagC);
            Assert.Contains("cmd-unique-C", bagC);
            Assert.DoesNotContain("cmd-unique-A", bagC);
            Assert.DoesNotContain("cmd-unique-B", bagC);
        }
        finally
        {
            ctsA.Cancel();
            ctsB.Cancel();
            ctsC.Cancel();

            await Task.WhenAll(taskA, taskB, taskC);
        }
    }

    [Fact]
    public async Task Manager_SendUnicastToOfflineOrNonExistentAgent_ReturnsFalseSafely_WithoutImpactingConnectedPeers()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string activeAgent = "agent-active";
        const string willDisconnectAgent = "agent-temp-disconnect";

        using var ctsActive = new CancellationTokenSource();
        using var ctsTemp = new CancellationTokenSource();

        var clientActive = new AgentNetworkClient(NullLogger.Instance);
        var clientTemp = new AgentNetworkClient(NullLogger.Instance);

        var taskActive = clientActive.RunAsync(serverHelper.Host, serverHelper.Port, activeAgent, TimeSpan.FromMilliseconds(200), ctsActive.Token);
        var taskTemp = clientTemp.RunAsync(serverHelper.Host, serverHelper.Port, willDisconnectAgent, TimeSpan.FromMilliseconds(200), ctsTemp.Token);

        try
        {
            // 1. Chờ cả 2 Agent kết nối
            await WaitForActiveCountAsync(server, 2, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, activeAgent, TimeSpan.FromSeconds(5));
            var sessionTemp = await WaitForAgentSessionAsync(server, willDisconnectAgent, TimeSpan.FromSeconds(5));

            // 2. Ngắt kết nối Agent tạm thời
            ctsTemp.Cancel();
            await taskTemp;

            // Chờ server gỡ bỏ session của Agent bị ngắt
            await WaitForActiveCountAsync(server, 1, TimeSpan.FromSeconds(5));

            var dummyMsg = NetOpsMessage.Create(MessageType.Ping, "TEST_OFFLINE");

            // 3. Gửi Unicast tới Agent vừa ngắt kết nối -> trả về false an toàn
            bool sentDisconnected = await server.ConnectionManager.SendUnicastAsync(willDisconnectAgent, dummyMsg);
            Assert.False(sentDisconnected);

            // Gửi theo SessionId cũ -> trả về false an toàn
            bool sentDisconnectedSession = await server.ConnectionManager.SendUnicastBySessionIdAsync(sessionTemp.SessionId, dummyMsg);
            Assert.False(sentDisconnectedSession);

            // 4. Gửi Unicast tới Agent chưa từng tồn tại -> trả về false an toàn
            bool sentGhost = await server.ConnectionManager.SendUnicastAsync("agent-never-existed-ghost", dummyMsg);
            Assert.False(sentGhost);

            // 5. Xác thực Agent đang active vẫn nhận Unicast hoàn toàn bình thường
            var activeTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientActive.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) activeTcs.TrySetResult(payload);
            };

            var activeCmd = NetOpsMessage.CreateRequest(MessageType.Command, new CommandRequestPayload("cmd-active-still-works", "echo OK", 10));
            bool sentActive = await server.ConnectionManager.SendUnicastAsync(activeAgent, activeCmd);
            Assert.True(sentActive);

            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitCts.Token.Register(() => activeTcs.TrySetCanceled()))
            {
                var received = await activeTcs.Task;
                Assert.Equal("cmd-active-still-works", received.CommandId);
            }
        }
        finally
        {
            ctsActive.Cancel();
            ctsTemp.Cancel();

            await Task.WhenAll(taskActive, taskTemp);
        }
    }

    [Fact]
    public async Task RestApi_PostCommand_SendsUnicastTcpMessageToAgentSuccessfully()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        // 1. Khởi chạy song song Web API Kestrel in-memory trên Loopback cổng 0 ngẫu nhiên
        var webBuilder = WebApplication.CreateBuilder();
        webBuilder.Logging.ClearProviders();
        webBuilder.Services.AddSingleton<IConnectionManager>(server.ConnectionManager);
        webBuilder.Services.Configure<MasterServerOptions>(opt =>
        {
            opt.Port = serverHelper.Port;
            opt.WatchdogIntervalSeconds = 5;
            opt.SessionTimeoutSeconds = 15;
        });
        webBuilder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0));

        await using var webApp = webBuilder.Build();
        webApp.MapMasterApiEndpoints();
        await webApp.StartAsync();

        using var httpClient = new HttpClient { BaseAddress = new Uri(webApp.Urls.First()) };

        // 2. Kết nối 2 Agent qua TCP Socket thật
        const string targetAgent = "agent-web-target";
        const string peerAgent = "agent-web-peer";

        using var ctsTarget = new CancellationTokenSource();
        using var ctsPeer = new CancellationTokenSource();

        var clientTarget = new AgentNetworkClient(NullLogger.Instance);
        var clientPeer = new AgentNetworkClient(NullLogger.Instance);

        var taskTarget = clientTarget.RunAsync(serverHelper.Host, serverHelper.Port, targetAgent, TimeSpan.FromMilliseconds(200), ctsTarget.Token);
        var taskPeer = clientPeer.RunAsync(serverHelper.Host, serverHelper.Port, peerAgent, TimeSpan.FromMilliseconds(200), ctsPeer.Token);

        try
        {
            // Chờ cả 2 Agent kết nối và nhận diện trên Master Server
            await WaitForActiveCountAsync(server, 2, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, targetAgent, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, peerAgent, TimeSpan.FromSeconds(5));

            var targetCmdTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerReceivedCommands = new ConcurrentBag<string>();

            clientTarget.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) targetCmdTcs.TrySetResult(payload);
            };
            clientPeer.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null) peerReceivedCommands.Add(payload.CommandId);
            };

            // 3. Client bên ngoài gọi HTTP POST /api/agents/{targetAgent}/command
            var commandReq = new SendCommandRequest(
                CommandLine: "whoami /groups",
                CommandId: "cmd-rest-e2e-01",
                TimeoutSeconds: 20,
                WorkingDir: "C:\\Temp");

            var response = await httpClient.PostAsJsonAsync($"/api/agents/{targetAgent}/command", commandReq);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var respDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(respDoc.GetProperty("success").GetBoolean());
            Assert.Equal(targetAgent, respDoc.GetProperty("agentId").GetString());
            Assert.Equal("cmd-rest-e2e-01", respDoc.GetProperty("commandId").GetString());
            Assert.Equal("whoami /groups", respDoc.GetProperty("commandLine").GetString());

            // 4. Xác nhận Agent targetAgent nhận được lệnh qua socket TCP thật với đúng payload
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitCts.Token.Register(() => targetCmdTcs.TrySetCanceled()))
            {
                var receivedPayload = await targetCmdTcs.Task;
                Assert.NotNull(receivedPayload);
                Assert.Equal("cmd-rest-e2e-01", receivedPayload.CommandId);
                Assert.Equal("whoami /groups", receivedPayload.CommandLine);
                Assert.Equal(20, receivedPayload.TimeoutSeconds);
                Assert.Equal("C:\\Temp", receivedPayload.WorkingDir);
            }

            // 5. Đợi 100ms và khẳng định Agent peerAgent không nhận được bất kỳ lệnh nào
            await Task.Delay(100);
            Assert.Empty(peerReceivedCommands);

            // 6. Gửi lệnh tới Agent không tồn tại qua REST API -> HTTP 404 Not Found
            var ghostReq = new SendCommandRequest(CommandLine: "dir");
            var ghostResp = await httpClient.PostAsJsonAsync("/api/agents/agent-ghost-unknown/command", ghostReq);
            Assert.Equal(HttpStatusCode.NotFound, ghostResp.StatusCode);

            // 7. Gửi thông điệp tùy biến qua POST /api/agents/{targetAgent}/message
            var targetPingTcs = new TaskCompletionSource<NetOpsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientTarget.MessageControl.PongReceived += (msg, _) =>
            {
                if (msg != null && msg.RequestId == "req-rest-ping-e2e")
                {
                    targetPingTcs.TrySetResult(msg);
                }
            };

            var pingMsg = NetOpsMessage.Create(MessageType.Ping, "REST_PING_E2E", requestId: "req-rest-ping-e2e");
            var postMsgResp = await httpClient.PostAsJsonAsync($"/api/agents/{targetAgent}/message", pingMsg);
            Assert.Equal(HttpStatusCode.OK, postMsgResp.StatusCode);
        }
        finally
        {
            ctsTarget.Cancel();
            ctsPeer.Cancel();

            await Task.WhenAll(taskTarget, taskPeer);
            await webApp.StopAsync();
        }
    }
}
