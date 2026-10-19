using System.Collections.Concurrent;
using Agent.Worker.Services;
using Master.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NetOps.Tests.TestHelpers;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;
using Xunit;

namespace NetOps.Tests;

/// <summary>
/// Bộ kiểm thử tích hợp chuyên sâu kiểm chứng khả năng cô lập thông điệp giữa các Agent
/// (Message Isolation & Anti-Cross-Talk Test Suite).
/// Đảm bảo khi nhiều Agent hoạt động đồng thời:
/// 1. Request-Response correlation (Ping/Pong, Heartbeat/Ack) được bảo toàn tuyệt đối theo từng session.
/// 2. Không có bất kỳ gói tin nào của Agent A bị gửi nhầm sang Agent B (Zero Data Bleeding / Cross-Talk).
/// 3. Master Server phân loại và định tuyến thông điệp chính xác theo ngữ cảnh phiên AgentSession độc lập.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Master")]
public class AgentMessageIsolationTests
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
    public async Task ConcurrentPingMetrics_MaintainsStrictCorrelationAndPayloadIsolation()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentId1 = "agent-iso-01";
        const string agentId2 = "agent-iso-02";
        const string agentId3 = "agent-iso-03";

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var client1 = new AgentNetworkClient(NullLogger.Instance);
        var client2 = new AgentNetworkClient(NullLogger.Instance);
        var client3 = new AgentNetworkClient(NullLogger.Instance);

        var task1 = client1.RunAsync(serverHelper.Host, serverHelper.Port, agentId1, TimeSpan.FromMilliseconds(500), cts1.Token);
        var task2 = client2.RunAsync(serverHelper.Host, serverHelper.Port, agentId2, TimeSpan.FromMilliseconds(500), cts2.Token);
        var task3 = client3.RunAsync(serverHelper.Host, serverHelper.Port, agentId3, TimeSpan.FromMilliseconds(500), cts3.Token);

        try
        {
            // 1. Chờ cả 3 Agent kết nối socket TCP thật và được nhận diện
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentId1, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentId2, TimeSpan.FromSeconds(5));
            await WaitForAgentSessionAsync(server, agentId3, TimeSpan.FromSeconds(5));

            // 2. Thiết lập bộ thu thập phản hồi độc lập cho từng Agent
            var pongsAgent1 = new ConcurrentBag<NetOpsMessage>();
            var pongsAgent2 = new ConcurrentBag<NetOpsMessage>();
            var pongsAgent3 = new ConcurrentBag<NetOpsMessage>();

            var hbAcksAgent1 = new ConcurrentBag<NetOpsMessage>();
            var hbAcksAgent2 = new ConcurrentBag<NetOpsMessage>();
            var hbAcksAgent3 = new ConcurrentBag<NetOpsMessage>();

            client1.MessageControl.PongReceived += (msg, _) => pongsAgent1.Add(msg);
            client2.MessageControl.PongReceived += (msg, _) => pongsAgent2.Add(msg);
            client3.MessageControl.PongReceived += (msg, _) => pongsAgent3.Add(msg);

            client1.MessageControl.HeartbeatAckReceived += (msg, _) => hbAcksAgent1.Add(msg);
            client2.MessageControl.HeartbeatAckReceived += (msg, _) => hbAcksAgent2.Add(msg);
            client3.MessageControl.HeartbeatAckReceived += (msg, _) => hbAcksAgent3.Add(msg);

            // Thiết lập bộ theo dõi Metrics trên Master Server để kiểm tra Session Isolation
            var metricsAuditLog = new ConcurrentBag<(string SessionAgentId, MetricsPayload Metrics)>();
            server.MessageControl.MetricsReceived += (session, metrics) =>
            {
                metricsAuditLog.Add((session.AgentId ?? "Unknown", metrics));
            };

            const int messagesPerBatch = 20;

            // 3. Khởi chạy 3 luồng tác vụ độc lập gửi liên tục PING, HEARTBEAT và METRICS
            async Task SendBurstAsync(AgentNetworkClient client, string agentId)
            {
                for (int i = 1; i <= messagesPerBatch; i++)
                {
                    // A. Gửi PING kèm RequestId và token riêng
                    var pingMsg = new NetOpsMessage
                    {
                        MessageType = MessageType.Ping,
                        MessageId = $"msg-ping-{agentId}-{i:D3}",
                        RequestId = $"req-ping-{agentId}-{i:D3}",
                        Payload = $"PING_PAYLOAD_{agentId}_{i}"
                    };
                    await client.SendMessageAsync(pingMsg);

                    // B. Gửi HEARTBEAT kèm RequestId và HeartbeatPayload
                    var hbPayload = new HeartbeatPayload(agentId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    var hbMsg = new NetOpsMessage
                    {
                        MessageType = MessageType.Heartbeat,
                        MessageId = $"msg-hb-{agentId}-{i:D3}",
                        RequestId = $"req-hb-{agentId}-{i:D3}"
                    };
                    hbMsg.SetPayload(hbPayload);
                    await client.SendMessageAsync(hbMsg);

                    // C. Gửi METRICS
                    var metricsPayload = new MetricsPayload
                    {
                        AgentId = agentId,
                        CpuUsagePercent = 10.0 + i,
                        MemoryUsedMb = 256 + i,
                        MemoryTotalMb = 1024,
                        UptimeSeconds = 1000 + i
                    };
                    var metricsMsg = new NetOpsMessage
                    {
                        MessageType = MessageType.Metrics,
                        MessageId = $"msg-metrics-{agentId}-{i:D3}",
                        RequestId = $"req-metrics-{agentId}-{i:D3}"
                    };
                    metricsMsg.SetPayload(metricsPayload);
                    await client.SendMessageAsync(metricsMsg);

                    // Khoảng dừng siêu ngắn để luồng mạng xen kẽ ngẫu nhiên
                    await Task.Delay(5);
                }
            }

            // Gửi đồng thời từ cả 3 Agent (Tổng cộng 60 Pings, 60 Heartbeats, 60 Metrics)
            await Task.WhenAll(
                SendBurstAsync(client1, agentId1),
                SendBurstAsync(client2, agentId2),
                SendBurstAsync(client3, agentId3)
            );

            // 4. Chờ toàn bộ các phản hồi PONG và HEARTBEAT_ACK được xử lý xong
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while ((pongsAgent1.Count < messagesPerBatch ||
                    pongsAgent2.Count < messagesPerBatch ||
                    pongsAgent3.Count < messagesPerBatch ||
                    hbAcksAgent1.Count < messagesPerBatch ||
                    hbAcksAgent2.Count < messagesPerBatch ||
                    hbAcksAgent3.Count < messagesPerBatch ||
                    metricsAuditLog.Count < (messagesPerBatch * 3)) &&
                   !waitCts.IsCancellationRequested)
            {
                await Task.Delay(25, waitCts.Token);
            }

            // 5. ĐỐI SOÁT CHÉO TOÀN VẸN (CROSS-AUDITING VALIDATION)

            // Kiểm tra số lượng phản hồi
            Assert.True(pongsAgent1.Count >= messagesPerBatch, $"Agent 1 expected >={messagesPerBatch} Pongs, but got {pongsAgent1.Count}");
            Assert.True(pongsAgent2.Count >= messagesPerBatch, $"Agent 2 expected >={messagesPerBatch} Pongs, but got {pongsAgent2.Count}");
            Assert.True(pongsAgent3.Count >= messagesPerBatch, $"Agent 3 expected >={messagesPerBatch} Pongs, but got {pongsAgent3.Count}");

            Assert.True(hbAcksAgent1.Count >= messagesPerBatch, $"Agent 1 expected >={messagesPerBatch} HbAcks, but got {hbAcksAgent1.Count}");
            Assert.True(hbAcksAgent2.Count >= messagesPerBatch, $"Agent 2 expected >={messagesPerBatch} HbAcks, but got {hbAcksAgent2.Count}");
            Assert.True(hbAcksAgent3.Count >= messagesPerBatch, $"Agent 3 expected >={messagesPerBatch} HbAcks, but got {hbAcksAgent3.Count}");

            // Hàm kiểm tra đối soát từng danh sách thông điệp
            static void AuditIsolation(IEnumerable<NetOpsMessage> messages, string targetAgentId, string otherAgentId1, string otherAgentId2, string messagePrefix)
            {
                var targetCount = 0;
                foreach (var msg in messages)
                {
                    // RequestId và InReplyTo phải gắn với targetAgentId
                    if (msg.RequestId != null && msg.RequestId.StartsWith($"req-{messagePrefix}-{targetAgentId}-", StringComparison.Ordinal))
                    {
                        targetCount++;
                        Assert.StartsWith($"msg-{messagePrefix}-{targetAgentId}-", msg.InReplyTo);

                        // Tuyệt đối không chứa ID của các Agent khác
                        Assert.DoesNotContain(otherAgentId1, msg.RequestId);
                        Assert.DoesNotContain(otherAgentId2, msg.RequestId);
                    }
                    else if (msg.RequestId != null && (msg.RequestId.Contains(otherAgentId1) || msg.RequestId.Contains(otherAgentId2)))
                    {
                        // PHÁT HIỆN CROSS-TALK / DATA BLEEDING NẾU XẢY RA
                        Assert.Fail($"[DATA BLEEDING DETECTED] Agent '{targetAgentId}' received message belonging to '{msg.RequestId}'!");
                    }
                }

                Assert.True(targetCount >= messagesPerBatch, $"Target count for {targetAgentId} was {targetCount}, expected at least {messagesPerBatch}");
            }

            // Xác thực cô lập PONG
            AuditIsolation(pongsAgent1, agentId1, agentId2, agentId3, "ping");
            AuditIsolation(pongsAgent2, agentId2, agentId1, agentId3, "ping");
            AuditIsolation(pongsAgent3, agentId3, agentId1, agentId2, "ping");

            // Xác thực cô lập HEARTBEAT_ACK
            AuditIsolation(hbAcksAgent1, agentId1, agentId2, agentId3, "hb");
            AuditIsolation(hbAcksAgent2, agentId2, agentId1, agentId3, "hb");
            AuditIsolation(hbAcksAgent3, agentId3, agentId1, agentId2, "hb");

            // Xác thực Session Isolation phía Master đối với METRICS
            Assert.True(metricsAuditLog.Count >= (messagesPerBatch * 3));
            foreach (var log in metricsAuditLog)
            {
                // SessionId trên Master phải phân loại đúng AgentId khớp với Payload bên trong
                Assert.Equal(log.SessionAgentId, log.Metrics.AgentId);
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
    public async Task ConcurrentCommands_StreamingOutputChunks_RemainStrictlyIsolatedAcrossAgents()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentId1 = "agent-cmd-01";
        const string agentId2 = "agent-cmd-02";
        const string agentId3 = "agent-cmd-03";

        const string cmdId1 = "cmd-01-uname";
        const string cmdId2 = "cmd-02-meminfo";
        const string cmdId3 = "cmd-03-diskspace";

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var client1 = new AgentNetworkClient(NullLogger.Instance);
        var client2 = new AgentNetworkClient(NullLogger.Instance);
        var client3 = new AgentNetworkClient(NullLogger.Instance);

        var task1 = client1.RunAsync(serverHelper.Host, serverHelper.Port, agentId1, TimeSpan.FromMilliseconds(500), cts1.Token);
        var task2 = client2.RunAsync(serverHelper.Host, serverHelper.Port, agentId2, TimeSpan.FromMilliseconds(500), cts2.Token);
        var task3 = client3.RunAsync(serverHelper.Host, serverHelper.Port, agentId3, TimeSpan.FromMilliseconds(500), cts3.Token);

        try
        {
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var session1 = await WaitForAgentSessionAsync(server, agentId1, TimeSpan.FromSeconds(5));
            var session2 = await WaitForAgentSessionAsync(server, agentId2, TimeSpan.FromSeconds(5));
            var session3 = await WaitForAgentSessionAsync(server, agentId3, TimeSpan.FromSeconds(5));

            const int chunksPerCommand = 10;

            // Thiết lập Agent giả lập xử lý lệnh: khi nhận Command, stream 10 chunks và gửi Done
            void SetupAgentCommandExecution(AgentNetworkClient client, string agentId)
            {
                client.MessageControl.CommandReceived += async (reqMsg, cmdReq) =>
                {
                    // Stream 10 chunks bất đồng bộ
                    for (int i = 1; i <= chunksPerCommand; i++)
                    {
                        var chunkText = $"[OUT][{agentId}][chunk-{i:D2}] execution step {i} for command {cmdReq.CommandId}\n";
                        var chunkPayload = new CommandOutputPayload(cmdReq.CommandId, chunkText, isError: false);
                        var chunkMsg = NetOpsMessage.Create(MessageType.CommandOutput, chunkPayload, requestId: reqMsg.RequestId);
                        await client.SendMessageAsync(chunkMsg);

                        await Task.Delay(5); // mô phỏng độ trễ sinh kết quả
                    }

                    // Gửi thông báo hoàn tất CommandDone
                    var donePayload = new CommandDonePayload(cmdReq.CommandId, exitCode: 0, totalExecutionTimeMs: 150);
                    var doneMsg = NetOpsMessage.Create(MessageType.CommandDone, donePayload, requestId: reqMsg.RequestId);
                    await client.SendMessageAsync(doneMsg);
                };
            }

            SetupAgentCommandExecution(client1, agentId1);
            SetupAgentCommandExecution(client2, agentId2);
            SetupAgentCommandExecution(client3, agentId3);

            // Phía Master Server: Thu thập chunks và done events
            var chunksByAgent = new ConcurrentDictionary<string, ConcurrentBag<CommandOutputPayload>>();
            var doneByAgent = new ConcurrentDictionary<string, CommandDonePayload>();

            chunksByAgent[agentId1] = new ConcurrentBag<CommandOutputPayload>();
            chunksByAgent[agentId2] = new ConcurrentBag<CommandOutputPayload>();
            chunksByAgent[agentId3] = new ConcurrentBag<CommandOutputPayload>();

            server.MessageControl.CommandOutputReceived += (session, output) =>
            {
                if (!string.IsNullOrEmpty(session.AgentId) && chunksByAgent.TryGetValue(session.AgentId, out var bag))
                {
                    bag.Add(output);
                }
            };

            server.MessageControl.CommandDoneReceived += (session, done) =>
            {
                if (!string.IsNullOrEmpty(session.AgentId))
                {
                    doneByAgent[session.AgentId] = done;
                }
            };

            // Master phát đồng thời 3 lệnh tới 3 Agent
            var cmdReq1 = new CommandRequestPayload(cmdId1, "uname -a", timeoutSeconds: 15);
            var cmdReq2 = new CommandRequestPayload(cmdId2, "cat /proc/meminfo", timeoutSeconds: 15);
            var cmdReq3 = new CommandRequestPayload(cmdId3, "df -h", timeoutSeconds: 15);

            await Task.WhenAll(
                session1.SendMessageAsync(NetOpsMessage.CreateRequest(MessageType.Command, cmdReq1, $"req-{cmdId1}")).AsTask(),
                session2.SendMessageAsync(NetOpsMessage.CreateRequest(MessageType.Command, cmdReq2, $"req-{cmdId2}")).AsTask(),
                session3.SendMessageAsync(NetOpsMessage.CreateRequest(MessageType.Command, cmdReq3, $"req-{cmdId3}")).AsTask()
            );

            // Chờ nhận đủ output và done events cho cả 3 Agent với timeout 10 giây
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((chunksByAgent[agentId1].Count < chunksPerCommand ||
                    chunksByAgent[agentId2].Count < chunksPerCommand ||
                    chunksByAgent[agentId3].Count < chunksPerCommand ||
                    !doneByAgent.ContainsKey(agentId1) ||
                    !doneByAgent.ContainsKey(agentId2) ||
                    !doneByAgent.ContainsKey(agentId3)) &&
                   !waitCts.IsCancellationRequested)
            {
                await Task.Delay(25, waitCts.Token);
            }

            // ĐỐI SOÁT CHÉO TÍNH CÔ LẬP STREAMING CHUNKS & COMMANDS
            Assert.True(doneByAgent.ContainsKey(agentId1), "Agent 1 did not complete command execution");
            Assert.True(doneByAgent.ContainsKey(agentId2), "Agent 2 did not complete command execution");
            Assert.True(doneByAgent.ContainsKey(agentId3), "Agent 3 did not complete command execution");

            Assert.Equal(cmdId1, doneByAgent[agentId1].CommandId);
            Assert.Equal(0, doneByAgent[agentId1].ExitCode);
            Assert.Equal(cmdId2, doneByAgent[agentId2].CommandId);
            Assert.Equal(0, doneByAgent[agentId2].ExitCode);
            Assert.Equal(cmdId3, doneByAgent[agentId3].CommandId);
            Assert.Equal(0, doneByAgent[agentId3].ExitCode);

            void AuditAgentOutput(string targetAgentId, string expectedCmdId, string otherAgentId1, string otherAgentId2)
            {
                var chunks = chunksByAgent[targetAgentId];
                Assert.Equal(chunksPerCommand, chunks.Count);

                foreach (var chunk in chunks)
                {
                    Assert.Equal(expectedCmdId, chunk.CommandId);
                    Assert.Contains($"[OUT][{targetAgentId}]", chunk.Output);

                    // Tuyệt đối không chứa nhãn nhận diện của Agent khác
                    Assert.DoesNotContain($"[OUT][{otherAgentId1}]", chunk.Output);
                    Assert.DoesNotContain($"[OUT][{otherAgentId2}]", chunk.Output);
                }
            }

            AuditAgentOutput(agentId1, cmdId1, agentId2, agentId3);
            AuditAgentOutput(agentId2, cmdId2, agentId1, agentId3);
            AuditAgentOutput(agentId3, cmdId3, agentId1, agentId2);
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
    public async Task HighVolumeBurstMessages_WithVariablePayloadSizes_ZeroDataBleedingAcrossSessions()
    {
        await using var serverHelper = await TcpTestServerHelper.StartNewAsync();
        var server = serverHelper.Server;

        const string agentId1 = "agent-burst-01";
        const string agentId2 = "agent-burst-02";
        const string agentId3 = "agent-burst-03";

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var client1 = new AgentNetworkClient(NullLogger.Instance);
        var client2 = new AgentNetworkClient(NullLogger.Instance);
        var client3 = new AgentNetworkClient(NullLogger.Instance);

        var task1 = client1.RunAsync(serverHelper.Host, serverHelper.Port, agentId1, TimeSpan.FromSeconds(30), cts1.Token);
        var task2 = client2.RunAsync(serverHelper.Host, serverHelper.Port, agentId2, TimeSpan.FromSeconds(30), cts2.Token);
        var task3 = client3.RunAsync(serverHelper.Host, serverHelper.Port, agentId3, TimeSpan.FromSeconds(30), cts3.Token);

        try
        {
            await WaitForActiveCountAsync(server, 3, TimeSpan.FromSeconds(5));
            var session1 = await WaitForAgentSessionAsync(server, agentId1, TimeSpan.FromSeconds(5));
            var session2 = await WaitForAgentSessionAsync(server, agentId2, TimeSpan.FromSeconds(5));
            var session3 = await WaitForAgentSessionAsync(server, agentId3, TimeSpan.FromSeconds(5));

            const int messagesPerAgent = 100; // Tổng cộng 300 gói tin PING dồn dập

            var receivedPongs1 = new ConcurrentBag<NetOpsMessage>();
            var receivedPongs2 = new ConcurrentBag<NetOpsMessage>();
            var receivedPongs3 = new ConcurrentBag<NetOpsMessage>();

            var receivedCommands1 = new ConcurrentBag<CommandRequestPayload>();
            var receivedCommands2 = new ConcurrentBag<CommandRequestPayload>();
            var receivedCommands3 = new ConcurrentBag<CommandRequestPayload>();

            client1.MessageControl.PongReceived += (msg, _) => receivedPongs1.Add(msg);
            client2.MessageControl.PongReceived += (msg, _) => receivedPongs2.Add(msg);
            client3.MessageControl.PongReceived += (msg, _) => receivedPongs3.Add(msg);

            client1.MessageControl.CommandReceived += (_, payload) => { if (payload != null) receivedCommands1.Add(payload); };
            client2.MessageControl.CommandReceived += (_, payload) => { if (payload != null) receivedCommands2.Add(payload); };
            client3.MessageControl.CommandReceived += (_, payload) => { if (payload != null) receivedCommands3.Add(payload); };

            // Hàm tạo payload biến thiên kích thước từ nhỏ (50B) -> trung bình (2KB) -> lớn (16KB)
            static string GenerateVariablePayload(string agentId, int seq, out string hashToken)
            {
                hashToken = $"{agentId}-{seq:D4}-{Guid.NewGuid():N}";
                int paddingLength = (seq % 3) switch
                {
                    0 => 50,      // Small payload
                    1 => 2048,    // Medium payload (2KB)
                    _ => 16384    // Large payload (16KB)
                };

                return $"[BURST][{agentId}][seq-{seq:D4}][token-{hashToken}]" + new string('Z', paddingLength);
            }

            // Gửi dồn dập 100 gói PING từ từng Agent
            async Task SendAgentBurstAsync(AgentNetworkClient client, string agentId)
            {
                for (int i = 1; i <= messagesPerAgent; i++)
                {
                    var payload = GenerateVariablePayload(agentId, i, out var hashToken);
                    var pingMsg = new NetOpsMessage
                    {
                        MessageType = MessageType.Ping,
                        MessageId = $"msg-burst-{agentId}-{i:D4}",
                        RequestId = $"req-burst-{agentId}-{i:D4}-{hashToken}",
                        Payload = payload
                    };

                    await client.SendMessageAsync(pingMsg);
                }
            }

            // Master đồng thời gửi 20 lệnh COMMAND tới từng Agent với payload lớn biến thiên
            const int commandsPerAgent = 20;
            async Task SendMasterBurstCommandsAsync(AgentSession session, string agentId)
            {
                for (int j = 1; j <= commandsPerAgent; j++)
                {
                    var cmdToken = $"cmd-token-{agentId}-{j:D3}";
                    var cmdText = $"run-diagnostics --agent={agentId} --token={cmdToken} --padding=" + new string('A', 256 * (j % 5));
                    var cmdPayload = new CommandRequestPayload($"cmd-{agentId}-{j:D3}", cmdText, timeoutSeconds: 30);
                    var cmdMsg = NetOpsMessage.CreateRequest(MessageType.Command, cmdPayload, $"req-cmd-{agentId}-{j:D3}");

                    await session.SendMessageAsync(cmdMsg);
                }
            }

            // Chạy đồng thời toàn bộ 3 Agent gửi 100 PINGs + Master gửi 20 Commands tới mỗi Agent
            await Task.WhenAll(
                SendAgentBurstAsync(client1, agentId1),
                SendAgentBurstAsync(client2, agentId2),
                SendAgentBurstAsync(client3, agentId3),
                SendMasterBurstCommandsAsync(session1, agentId1),
                SendMasterBurstCommandsAsync(session2, agentId2),
                SendMasterBurstCommandsAsync(session3, agentId3)
            );

            // Chờ nhận đủ toàn bộ phản hồi PONG (300 pongs) và COMMANDS (60 commands)
            using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while ((receivedPongs1.Count < messagesPerAgent ||
                    receivedPongs2.Count < messagesPerAgent ||
                    receivedPongs3.Count < messagesPerAgent ||
                    receivedCommands1.Count < commandsPerAgent ||
                    receivedCommands2.Count < commandsPerAgent ||
                    receivedCommands3.Count < commandsPerAgent) &&
                   !waitCts.IsCancellationRequested)
            {
                await Task.Delay(25, waitCts.Token);
            }

            // THUẬT TOÁN ĐỐI SOÁT CHÉO TOÀN DIỆN (CROSS-AUDITING VALIDATION ENGINE)
            Assert.True(receivedPongs1.Count >= messagesPerAgent, $"Agent 1 expected >={messagesPerAgent} Pongs, but got {receivedPongs1.Count}");
            Assert.True(receivedPongs2.Count >= messagesPerAgent, $"Agent 2 expected >={messagesPerAgent} Pongs, but got {receivedPongs2.Count}");
            Assert.True(receivedPongs3.Count >= messagesPerAgent, $"Agent 3 expected >={messagesPerAgent} Pongs, but got {receivedPongs3.Count}");

            Assert.True(receivedCommands1.Count >= commandsPerAgent, $"Agent 1 expected >={commandsPerAgent} Commands, but got {receivedCommands1.Count}");
            Assert.True(receivedCommands2.Count >= commandsPerAgent, $"Agent 2 expected >={commandsPerAgent} Commands, but got {receivedCommands2.Count}");
            Assert.True(receivedCommands3.Count >= commandsPerAgent, $"Agent 3 expected >={commandsPerAgent} Commands, but got {receivedCommands3.Count}");

            int crossTalkViolations = 0;
            int totalMessagesAudited = 0;

            void AuditPongList(IEnumerable<NetOpsMessage> pongs, string targetAgentId, string otherId1, string otherId2)
            {
                var burstPongs = pongs.Where(p => p.InReplyTo?.StartsWith("msg-burst-", StringComparison.Ordinal) == true).ToList();
                Assert.True(burstPongs.Count >= messagesPerAgent, $"Expected >={messagesPerAgent} burst pongs for {targetAgentId}, but got {burstPongs.Count}");

                foreach (var pong in burstPongs)
                {
                    totalMessagesAudited++;
                    Assert.NotNull(pong.RequestId);
                    Assert.NotNull(pong.InReplyTo);

                    // Phải mang mã của targetAgentId
                    if (pong.RequestId.Contains(targetAgentId, StringComparison.Ordinal))
                    {
                        Assert.StartsWith($"msg-burst-{targetAgentId}-", pong.InReplyTo);
                    }
                    else
                    {
                        crossTalkViolations++;
                    }

                    // Tuyệt đối không chứa ID của các Agent khác
                    if (pong.RequestId.Contains(otherId1, StringComparison.Ordinal) || pong.RequestId.Contains(otherId2, StringComparison.Ordinal))
                    {
                        crossTalkViolations++;
                    }
                }

                // Kiểm tra toàn bộ danh sách gói tin nhận được không chứa ID của các Agent khác
                foreach (var pong in pongs)
                {
                    if (pong.RequestId != null && (pong.RequestId.Contains(otherId1, StringComparison.Ordinal) || pong.RequestId.Contains(otherId2, StringComparison.Ordinal)))
                    {
                        crossTalkViolations++;
                    }
                }
            }

            void AuditCommandList(IEnumerable<CommandRequestPayload> commands, string targetAgentId, string otherId1, string otherId2)
            {
                foreach (var cmd in commands)
                {
                    totalMessagesAudited++;
                    Assert.StartsWith($"cmd-{targetAgentId}-", cmd.CommandId);
                    Assert.Contains($"--agent={targetAgentId}", cmd.CommandLine);

                    if (cmd.CommandLine.Contains(otherId1, StringComparison.Ordinal) || cmd.CommandLine.Contains(otherId2, StringComparison.Ordinal))
                    {
                        crossTalkViolations++;
                    }
                }
            }

            AuditPongList(receivedPongs1, agentId1, agentId2, agentId3);
            AuditPongList(receivedPongs2, agentId2, agentId1, agentId3);
            AuditPongList(receivedPongs3, agentId3, agentId1, agentId2);

            AuditCommandList(receivedCommands1, agentId1, agentId2, agentId3);
            AuditCommandList(receivedCommands2, agentId2, agentId1, agentId3);
            AuditCommandList(receivedCommands3, agentId3, agentId1, agentId2);

            // Tỷ lệ rò rỉ dữ liệu chéo (Cross-Talk Rate) phải bằng chính xác 0.00%
            double crossTalkRate = (double)crossTalkViolations / totalMessagesAudited;
            Assert.Equal(0, crossTalkViolations);
            Assert.Equal(0.0, crossTalkRate);
        }
        finally
        {
            cts1.Cancel();
            cts2.Cancel();
            cts3.Cancel();

            await Task.WhenAll(task1, task2, task3);
        }
    }
}


