using System.Diagnostics;
using Grpc.Core;
using NetOps.Shared; // Namespace chứa protobuf generated code của bạn
using Microsoft.Extensions.Logging;

namespace NetOps.Agent.Services
{
    /// <summary>
    /// Chịu trách nhiệm thực thi lệnh cục bộ trên máy Agent (Windows/Linux)
    /// </summary>
    public class CommandExecutor
    {
        private readonly ILogger<CommandExecutor> _logger;

        public CommandExecutor(ILogger<CommandExecutor> logger)
        {
            _logger = logger;
        }

        public async Task ExecuteCommandAsync(
            CommandRequest request, 
            IAsyncStreamWriter<AgentMessage> responseStream, 
            string agentId)
        {
            // 1. Cài đặt CancellationToken để xử lý Timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            var token = cts.Token;

            // 2. Chọn Shell dựa trên nền tảng và yêu cầu
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            // Bỏ cái ShellType giùm, có điều kiện OS tự kiểm tra là đủ rồi
            // Tự dưng server phải chọn ShellType ở đâu ra? Nhập tay hả?
            // Chưa thấy logic nào để chọn ShellType từ phía server khi gửi lệnh đi cả
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "powershell.exe";
                // Dùng -NonInteractive và -NoProfile để script chạy ngầm ổn định, không bị treo chờ input
                psi.Arguments = $"-NoProfile -NonInteractive -Command \"{request.CommandText}\"";
            }
            else
            {
                // Mặc định cho Linux/MacOS hoặc yêu cầu Bash
                psi.FileName = "/bin/bash";
                psi.Arguments = $"-c \"{request.CommandText}\"";
            }

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();
                _logger.LogInformation("Bắt đầu chạy lệnh {CommandId}, PID: {PID}", request.CommandId, process.Id);

                // 3. Đọc Real-time StdOut và StdErr song song
                var readOutTask = StreamOutputAsync(process.StandardOutput, request.CommandId, agentId, false, responseStream, token);
                var readErrTask = StreamOutputAsync(process.StandardError, request.CommandId, agentId, true, responseStream, token);

                // Đợi Process chạy xong HOẶC bị Timeout (bị hủy bởi token)
                await process.WaitForExitAsync(token);
                await Task.WhenAll(readOutTask, readErrTask);

                // Báo cáo lệnh hoàn tất
                await SendCommandStatusAsync(responseStream, agentId, request.CommandId, process.ExitCode, "Hoàn thành");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Lệnh {CommandId} bị Timeout hoặc hủy. Tiến hành dọn dẹp Process Tree.", request.CommandId);
                
                // CƠ CHẾ KILL TOÀN BỘ PROCESS TREE (Tránh treo ngầm)
                // true = Kill cả tiến trình cha lẫn các tiến trình con do nó sinh ra
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true); 
                }
                
                await SendCommandStatusAsync(responseStream, agentId, request.CommandId, -1, "Timeout/Hủy lệnh (Đã dọn dẹp Process Tree)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy lệnh {CommandId}", request.CommandId);
                await SendCommandStatusAsync(responseStream, agentId, request.CommandId, -1, $"Lỗi hệ thống: {ex.Message}");
            }
        }

        // Đọc từng dòng kết quả và Stream bắn về Server ngay lập tức
        private async Task StreamOutputAsync(
            StreamReader reader, 
            string commandId, 
            string agentId,
            bool isError, 
            IAsyncStreamWriter<AgentMessage> responseStream, 
            CancellationToken token)
        {
            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line != null)
                {
                    // Đóng gói và đẩy qua gRPC stream
                    var msg = new AgentMessage
                    {
                        CommandOutput = new CommandOutput
                        {
                            AgentId = agentId,
                            CommandId = commandId,
                            OutputLine = line,
                            IsError = isError,


                        }
                    };
                    await responseStream.WriteAsync(msg);
                }
            }
        }

        private async Task SendCommandStatusAsync(IAsyncStreamWriter<AgentMessage> responseStream, string agentId, string commandId, int exitCode, string status)
        {
            var msg = new AgentMessage
            {
                CommandOutput = new CommandOutput
                {
                    AgentId = agentId,
                    CommandId = commandId,
                    OutputLine = $"[EXIT CODE: {exitCode}] - {status}",
                    IsError = exitCode != 0,

                    // Um t thêm 2 trường này vo thoi
                    IsCompleted = true,
                    ExitCode = exitCode
                }
            };
            await responseStream.WriteAsync(msg);
        }
    }
}
