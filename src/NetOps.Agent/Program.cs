using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NetOps.Agent;
using NetOps.Agent.Services;
using NetOps.Shared;

const string DefaultHubUrl = "https://api.r1antech.online/hubs/agents";
const string DefaultTokenUrl = "https://auth.r1antech.online/realms/netops/protocol/openid-connect/token";

// Dùng lúc cấp quyền lần đầu. Lệnh này chỉ in ID của máy rồi thoát, chưa mở kết nối mạng.
if (args is ["--show-id"])
{
    Console.WriteLine(SystemInfoService.GetAgentId());
    return;
}

var cliHubUrl = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg));
var envHubUrl = Environment.GetEnvironmentVariable("NETOPS_HUB_URL");
var hubUrl = !string.IsNullOrWhiteSpace(cliHubUrl)
    ? cliHubUrl
    : !string.IsNullOrWhiteSpace(envHubUrl)
        ? envHubUrl
        : DefaultHubUrl;

if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var hubUri) || !CanSendCredentialsTo(hubUri))
{
    Console.Error.WriteLine("Hub URL phai dung HTTPS; HTTP chi duoc dung tren localhost.");
    Environment.ExitCode = 1;
    return;
}

// Secret chỉ gửi đến Keycloak của môi trường đang dùng. Khi đổi Server, người cài
// cần đặt NETOPS_TOKEN_URL để tránh gửi secret của lab đến Keycloak mặc định.
var tokenUrl = Environment.GetEnvironmentVariable("NETOPS_TOKEN_URL");
if (tokenUrl is null && hubUri.Authority == new Uri(DefaultHubUrl).Authority)
    tokenUrl = DefaultTokenUrl;

if (!Uri.TryCreate(tokenUrl, UriKind.Absolute, out var tokenUri) || !CanSendCredentialsTo(tokenUri))
{
    Console.Error.WriteLine("Hãy đặt NETOPS_TOKEN_URL tới endpoint cấp token của Keycloak. Dùng HTTPS, hoặc HTTP trên localhost.");
    Environment.ExitCode = 1;
    return;
}

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var executorLogger = loggerFactory.CreateLogger<CommandExecutor>();
var commandExecutor = new CommandExecutor(executorLogger);

var info = SystemInfoService.GetRegistrationInfo();
var secretPath = Path.Combine(SystemInfoService.GetDataDirectory(), "client-secret.txt");
if (!File.Exists(secretPath))
{
    Console.Error.WriteLine($"Hãy cấp Client ID {info.AgentId} trong Keycloak và lưu secret của máy vào {secretPath}.");
    Environment.ExitCode = 1;
    return;
}
Console.WriteLine($"Agent {info.Hostname} (ID: {info.AgentId}). Nhan Ctrl+C de dung.");

using var shutdown = new CancellationTokenSource();
// Bắt tín hiệu Ctrl+C (SIGINT) khi chạy trực tiếp trên terminal.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};
// Bắt thêm tín hiệu SIGTERM khi chạy dưới dạng systemd service trên Linux (`systemctl stop netops-agent`):
// giúp Agent đóng kết nối SignalR sạch sẽ ngay lập tức thay vì bị systemd đợi 90 giây rồi ép kill.
using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
{
    ctx.Cancel = true;
    shutdown.Cancel();
});

// Dùng lại HttpClient cho các lần xin token. Tắt chuyển hướng để một phản hồi HTTP
// không thể chuyển tiếp yêu cầu chứa client_secret sang địa chỉ khác.
using var keycloak = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
await using var connection = new HubConnectionBuilder()
    .WithUrl(hubUri, HttpTransportType.WebSockets, options =>
    {
        // Mỗi lần mở lại kết nối, SignalR gọi hàm này để lấy access token cho header Authorization.
        options.AccessTokenProvider = () =>
            AgentTokenClient.RequestAsync(keycloak, tokenUri, info.AgentId, secretPath, shutdown.Token);
    })
    .Build();

// Ổ khóa chỉ cho phép chạy tuần tự từng lệnh một trên Agent này,
// tránh nhiều lệnh chạy chồng nhau làm quá tải máy hoặc trộn lẫn output hiển thị trên Desktop.
var commandLock = new SemaphoreSlim(1, 1);

// Đăng ký handler nhận lệnh một lần trước vòng lặp reconnect để tránh trùng lặp handler.
connection.On<CommandRequest>("ReceiveCommand", cmdReq =>
{
    Console.WriteLine($"Nhan lenh {cmdReq.CommandId}: {cmdReq.CommandText}");
    _ = ExecuteAssignedCommandAsync(commandExecutor, commandLock, connection, cmdReq, info.AgentId, shutdown.Token);
});

try
{
    while (!shutdown.IsCancellationRequested)
    {
        try
        {
            // Mỗi phiên kết nối đều đăng ký lại trước khi gửi heartbeat.
            await connection.StartAsync(shutdown.Token);
            var reply = await connection.InvokeAsync<RegisterAgentResponse>("Register", info, shutdown.Token);
            if (!reply.Success)
            {
                throw new InvalidOperationException(reply.Message);
            }
            Console.WriteLine($"Server phan hoi: {reply.Message}");

            // -- ĐOẠN CODE MỚI (TRUYỀN TRỰC TIẾP SỐ LIỆU CẢM BIẾN MÁY THẬT) --
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(shutdown.Token))
            {
                // Đọc thông số phần cứng tức thời từ cảm biến
                var currentCpu = MetricsExportService.GetCpuUsagePercent();
                var currentRamBytes = MetricsExportService.GetMemoryUsedBytes();
                var currentDiskFreeBytes = MetricsExportService.GetSystemDriveFreeBytes();

                await connection.InvokeAsync("Heartbeat", new HeartbeatMessage
                {
                    AgentId = info.AgentId,
                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    CpuUsagePercent = currentCpu,
                    MemoryUsedBytes = currentRamBytes,
                    DiskFreeBytes = currentDiskFreeBytes
                }, shutdown.Token);

                // In nhật ký chi tiết ra Console để tiện theo dõi
                var ramMb = currentRamBytes / (1024 * 1024);
                var diskGb = Math.Round((double)currentDiskFreeBytes / (1024 * 1024 * 1024), 1);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Da gui Heartbeat: CPU {currentCpu:F1}% | RAM {ramMb} MB | Disk Trong {diskGb} GB");
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ket noi bi gian doan: {ex.Message}. Thu lai sau 5 giay.");
        }
        finally
        {
            await connection.StopAsync();
        }

        await Task.Delay(TimeSpan.FromSeconds(5), shutdown.Token);
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    // Ctrl+C cũng có thể đến trong lúc chờ thử kết nối lại.
}

// Cả secret gửi đến Keycloak và token gửi đến Hub đều cần đường truyền mã hóa.
// HTTP trên loopback vẫn dùng được khi chạy các dịch vụ trên cùng máy phát triển.
static bool CanSendCredentialsTo(Uri uri) =>
    uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

static async Task ExecuteAssignedCommandAsync(
    CommandExecutor executor,
    SemaphoreSlim commandLock,
    HubConnection connection,
    CommandRequest command,
    string agentId,
    CancellationToken shutdownToken)
{
    var lockAcquired = false;
    try
    {
        // Xếp hàng chờ nếu Agent đang bận chạy lệnh trước đó; tự hủy chờ nếu Agent tắt.
        await commandLock.WaitAsync(shutdownToken);
        lockAcquired = true;

        await executor.ExecuteCommandAsync(
            command,
            output => connection.InvokeAsync("SendCommandOutput", output, shutdownToken),
            agentId,
            shutdownToken);
    }
    catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
    {
        // Đang tắt Agent nên dừng chờ/gửi output.
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Loi khi gui ket qua lenh {command.CommandId}: {ex.Message}");
    }
    finally
    {
        // Chỉ mở khóa khi thực sự đã chiếm được khóa (tránh lỗi nếu bị hủy ngay lúc đang chờ ở WaitAsync).
        if (lockAcquired)
            commandLock.Release();
    }
}
