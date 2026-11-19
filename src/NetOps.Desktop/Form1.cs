namespace NetOps.Desktop;
using Duende.IdentityModel.OidcClient;
using Microsoft.AspNetCore.SignalR.Client;
using NetOps.Shared;

public partial class Form1 : AntdUI.Window
{
    private const string ServerBaseUrl = "https://api.r1antech.online";
    private const string KeycloakAuthority = "https://auth.r1antech.online/realms/netops";

    private List<AgentSummary> _agents = new();

    private readonly HubConnection _hubConnection;
    private readonly OidcClient _oidcClient;
    private readonly MetricsApiClient _metricsClient;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private string? _identityToken; // Lưu id_token_hint cho việc Logout
    private DateTimeOffset _accessTokenExpiresAt;
    private bool _connecting;
    private int _outputLineCount;
    private AntdUI.Button btnLogout;

    public Form1()
    {
        InitializeComponent();
        btnLogout = new AntdUI.Button
        {
            Text = "Logout",
            Radius = 7,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BorderWidth = 1F,
            DefaultBack = Color.FromArgb(254, 242, 242),
            DefaultBorderColor = Color.FromArgb(254, 202, 202),
            ForeColor = Color.FromArgb(185, 28, 28),
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            Enabled = false
        };
        btnLogout.Click += btnLogout_Click;
        headingLayout.ColumnStyles[1].Width = 390F;
        connectionLayout.ColumnCount = 3;
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
        connectionLayout.Controls.Add(btnLogout, 2, 0);
        connectionLayout.SetRowSpan(btnLogout, 2);
        // Tạo một đối tượng chứa cấu hình đăng nhập, rồi giữ nó trong biến options.
        var options = new OidcClientOptions
        {
            // Nơi đăng nhập: Keycloak, realm netops
            Authority = KeycloakAuthority,
            // Ứng dụng yêu cầu: netops-desktop
            ClientId = "netops-desktop",
            // Nơi nhận kết quả
            RedirectUri = "http://127.0.0.1:7890/",
            PostLogoutRedirectUri = "http://127.0.0.1:7890/",
            // Thông tin yêu cầu: đăng nhập, hồ sơ cơ bản và email
            // Danh sách scope này phục vụ việc yêu cầu thông tin trong quy trình đăng nhập.
            Scope = "openid profile email",
            // Khi cần mở trình duyệt để đăng nhập, hãy sử dụng đối tượng SystemBrowser này.
            Browser = new SystemBrowser()
        };

        _oidcClient = new OidcClient(options);
        _metricsClient = new MetricsApiClient(ServerBaseUrl, GetValidAccessTokenAsync);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{ServerBaseUrl}/hubs/netops", connectionOptions =>
            {
                connectionOptions.AccessTokenProvider = () => GetValidAccessTokenAsync();
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
        await SignInAsync();
    }
    private async Task SignInAsync()
    {
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



            // Lưu bộ token sau khi đăng nhập thành công
            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _identityToken = result.IdentityToken;
            _accessTokenExpiresAt = result.AccessTokenExpiration;

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
            // Bật các nút sau khi đăng nhập thành công
            btnConnect.Enabled = true;
            if (btnLogout != null)
                btnLogout.Enabled = true;
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
        
      private async void btnLogout_Click(object? sender, EventArgs e)
    {
        await SignOutAsync();
    }
    private async Task SignOutAsync()
    {
        var confirm = MessageBox.Show(
            this,
            "Bạn có chắc chắn muốn đăng xuất khỏi NetOps không?",
            "Xác nhận đăng xuất",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        // 1. Giữ tạm id_token rồi xóa phiên cục bộ ngay
        var idToken = _identityToken;
        _accessToken = null;
        _refreshToken = null;
        _identityToken = null;
        _accessTokenExpiresAt = default;

        // 2. Khóa các nút và dọn giao diện trước khi gọi mạng
        btnLogout.Enabled = false;
        btnConnect.Enabled = false;
        btnSend.Enabled = false;
        ResetUiAfterLogout();
        SetStatus("Logging out...");

        try
        {
            // 3. Ngắt SignalR (kể cả khi đang Connecting/Reconnecting)
            if (_hubConnection.State != HubConnectionState.Disconnected)
                await _hubConnection.StopAsync();

            // 4. Đăng xuất phiên SSO trên Keycloak
            if (!string.IsNullOrEmpty(idToken))
            {
                var result = await _oidcClient.LogoutAsync(new LogoutRequest
                {
                    IdTokenHint = idToken
                });

                if (result.IsError)
                {
                    SetStatus($"Local sign-out done, Keycloak logout failed: {result.Error}", true);
                    return;
                }
            }

            SetStatus("Logged out successfully. Click 'Sign In' to log in again.");
        }
        catch (Exception ex)
        {
            SetStatus($"Local sign-out done, error during SSO logout: {ex.Message}", true);
        }
        finally
        {
            if (!IsDisposed)
            {
                btnConnect.Text = "Sign In";
                btnConnect.Enabled = true;
            }
        }
    }

    private void ResetUiAfterLogout()
    {
        _agents.Clear();
        txtAgentId.Text = string.Empty;
        txtCommand.Text = string.Empty;
        txtResult.Clear();
        _outputLineCount = 0;
        UpdateOutputControls();

        badgeConnection.Text = "Logged out";
        badgeConnection.State = AntdUI.TState.Default;
        badgeOutput.Text = "Waiting for output";
        badgeOutput.State = AntdUI.TState.Default;
    }
    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            await SignInAsync();
        }
        else
        {
            await ConnectToServerAsync();
        }
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
            if (string.IsNullOrEmpty(_accessToken))
                return;
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
        window.MetricsClient = _metricsClient;
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

        if (_hubConnection.State != HubConnectionState.Connected)
        {
            SetStatus("Please connect to the server before sending.", true);
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
            var request = new SubmitCommandRequest
            {
                AgentId = txtAgentId.Text.Trim(),
                CommandText = txtCommand.Text,
                TimeoutSeconds = (int)nudTimeout.Value
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _hubConnection.InvokeAsync<SubmitCommandResponse>(
                "SubmitCommand",
                request,
                timeoutCts.Token);

            if (IsDisposed)
                return;

            SetStatus($"Submitted: {response.CommandId}");
            if (badgeOutput.Text == "Submitting command")
                badgeOutput.Text = "Waiting for output";
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed)
                return;

            SetStatus("Command submission timed out.", true);
            badgeOutput.State = AntdUI.TState.Error;
            badgeOutput.Text = "Submission failed";
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;

            SetStatus($"Submit failed: {ex.Message}", true);
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

    // Tự động làm mới Access Token khi còn dưới 60 giây là hết hạn.
    private async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_accessToken))
            return null;

        if (DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
                return _accessToken;

            var currentRefreshToken = _refreshToken;
            if (string.IsNullOrEmpty(currentRefreshToken))
                return null;

            var refresh = await _oidcClient.RefreshTokenAsync(currentRefreshToken, cancellationToken: ct);
            if (_refreshToken != currentRefreshToken)
                return null; // Đã đăng xuất trong lúc đang làm mới token

            if (refresh.IsError || string.IsNullOrEmpty(refresh.AccessToken))
            {
                _accessToken = null;
                _refreshToken = null;
                _accessTokenExpiresAt = default;
                _ = RunOnUiAsync(() =>
                {
                    SetStatus("Session expired. Please sign in again.", true);
                    if (_hubConnection.State == HubConnectionState.Disconnected)
                    {
                        btnConnect.Text = "Sign In";
                        btnConnect.Enabled = true;
                        btnLogout.Enabled = false;
                    }
                });
                return null;
            }

            _accessToken = refresh.AccessToken;
            _accessTokenExpiresAt = refresh.AccessTokenExpiration;
            if (!string.IsNullOrEmpty(refresh.RefreshToken))
                _refreshToken = refresh.RefreshToken;
            if (!string.IsNullOrEmpty(refresh.IdentityToken))
                _identityToken = refresh.IdentityToken;

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // Giải phóng tài nguyên khi đóng Form
    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _metricsClient.Dispose();

        try
        {
            await _hubConnection.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
