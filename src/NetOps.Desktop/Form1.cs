namespace NetOps.Desktop;
using Duende.IdentityModel.OidcClient;
// Để sử dụng RpcException để bắt lỗi lời gọi gRPC
using Grpc.Core;
// Để sử dụng GrpcChannel để cấu hình giao tiếp tới Server
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;
// Để sd SubmitCommandRequest và CommandServiceClient được sinh từ protobuf
using NetOps.Shared;

public partial class Form1 : AntdUI.Window
{
    // chứa cấu hình giao tiếp tới Server.
    private readonly GrpcChannel _channel;

    // cung cấp phương thức gọi API SubmitCommand.

    private List<AgentSummary> _agents = new();

    private readonly HubConnection _hubConnection;

    private readonly CommandService.CommandServiceClient _commandClient;

    private readonly OidcClient _oidcClient;

    // [2.1] Thêm một field để giữ token
    // ?: cho phép giá trị là null, vì lúc ứng dụng vừa mở thì chưa đăng nhập.
    // Giá trị sẽ thay đổi sau khi đăng nhập, nên field này không đặt readonly.
    // Vì sao phải giữ trong field? Vì chúng ta nhận token trong OnShown(), nhưng phần cấu hình SignalR cũng cần đọc được nó.
    private string? _accessToken;
    private bool _connecting;
    private int _outputLineCount;

    public Form1()
    {
        InitializeComponent();

        // Tạo một đối tượng chứa cấu hình đăng nhập, rồi giữ nó trong biến options.
        var options = new OidcClientOptions
        {
            // Nơi đăng nhập: Keycloak, realm netops
            Authority = "http://localhost:8080/realms/netops",
            // Ứng dụng yêu cầu: netops-desktop
            ClientId = "netops-desktop",
            // Nơi nhận kết quả
            RedirectUri = "http://127.0.0.1:7890/",
            // Thông tin yêu cầu: đăng nhập, hồ sơ cơ bản và email
            // Danh sách scope này phục vụ việc yêu cầu thông tin trong quy trình đăng nhập.
            Scope = "openid profile email",
            // Khi cần mở trình duyệt để đăng nhập, hãy sử dụng đối tượng SystemBrowser này.
            Browser = new SystemBrowser()
        };

        // Tạo công cụ đăng nhập sử dụng phiếu cấu hình đó,
        // rồi giữ công cụ trong field _oidcClient để các hàm của Form1 dùng được.
        _oidcClient = new OidcClient(options);

        _channel = GrpcChannel.ForAddress("https://localhost:7027");

        _commandClient = new CommandService.CommandServiceClient(_channel);

        // Đoạn này mới tạo đối tượng chứa cấu hình kết nối. Muốn kết nối thật, cần gọi StartAsync().
        // [2.3] Cấu hình SignalR lấy token từ field vừa tạo
        // Đây là cách cấu hình AccessTokenProvider theo ví dụ SignalR .NET client của Microsoft.
        // https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0#bearer-token-authentication

        _hubConnection = new HubConnectionBuilder()
            .WithUrl("https://localhost:7027/hubs/netops", connectionOptions =>
            {
                // connectionOptions là gì?
                // connectionOptions là đối tượng cấu hình kết nối HTTP của SignalR.
                // Nó có kiểu HttpConnectionOptions, thuộc thư viện Microsoft.
                // Sử dụng thuộc tính AccessTokenProvider để chỉ cho SignalR cách lấy token khi cần gửi yêu cầu (gửi lệnh).
                // https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.connections.client.httpconnectionoptions.accesstokenprovider?view=aspnetcore-10.0
                // Vì sao lại có () =>?
                // là cách viết một hàm không có tham số, thường gọi là biểu thức lambda.
                // Ở đây, chúng ta đưa hàm ấy cho SignalR, với ý nghĩa: Khi cần token, hãy gọi hàm này để lấy giá trị _accessToken hiện tại.
                // Nó giải thích được một điều: constructor chạy trước khi đăng nhập, vậy lúc đó _accessToken còn null thì sao?
                // Trình tự thực tế là:
                //Constructor chạy:
                //    _accessToken đang null.
                //    Đăng ký hàm cung cấp token cho SignalR.
                //                 ↓
                //OnShown chạy:
                //    Đăng nhập thành công.
                //    Gán token vào _accessToken.
                //                 ↓
                //ConnectToServerAsync chạy:
                //    Gọi _hubConnection.StartAsync().
                //    SignalR gọi hàm đã đăng ký để lấy token.
                //    Lúc này _accessToken đã có giá trị.
                connectionOptions.AccessTokenProvider = () =>
                {
                    // Task.FromResult(_accessToken) làm gì?
                    // AccessTokenProvider yêu cầu hàm cung cấp token trả về một Task<string?>.
                    // Trong khi đó, _accessToken chỉ là một chuỗi đã có sẵn. Vì vậy:
                    // Task.FromResult(_accessToken)
                    // tạo một Task đã hoàn thành, chứa chuỗi token ấy làm kết quả.
                    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.fromresult?view=net-10.0
                    return Task.FromResult(_accessToken);
                };
            })
            .Build();
        // Khi kết nối SignalR nhận sự kiện tên CommandOutputReceived,
        // nhận dữ liệu kiểu CommandOutput, rồi đưa nội dung vào khung Command Output.
        _hubConnection.On<CommandOutput>("CommandOutputReceived", ReceiveCommandOutputAsync);
        _hubConnection.Closed += OnConnectionClosedAsync;



    }

    private Task ReceiveCommandOutputAsync(CommandOutput output)
    {
        return RunOnUiAsync(() =>
        {
            if (!string.IsNullOrEmpty(output.OutputLine))
            {
                var endsWithNewLine = output.OutputLine.EndsWith('\n');
                txtResult.AppendText(endsWithNewLine
                    ? output.OutputLine
                    : output.OutputLine + Environment.NewLine);
                _outputLineCount += output.OutputLine.Count(c => c == '\n') + (endsWithNewLine ? 0 : 1);
                UpdateOutputControls();

                if (btnFollow.Toggle)
                    txtResult.ScrollToEnd();
            }

            badgeOutput.State = output.IsCompleted
                ? (output.ExitCode == 0 ? AntdUI.TState.Success : AntdUI.TState.Error)
                : AntdUI.TState.Processing;
            badgeOutput.Text = output.IsCompleted
                ? $"Completed · exit {output.ExitCode}"
                : "Receiving output";

            if (output.IsCompleted)
            {
                if (txtResult.Text.Length == 0)
                {
                    lblEmptyTitle.Text = "No output returned";
                    lblEmptyHint.Text = "The command finished without returning output.";
                }
                SetStatus($"Command {output.CommandId} completed (exit {output.ExitCode}).", output.ExitCode != 0);
            }
        });
    }

    // Callback SignalR chạy trên luồng khác; chuyển thao tác giao diện về luồng UI.
    private async Task RunOnUiAsync(Action update)
    {
        if (IsDisposed || Disposing)
            return;

        try
        {
            // Chuyển việc cập nhật giao diện từ luồng SignalR về luồng UI.
            await this.InvokeAsync(() =>
            {
                if (IsDisposed || Disposing)
                    return;

                update();
            });
        }
        catch (ObjectDisposedException)
        {
            // Form đã đóng trong lúc đang chờ cập nhật giao diện.
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing)
        {
            // Handle của Form đã bị giải phóng.
        }
    }

    private void btnClear_Click(object? sender, EventArgs e)
    {
        txtResult.Clear();
        _outputLineCount = 0;
        badgeOutput.State = AntdUI.TState.Default;
        badgeOutput.Text = "Waiting for output";
        lblEmptyTitle.Text = "Your output starts here";
        lblEmptyHint.Text = "Choose an agent and send a command.\nIts output will appear here.";
        UpdateOutputControls();
    }

    private void UpdateOutputControls()
    {
        var hasOutput = txtResult.Text.Length > 0;
        emptyLayout.Visible = !hasOutput;
        btnCopy.Enabled = hasOutput;
        btnClear.Enabled = hasOutput;
        lblOutputCount.Text = _outputLineCount == 1 ? "1 line" : $"{_outputLineCount} lines";
    }

    private void btnCopy_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(txtResult.Text))
            return;

        try
        {
            Clipboard.SetText(txtResult.Text);
            SetStatus("Command output copied to clipboard.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("Clipboard is busy. Please try copying again.", true);
        }
    }

    private void commandField_TextChanged(object? sender, EventArgs e)
    {
        if (sender is AntdUI.Input input)
            input.Status = AntdUI.TType.None;
    }

    private void SetStatus(string text, bool error = false)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = error ? Color.FromArgb(185, 28, 28) : Color.FromArgb(97, 115, 139);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            if (btnSend.Enabled)
                btnSend_Click(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // [1.1]
    // OnShown là phương thức có sẵn của Form, Override nó để bổ sung việc kết nối SignalR, khi cửa sổ xuất hiện, chương trình kết nối ngay tới Server.
    // Giờ cần sửa lại thành Form xuất hiện ➞ Đăng nhập Keycloak ➞ Kiểm tra kết quả ➞ Hiển thị kq ➞ Hiển thị tk đã login ➞ Kết nối tới server
    protected override async void OnShown(EventArgs e)
    {
        // Microsoft yêu cầu giữ lời gọi base.OnShown(e) khi ghi đè phương thức này.
        // https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.onshown?view=windowsdesktop-10.0
        base.OnShown(e);

        // [1.2] Thay riêng phương thức OnShown
        try
        {
            SetStatus("Opening sign-in...");

            // [1.3] Khi chạy tới LoginAsync(), chuyện gì xảy ra?
            // Trong lúc ta đăng nhập trên trình duyệt thì nhờ có await thì ta sẽ chờ dòng này thực hiện xong
            // Những dòng sau đó chỉ được chạy tiếp khi có result
            // Chính xác hơn là nếu tác vụ chưa xong, phương thức hiện tại tạm dừng và nhường luồng UI để xử lý công việc khác.
            // Khi tác vụ xong, phương thức tiếp tục.
            // https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios
            var result = await _oidcClient.LoginAsync();

            if (IsDisposed || Disposing)
                return;

            // [1.4] Vì sao cần kiểm tra result.IsError?
            // LoginAsync() trả về đối tượng kiểu LoginResult.
            // Đối tượng này có thông tin kết quả và lỗi nếu đăng nhập không thành công.
            // https://docs.duendesoftware.com/identitymodel-oidcclient/automatic/#loginresult-properties
            // IsError: có lỗi hay không.
            if (result.IsError)
            {
                SetStatus(
                    // Error: mã hoặc tên lỗi.
                    // ErrorDescription: mô tả bổ sung.
                    $"Sign-in failed: {result.Error} {result.ErrorDescription}",
                    true);
                // return: kết thúc OnShown, nên chương trình chưa chạy tới ConnectToServerAsync().
                return;
                // Khối catch phía cuối xử lý những ngoại lệ phát sinh trong quá trình thực hiện, chẳng hạn lỗi mở bộ nhận callback.
            }

            // [2.2] Lấy token từ kết quả đăng nhập
            // Lấy chuỗi access token từ kết quả đăng nhập và giữ nó trong field _accessToken của Form.
            _accessToken = result.AccessToken;

            // [1.5] Đọc tên tài khoản từ đâu?
            // Sau khi thành công, result.User chứa thông tin danh tính dưới dạng các claim.
            // Mỗi claim là một mục thông tin có tên và giá trị.

            // Lấy đối tượng chứa danh tính người dùng.
            var username = result.User
                // Tìm claim đầu tiên có tên preferred_username.
                // FindFirst là phương thức của ClaimsPrincipal trong .NET.
                // https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.findfirst?view=net-10.0
                // ?.Value: Nếu tìm thấy claim, lấy giá trị của nó. Giá trị ở đây là tài khoản đăng nhập.
                // ?? "user": Nếu không có giá trị, dùng chữ "user" để vẫn hiển thị được thông báo.
                // Đây là giá trị dự phòng để thông báo vẫn có chữ khi không lấy được preferred_username
                // Theo chuẩn OIDC, các thông tin hồ sơ được yêu cầu bằng scope profile,
                // bao gồm preferred_username, là các claim không bắt buộc phải luôn được trả về.
                // https://openid.net/specs/openid-connect-core-1_0.html#ScopeClaims
                .FindFirst("preferred_username")?.Value ?? "user";

            // hiện hộp thoại:
            MessageBox.Show(
                this,
                $"Signed in as {username}.",
                "NetOps");
            // Khi bạn bấm OK trên hộp thoại, chương trình tiếp tục chạy:
            // Sau đó bắt đầu kết nối,
            // rồi chờ kết nối bằng await.
            await ConnectToServerAsync();
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
                return;

            SetStatus($"Sign-in failed: {ex.Message}", true);
        }
    }

    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        await ConnectToServerAsync();
    }

    private void SetConnectionState(bool connected, string text, AntdUI.TState state)
    {
        badgeConnection.Text = text;
        badgeConnection.State = state;
        btnConnect.Text = "Connect";
        btnConnect.Enabled = !connected && !_connecting;
        btnSend.Enabled = connected && !btnSend.Loading;
    }

    private Task OnConnectionClosedAsync(Exception? exception)
    {
        return RunOnUiAsync(() =>
        {
            SetConnectionState(false, "Disconnected", AntdUI.TState.Error);
            SetStatus("Connection lost. Click Connect to reconnect to the server.", true);
        });
    }


    private async Task ConnectToServerAsync()
    {

        if (_connecting || _hubConnection.State == HubConnectionState.Connected)
            return;

        // [2.4] Chặn nút Connect kết nối trước khi có token
        // Điều kiện này kiểm tra Desktop đã giữ được token hay chưa.
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            SetStatus("Please sign in before connecting.", true);
            return;
        }

        _connecting = true;
        btnConnect.Loading = true;
        SetConnectionState(false, "Connecting...", AntdUI.TState.Processing);
        SetStatus("Connecting to the server...");
        try
        {
            await _hubConnection.StartAsync();

            if (IsDisposed)
                return;

            SetConnectionState(true, "Connected", AntdUI.TState.Success);
            await LoadAgentsAsync();
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;

            SetConnectionState(false, "Connection failed", AntdUI.TState.Error);
            SetStatus($"Cannot connect to the server: {ex.Message}", true);
        }
        finally
        {
            _connecting = false;
            if (!IsDisposed)
            {
                btnConnect.Loading = false;
                btnConnect.Enabled = _hubConnection.State == HubConnectionState.Disconnected;
            }
        }
    }

    // "GetAgents": tên phương thức trên Hub cần gọi.
    // List<AgentSummary>: kiểu kết quả Desktop chờ nhận.
    // InvokeAsync: gửi lời gọi và chờ kết quả từ Server.
    private async Task LoadAgentsAsync()
    {
        try
        {
            var agents = await _hubConnection
                .InvokeAsync<List<AgentSummary>>("GetAgents");

            if (IsDisposed)
                return;

            _agents = agents;
            SetStatus($"Connected. Known agents: {_agents.Count}.");
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;

            SetStatus($"Cannot load agents: {ex.Message}", true);
        }
    }

    private void txtAgentId_SuffixClick(object sender, MouseEventArgs e)
    {
        if (_hubConnection.State != HubConnectionState.Connected)
        {
            SetStatus("Please connect to the server before browsing agents.", true);
            return;
        }

        using var window = new AgentsForm();
        window.ReloadAgentsAsync = async cancellationToken =>
        {
            var agents = await _hubConnection.InvokeAsync<List<AgentSummary>>(
                "GetAgents", cancellationToken);
            _agents = agents;
            return agents;
        };
        window.SetAgents(_agents);

        if (window.ShowDialog(this) == DialogResult.OK
            && window.SelectedAgent is AgentSummary selected)
        {
            txtAgentId.Text = selected.AgentId;
            txtCommand.Focus();
            SetStatus($"Selected {selected.Hostname} ({selected.IpAddress}).");
        }
    }

    // Cần dùng tới await trong hàm, cho nên khai báo async
    private async void btnSend_Click(object sender, EventArgs e)
    {
        if (!btnSend.Enabled)
            return;

        // Láo nháo không nhập mà bấm Send là không gửi đâu
        if (string.IsNullOrWhiteSpace(txtAgentId.Text)
            || string.IsNullOrWhiteSpace(txtCommand.Text))
        {
            var missingAgent = string.IsNullOrWhiteSpace(txtAgentId.Text);
            txtAgentId.Status = missingAgent ? AntdUI.TType.Error : AntdUI.TType.None;
            txtCommand.Status = string.IsNullOrWhiteSpace(txtCommand.Text)
                ? AntdUI.TType.Error : AntdUI.TType.None;
            SetStatus("Please enter an Agent ID and a command.", true);
            if (missingAgent)
                txtAgentId.Focus();
            else
                txtCommand.Focus();
            return;
        }

        var desktopConnectionId = _hubConnection.ConnectionId;

        if (string.IsNullOrWhiteSpace(desktopConnectionId))
        {
            SetStatus("Please connect to the server before sending. ", true);
            return;
        }

        // Bấm gửi thì tạm thời khoá nút lại
        btnSend.Enabled = false;
        btnSend.Loading = true;
        SetStatus("Submitting command...");
        badgeOutput.State = AntdUI.TState.Processing;
        badgeOutput.Text = "Submitting command";

        try
        {
            // Đọc dữ liệu người dùng nhập trên Form.
            var request = new SubmitCommandRequest
            {
                // Um ID không có dấu cách nên là Trim cho chắc ha
                AgentId = txtAgentId.Text.Trim(),
                CommandText = txtCommand.Text,
                TimeoutSeconds = (int)nudTimeout.Value,
                DesktopConnectionId = desktopConnectionId
            };

            // Gửi yêu cầu tới Server và chờ phản hồi.
            // Dùng await ở đây vì server không thể nào mà phản hồi lập tức được
            // Chờ Server xử lý xong thì nó sẽ thực hiện tiếp lệnh bên dưới
            // Nhưng mà trong thời gian chờ server vẫn hoàn toàn có thể thao tác trên giao diện
            // Chính xác hơn là do Winforms luồng chính chuyênxử lý giao diện
            // await giúp luồng đó được rảnh trong thời gian chờ phản hồi

            // [4.2] Gửi token từ Desktop
            // Metadata là class có sẵn của thư viện gRPC, thuộc namespace Grpc.Core và trước đó có using Grpc.Core ở trên rồi.
            // Class này chứa một tập hợp các mục dạng tên–giá trị, dùng để trao đổi thông tin đi kèm lời gọi gRPC.
            // https://grpc.github.io/grpc/csharp/api/Grpc.Core.Metadata.html
            // Ở đây tạo một đối tượng rồi giữ nó trong biến metadata.
            var metadata = new Metadata();

            // Phương thức Add thêm một mục vào đối tượng vừa tạo:
            //
            metadata.Add("authorization", $"Bearer {_accessToken}");

            var response = await _commandClient.SubmitCommandAsync(
                // Agent nào, lệnh gì, timeout bao lâu, connection ID nhận output.
                request,
                // Token để Server xác thực người gửi yêu cầu.
                // Đây là cách truyền đối số bằng tên:
                // headers: là tên tham số của SubmitCommandAsync.
                // metadata là biến chứa đối tượng chúng ta truyền vào tham số đó.
                headers: metadata,
                deadline: DateTime.UtcNow.AddSeconds(10));

            // Nếu Form đã bị giải phóng thì kết thúc hàm, không cập nhật giao diện nữa.
            if (IsDisposed)
                return;

            SetStatus($"Submitted: {response.CommandId}");
            if (badgeOutput.Text == "Submitting command")
                badgeOutput.Text = "Waiting for output";
        }
        catch (RpcException ex)
        {
            if (IsDisposed)
                return;

            SetStatus($"{ex.StatusCode}: {ex.Status.Detail}", true);
            badgeOutput.State = AntdUI.TState.Error;
            badgeOutput.Text = "Submission failed";
        }
        finally
        {
            if (!IsDisposed)
            {
                btnSend.Loading = false;
                btnSend.Enabled = _hubConnection.State == HubConnectionState.Connected;
            }
        }
    }

    // Phần giải phóng _hubConnection
    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        _channel.Dispose();

        try
        {
            await _hubConnection.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void txtResult_TextChanged(object sender, EventArgs e)
    {

    }
}
