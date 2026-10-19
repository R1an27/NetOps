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
using ServerManager.Shared.Contracts.Protocol;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp chuyên sâu kiểm chứng cơ chế phát sóng đồng loạt (Broadcast Messaging Engine)
/// từ máy chủ trung tâm Master Server (TcpManagerServer) tới toàn bộ các Agent đang trực tuyến.
/// Xác thực:
/// 1. Manager phát sóng thông điệp tới toàn bộ Agent online thành công; tất cả Agent đều nhận đủ và đúng dữ liệu.
/// 2. Khả năng cách ly lỗi mạng (Fault Isolation): 1 Agent bị lỗi stream/ngắt kết nối không làm gián đoạn các Agent khác.
/// 3. Phát sóng dồn dập (Burst Concurrency): Khung tin nhị phân không bị đan xen, tất cả Agent nhận đủ toàn bộ chuỗi gói tin.
/// 4. Edge Case: Phát sóng khi không có Agent nào online trả về BroadcastResult(0,0,0) an toàn, không sinh lỗi.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Master")]
public class ManagerBroadcastRoutingTests
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
    public async Task Manager_CanBroadcastCommand_ToAllThreeAgentsSimultaneously_AllReceiveMessage()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentAlpha = "agent-bc-alpha";
        const string agentBeta = "agent-bc-beta";
        const string agentGamma = "agent-bc-gamma";

        using var ctsAlpha = new CancellationTokenSource();
        using var ctsBeta = new CancellationTokenSource();
        using var ctsGamma = new CancellationTokenSource();

        var clientAlpha = new AgentNetworkClient(NullLogger.Instance);
        var clientBeta = new AgentNetworkClient(NullLogger.Instance);
        var clientGamma = new AgentNetworkClient(NullLogger.Instance);

        var taskAlpha = clientAlpha.RunAsync(serverHelper.Host, serverHelper.Port, agentAlpha, TimeSpan.FromMilliseconds(200), ctsAlpha.Token);
        var taskBeta = clientBeta.RunAsync(serverHelper.Host, serverHelper.Port, agentBeta, TimeSpan.FromMilliseconds(200), ctsBeta.Token);
        var taskGamma = clientGamma.RunAsync(serverHelper.Host, serverHelper.Port, agentGamma, TimeSpan.FromMilliseconds(200), ctsGamma.Token);

        try
        {
            // 1. Chờ cả 3 Agent kết nối socket TCP thật và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentAlpha, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentBeta, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentGamma, TimeSpan.FromSeconds(5));

            var alphaTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var betaTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gammaTcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientAlpha.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-broadcast-all-01")
                    alphaTcs.TrySetResult(payload);
            };
            clientBeta.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-broadcast-all-01")
                    betaTcs.TrySetResult(payload);
            };
            clientGamma.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-broadcast-all-01")
                    gammaTcs.TrySetResult(payload);
            };

            // 2. Manager phát sóng đồng loạt lệnh shell tới toàn bộ các Agent
            var broadcastCmd = new CommandRequestPayload("cmd-broadcast-all-01", "sync-configuration", timeoutSeconds: 15);
            var broadcastMsg = NetOpsMessage.CreateRequest(MessageType.Command, broadcastCmd, requestId: "req-broadcast-all-01");

            var result = await server.ConnectionManager.BroadcastAsync(broadcastMsg);

            // 3. Xác thực kết quả thống kê BroadcastResult
            Assert.Equal(3, result.TotalTargeted);
            Assert.Equal(3, result.SuccessCount);
            Assert.Equal(0, result.FailedCount);
            Assert.True(result.AllSucceeded);
            Assert.False(result.HasFailures);

            // 4. Xác thực cả 3 Agent nhận được lệnh với nội dung nguyên vẹn
            using var waitTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitTimeoutCts.Token.Register(() =>
            {
                alphaTcs.TrySetCanceled();
                betaTcs.TrySetCanceled();
                gammaTcs.TrySetCanceled();
            }))
            {
                var receivedPayloads = await Task.WhenAll(alphaTcs.Task, betaTcs.Task, gammaTcs.Task);
                Assert.Equal(3, receivedPayloads.Length);

                foreach (var cmd in receivedPayloads)
                {
                    Assert.Equal("cmd-broadcast-all-01", cmd.CommandId);
                    Assert.Equal("sync-configuration", cmd.CommandLine);
                    Assert.Equal(15, cmd.TimeoutSeconds);
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
    public async Task Manager_BroadcastWithDisconnectedAgent_IsolatesFault_OtherAgentsReceiveSuccessfully()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentAlive1 = "agent-alive-01";
        const string agentAlive2 = "agent-alive-02";

        using var ctsAlive1 = new CancellationTokenSource();
        using var ctsAlive2 = new CancellationTokenSource();

        var clientAlive1 = new AgentNetworkClient(NullLogger.Instance);
        var clientAlive2 = new AgentNetworkClient(NullLogger.Instance);

        var taskAlive1 = clientAlive1.RunAsync(serverHelper.Host, serverHelper.Port, agentAlive1, TimeSpan.FromMilliseconds(200), ctsAlive1.Token);
        var taskAlive2 = clientAlive2.RunAsync(serverHelper.Host, serverHelper.Port, agentAlive2, TimeSpan.FromMilliseconds(200), ctsAlive2.Token);

        // Tạo 1 session giả lập có stream bị đóng/lỗi mạng (Faulty Session)
        var faultyMs = new MemoryStream();
        var faultyStream = new SafeFrameStream(faultyMs);
        var sessionFaulty = new AgentSession("sess-faulty-03", faultyStream, "127.0.0.1:4099", DateTimeOffset.UtcNow, agentId: "agent-faulty-03");

        server.ConnectionManager.TryRegister(sessionFaulty);

        // Đóng stream để giả lập sự cố kết nối đứt đột ngột khi Master ghi dữ liệu
        faultyMs.Dispose();

        try
        {
            // 1. Chờ 2 Agent thật kết nối socket TCP
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentAlive1, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentAlive2, TimeSpan.FromSeconds(5));

            var alive1Tcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var alive2Tcs = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientAlive1.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-fault-isolation")
                    alive1Tcs.TrySetResult(payload);
            };
            clientAlive2.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-fault-isolation")
                    alive2Tcs.TrySetResult(payload);
            };

            // 2. Manager phát sóng tới toàn bộ 3 Agent (2 thật + 1 lỗi)
            var cmd = new CommandRequestPayload("cmd-fault-isolation", "health-check", timeoutSeconds: 10);
            var msg = NetOpsMessage.CreateRequest(MessageType.Command, cmd, requestId: "req-fault-iso");

            var result = await server.ConnectionManager.BroadcastAsync(msg);

            // 3. Xác thực khả năng cách ly lỗi mạng:
            // Tổng mục tiêu: 3, Thành công: 2, Thất bại: 1
            Assert.Equal(3, result.TotalTargeted);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.False(result.AllSucceeded);
            Assert.True(result.HasFailures);

            // 4. Xác thực 2 Agent lành lặn vẫn nhận được gói tin bình thường
            using var waitTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitTimeoutCts.Token.Register(() =>
            {
                alive1Tcs.TrySetCanceled();
                alive2Tcs.TrySetCanceled();
            }))
            {
                var p1 = await alive1Tcs.Task;
                var p2 = await alive2Tcs.Task;

                Assert.Equal("cmd-fault-isolation", p1.CommandId);
                Assert.Equal("cmd-fault-isolation", p2.CommandId);
            }
        }
        finally
        {
            ctsAlive1.Cancel();
            ctsAlive2.Cancel();

            await Task.WhenAll(taskAlive1, taskAlive2);
        }
    }

    [Fact]
    public async Task Manager_CanBroadcastBurst_MultipleConsecutiveMessages_AllAgentsReceiveAllMessagesInOrder()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agent1 = "agent-burst-01";
        const string agent2 = "agent-burst-02";
        const string agent3 = "agent-burst-03";

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var client1 = new AgentNetworkClient(NullLogger.Instance);
        var client2 = new AgentNetworkClient(NullLogger.Instance);
        var client3 = new AgentNetworkClient(NullLogger.Instance);

        var task1 = client1.RunAsync(serverHelper.Host, serverHelper.Port, agent1, TimeSpan.FromMilliseconds(200), cts1.Token);
        var task2 = client2.RunAsync(serverHelper.Host, serverHelper.Port, agent2, TimeSpan.FromMilliseconds(200), cts2.Token);
        var task3 = client3.RunAsync(serverHelper.Host, serverHelper.Port, agent3, TimeSpan.FromMilliseconds(200), cts3.Token);

        const int burstCount = 10;
        var received1 = new ConcurrentQueue<CommandRequestPayload>();
        var received2 = new ConcurrentQueue<CommandRequestPayload>();
        var received3 = new ConcurrentQueue<CommandRequestPayload>();

        client1.MessageControl.CommandReceived += (_, payload) =>
        {
            if (payload != null && payload.CommandId.StartsWith("cmd-burst-seq-"))
                received1.Enqueue(payload);
        };
        client2.MessageControl.CommandReceived += (_, payload) =>
        {
            if (payload != null && payload.CommandId.StartsWith("cmd-burst-seq-"))
                received2.Enqueue(payload);
        };
        client3.MessageControl.CommandReceived += (_, payload) =>
        {
            if (payload != null && payload.CommandId.StartsWith("cmd-burst-seq-"))
                received3.Enqueue(payload);
        };

        try
        {
            // 1. Chờ cả 3 Agent kết nối socket TCP
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent1, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent2, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent3, TimeSpan.FromSeconds(5));

            // 2. Phát sóng dồn dập 10 thông điệp liên tiếp
            for (int i = 0; i < burstCount; i++)
            {
                var payload = new CommandRequestPayload($"cmd-burst-seq-{i:D2}", $"echo step-{i}");
                var msg = NetOpsMessage.CreateRequest(MessageType.Command, payload, requestId: $"req-burst-{i:D2}");

                var result = await server.ConnectionManager.BroadcastAsync(msg);
                Assert.Equal(3, result.SuccessCount);
            }

            // 3. Chờ cả 3 Agent nhận đủ 10 thông điệp
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while ((received1.Count < burstCount || received2.Count < burstCount || received3.Count < burstCount)
                   && !waitCts.IsCancellationRequested)
            {
                await Task.Delay(25, waitCts.Token);
            }

            Assert.Equal(burstCount, received1.Count);
            Assert.Equal(burstCount, received2.Count);
            Assert.Equal(burstCount, received3.Count);

            // 4. Xác thực thứ tự tuần tự hóa đúng từ 00 đến 09
            var list1 = received1.ToArray();
            var list2 = received2.ToArray();
            var list3 = received3.ToArray();

            for (int i = 0; i < burstCount; i++)
            {
                string expectedCmdId = $"cmd-burst-seq-{i:D2}";
                Assert.Equal(expectedCmdId, list1[i].CommandId);
                Assert.Equal(expectedCmdId, list2[i].CommandId);
                Assert.Equal(expectedCmdId, list3[i].CommandId);
            }
        }
        finally
        {
            cts1.Cancel();
            cts2.Cancel();
            cts3.Cancel();

            await Task.WhenAll(task1, task2, task3);
        }
    }

    [Fact]
    public async Task Manager_BroadcastWithZeroAgents_ReturnsZeroResultSafely()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        Assert.Equal(0, server.ConnectionManager.ActiveCount);

        var pingMsg = NetOpsMessage.Create(MessageType.Ping, "ZERO_AGENTS_PING");
        var result = await server.ConnectionManager.BroadcastAsync(pingMsg);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalTargeted);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.False(result.AllSucceeded);
        Assert.False(result.HasFailures);

        // Server vẫn hoạt động bình thường
        Assert.Equal(0, server.ConnectionManager.ActiveCount);
    }

    [Fact]
    public async Task RestApi_PostBroadcastCommand_SendsBroadcastTcpMessageToAllOnlineAgentsSuccessfully()
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

        // 2. Kết nối 3 Agent thật qua socket TCP
        const string agent1 = "agent-rest-bc-01";
        const string agent2 = "agent-rest-bc-02";
        const string agent3 = "agent-rest-bc-03";

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var client1 = new AgentNetworkClient(NullLogger.Instance);
        var client2 = new AgentNetworkClient(NullLogger.Instance);
        var client3 = new AgentNetworkClient(NullLogger.Instance);

        var task1 = client1.RunAsync(serverHelper.Host, serverHelper.Port, agent1, TimeSpan.FromMilliseconds(200), cts1.Token);
        var task2 = client2.RunAsync(serverHelper.Host, serverHelper.Port, agent2, TimeSpan.FromMilliseconds(200), cts2.Token);
        var task3 = client3.RunAsync(serverHelper.Host, serverHelper.Port, agent3, TimeSpan.FromMilliseconds(200), cts3.Token);

        try
        {
            // Chờ cả 3 Agent kết nối socket TCP thật và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent1, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent2, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agent3, TimeSpan.FromSeconds(5));

            var tcs1 = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs2 = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs3 = new TaskCompletionSource<CommandRequestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

            client1.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-rest-bc-e2e-01") tcs1.TrySetResult(payload);
            };
            client2.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-rest-bc-e2e-01") tcs2.TrySetResult(payload);
            };
            client3.MessageControl.CommandReceived += (_, payload) =>
            {
                if (payload != null && payload.CommandId == "cmd-rest-bc-e2e-01") tcs3.TrySetResult(payload);
            };

            // 3. Client bên ngoài gọi HTTP POST /api/broadcast/command
            var broadcastReq = new SendCommandRequest(
                CommandLine: "uptime",
                CommandId: "cmd-rest-bc-e2e-01",
                TimeoutSeconds: 25,
                WorkingDir: "C:\\Temp");

            var response = await httpClient.PostAsJsonAsync("/api/broadcast/command", broadcastReq);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var respDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(respDoc.GetProperty("success").GetBoolean());
            Assert.Equal("cmd-rest-bc-e2e-01", respDoc.GetProperty("commandId").GetString());
            Assert.Equal("uptime", respDoc.GetProperty("commandLine").GetString());
            Assert.Equal(3, respDoc.GetProperty("totalTargeted").GetInt32());
            Assert.Equal(3, respDoc.GetProperty("successCount").GetInt32());
            Assert.Equal(0, respDoc.GetProperty("failedCount").GetInt32());

            // 4. Xác nhận toàn bộ 3 Agent đều nhận được lệnh qua socket TCP thật
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using (waitCts.Token.Register(() =>
            {
                tcs1.TrySetCanceled();
                tcs2.TrySetCanceled();
                tcs3.TrySetCanceled();
            }))
            {
                var payloads = await Task.WhenAll(tcs1.Task, tcs2.Task, tcs3.Task);
                Assert.Equal(3, payloads.Length);

                foreach (var cmd in payloads)
                {
                    Assert.Equal("cmd-rest-bc-e2e-01", cmd.CommandId);
                    Assert.Equal("uptime", cmd.CommandLine);
                    Assert.Equal(25, cmd.TimeoutSeconds);
                    Assert.Equal("C:\\Temp", cmd.WorkingDir);
                }
            }

            // 5. Ngắt kết nối 3 Agent và kiểm chứng phát sóng khi không có Agent nào online
            cts1.Cancel();
            cts2.Cancel();
            cts3.Cancel();
            await Task.WhenAll(task1, task2, task3);

            await WaitForActiveCountAsync(server, 0, TimeSpan.FromSeconds(5));

            var emptyResponse = await httpClient.PostAsJsonAsync("/api/broadcast/command", new SendCommandRequest(CommandLine: "echo empty"));
            Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);

            var emptyDoc = await emptyResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(emptyDoc.GetProperty("success").GetBoolean());
            Assert.Equal(0, emptyDoc.GetProperty("totalTargeted").GetInt32());
            Assert.Equal(0, emptyDoc.GetProperty("successCount").GetInt32());
            Assert.Equal(0, emptyDoc.GetProperty("failedCount").GetInt32());
        }
        finally
        {
            cts1.Cancel();
            cts2.Cancel();
            cts3.Cancel();

            await webApp.StopAsync();
        }
    }
}
