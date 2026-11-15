using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetOps.Shared;

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
            Func<CommandOutput, Task> sendOutputAsync,
            string agentId)
        {
            // 1. Cài đặt CancellationToken để xử lý Timeout
            var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 60;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
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
                var readOutTask = StreamOutputAsync(process.StandardOutput, request.CommandId, agentId, false, sendOutputAsync, token);
                var readErrTask = StreamOutputAsync(process.StandardError, request.CommandId, agentId, true, sendOutputAsync, token);

                // Đợi Process chạy xong HOẶC bị Timeout (bị hủy bởi token)
                await process.WaitForExitAsync(token);
                await Task.WhenAll(readOutTask, readErrTask);

                // Báo cáo lệnh hoàn tất
                await SendCommandStatusAsync(sendOutputAsync, agentId, request.CommandId, process.ExitCode, "Hoàn thành");
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

                await SendCommandStatusAsync(sendOutputAsync, agentId, request.CommandId, -1, "Timeout/Hủy lệnh (Đã dọn dẹp Process Tree)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy lệnh {CommandId}", request.CommandId);
                await SendCommandStatusAsync(sendOutputAsync, agentId, request.CommandId, -1, $"Lỗi hệ thống: {ex.Message}");
            }
        }

        // Đọc từng dòng kết quả và đẩy về Server ngay lập tức
        private static async Task StreamOutputAsync(
            StreamReader reader,
            string commandId,
            string agentId,
            bool isError,
            Func<CommandOutput, Task> sendOutputAsync,
            CancellationToken token)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                var output = new CommandOutput
                {
                    AgentId = agentId,
                    CommandId = commandId,
                    OutputLine = line,
                    IsError = isError
                };
                await sendOutputAsync(output);
            }
        }

        private static async Task SendCommandStatusAsync(
            Func<CommandOutput, Task> sendOutputAsync,
            string agentId,
            string commandId,
            int exitCode,
            string status)
        {
            var output = new CommandOutput
            {
                AgentId = agentId,
                CommandId = commandId,
                OutputLine = $"[EXIT CODE: {exitCode}] - {status}",
                IsError = exitCode != 0,
                IsCompleted = true,
                ExitCode = exitCode
            };
            await sendOutputAsync(output);
        }
    }
}
