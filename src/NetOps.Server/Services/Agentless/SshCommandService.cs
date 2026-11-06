using Renci.SshNet;
using Microsoft.Extensions.Logging;

namespace NetOps.Server.Services.Agentless
{
    /// <summary>
    /// Module Agentless: Cho phép Server chạy lệnh trực tiếp vào các máy Linux không cài Agent thông qua SSH.
    /// Yêu cầu cài đặt NuGet package: SSH.NET
    /// </summary>
    public class SshCommandService
    {
        private readonly ILogger<SshCommandService> _logger;

        public SshCommandService(ILogger<SshCommandService> logger)
        {
            _logger = logger;
        }

        // Hàm thực thi lệnh SSH đơn giản lấy kết quả 1 lần (Dành cho lệnh chạy nhanh)
        public async Task<string> ExecuteQuickCommandAsync(string host, string username, string password, string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var client = new SshClient(host, username, password);
                    client.Connect();
                    _logger.LogInformation("Đã kết nối SSH thành công tới {Host}", host);

                    using var cmd = client.CreateCommand(command);
                    var result = cmd.Execute();
                    
                    client.Disconnect();
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi chạy lệnh SSH tới {Host}", host);
                    throw;
                }
            });
        }

        // Hàm thực thi lệnh SSH hỗ trợ Stream Real-time từng dòng trả về
        public async Task StreamCommandAsync(string host, string username, string password, string command, Action<string> onLineReceived)
        {
            await Task.Run(() =>
            {
                using var client = new SshClient(host, username, password);
                client.Connect();
                
                using var cmd = client.CreateCommand(command);
                
                // Mở luồng bất đồng bộ
                var asyncResult = cmd.BeginExecute();
                
                using var reader = new StreamReader(cmd.OutputStream);
                while (!asyncResult.IsCompleted || !reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                    {
                        onLineReceived(line); // Gọi callback ngay khi có dòng mới
                    }
                }

                cmd.EndExecute(asyncResult);
                client.Disconnect();
            });
        }
    }
}
