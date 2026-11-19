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

    // Tạo duy nhất 1 bộ gọi API Metrics dùng chung trong suốt thời gian Form1 mở.
    // Vì sao không tạo mới (new MetricsApiClient) mỗi lần bấm mở cửa sổ chọn Agent?
    // Vì bên trong MetricsApiClient có giữ một HttpClient (quản lý các kết nối mạng/socket).
    // Nếu mỗi lần mở cửa sổ lại new mới mà không giải phóng thì sẽ gây rò rỉ kết nối mạng (socket leak).
    private readonly MetricsApiClient _metricsClient;

    // SemaphoreSlim(1, 1) là gì?
    // Hiểu đơn giản: đây là một "ổ khóa" chỉ cho phép tối đa 1 luồng (1 người) đi qua cùng lúc.
    // Vì sao cần ổ khóa này khi làm mới (refresh) token?
    // Giả sử đúng lúc Access Token sắp hết hạn, cả SignalR và nút tải Metrics cùng gọi xin token 1 lúc:
    //   - Không có khóa: cả 2 luồng cùng gọi Keycloak xin gia hạn token -> tốn mạng, dễ đè token nhau.
    //   - Có khóa (1, 1): luồng 1 vào gọi Keycloak làm mới xong, luồng 2 đứng đợi;
    //     tới lượt luồng 2 vào thấy token đã mới rồi thì lấy dùng luôn, không gọi Keycloak lần 2.
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // Bộ 4 thông tin phiên đăng nhập hiện tại (nullable ? vì lúc mới mở app thì chưa đăng nhập):
    // - _accessToken: "vé vào cửa" gửi kèm lên NetOps.Server để chứng minh đã đăng nhập.
    //   Trên Keycloak, vé này chỉ sống 5 phút (300 giây) để giảm rủi ro nếu bị lộ.
    // - _refreshToken: "vé đổi vé mới" có hạn dài hơn (mặc định 30 phút không hoạt động).
    //   Khi _accessToken sắp hết hạn, app tự lấy _refreshToken gọi ngầm Keycloak đổi lấy _accessToken mới
    //   mà người dùng KHÔNG cần đăng nhập lại.
    // - _identityToken (id_token): chứa thông tin định danh phiên SSO, gửi kèm cho Keycloak lúc Logout
    //   để Keycloak biết chính xác phải đăng xuất phiên nào (không hỏi lại người dùng).
    // - _accessTokenExpiresAt: mốc thời gian _accessToken sẽ hết hạn, để biết khi nào cần tự gia hạn.
    private string? _accessToken;
    private string? _refreshToken;
    private string? _identityToken;
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
            // Nơi nhận kết quả sau khi Login xong (trình duyệt gọi về mini server ở cổng 7890)
            RedirectUri = "http://127.0.0.1:7890/",
            // Nơi Keycloak chuyển trình duyệt về sau khi Logout xong.
            // Bắt buộc phải khai báo dòng này (và bật tương ứng trên Keycloak client) thì _oidcClient.LogoutAsync()
            // mới biết mở mini server ở địa chỉ nào để nhận tín hiệu "đã đăng xuất SSO thành công".
            PostLogoutRedirectUri = "http://127.0.0.1:7890/",
            // Thông tin yêu cầu: đăng nhập, hồ sơ cơ bản và email
            // Danh sách scope này phục vụ việc yêu cầu thông tin trong quy trình đăng nhập.
            Scope = "openid profile email",
            // Khi cần mở trình duyệt để đăng nhập, hãy sử dụng đối tượng SystemBrowser này.
            Browser = new SystemBrowser()
        };

        // Tạo công cụ đăng nhập/đăng xuất/gia hạn token với Keycloak từ cấu hình trên.
        _oidcClient = new OidcClient(options);

        // Khởi tạo 1 MetricsApiClient dùng chung cho toàn app.
        // Truyền hàm GetValidAccessTokenAsync vào làm "nhà cung cấp token":
        // mỗi lần gọi API Metrics, nó sẽ gọi hàm này để luôn lấy được token CÒN HẠN (tự refresh nếu cần).
        _metricsClient = new MetricsApiClient(ServerBaseUrl, GetValidAccessTokenAsync);

        // Tạo cấu hình kết nối SignalR tới Server (chưa kết nối thật cho tới khi gọi StartAsync()).
        // AccessTokenProvider dùng chung hàm GetValidAccessTokenAsync():
        //   Constructor chạy -> đăng ký hàm lấy token (lúc này chưa gọi)
        //          ↓
        //   Đăng nhập xong -> lưu _accessToken, _refreshToken, _accessTokenExpiresAt
        //          ↓
        //   Connect / Reconnect -> SignalR tự gọi GetValidAccessTokenAsync()
        //                       -> nếu token sắp hết hạn thì tự gia hạn trước khi gửi lên Server.
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



            // Lưu toàn bộ thông tin phiên vừa nhận từ Keycloak sau khi đăng nhập thành công:
            // - AccessToken + AccessTokenExpiration: dùng gửi lên Server và canh giờ tự gia hạn.
            // - RefreshToken: dùng xin AccessToken mới sau mỗi ~5 phút.
            // - IdentityToken: dùng gửi lại cho Keycloak lúc bấm Logout để xóa phiên SSO.
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
        // Hỏi xác nhận trước để tránh người dùng bấm nhầm nút Logout.
        var confirm = MessageBox.Show(
            this,
            "Bạn có chắc chắn muốn đăng xuất khỏi NetOps không?",
            "Xác nhận đăng xuất",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        // 1. Copy tạm id_token ra biến cục bộ rồi xóa sạch toàn bộ token trong Form NGAY LẬP TỨC.
        // Vì sao phải xóa trước khi gọi StopAsync() và LogoutAsync()?
        // - Khi gọi _hubConnection.StopAsync(), sự kiện OnConnectionClosedAsync sẽ kích hoạt.
        //   Vì _accessToken đã là null, OnConnectionClosedAsync biết đây là chủ động đăng xuất
        //   nên sẽ bỏ qua, không ghi đè trạng thái thành "Disconnected" màu đỏ.
        // - Nếu đúng lúc đó có luồng nào đang tự refresh token ngầm, việc _refreshToken bị xóa về null
        //   sẽ làm luồng refresh đó tự hủy, không ghi đè lại token cũ sau khi đã đăng xuất.
        var idToken = _identityToken;
        _accessToken = null;
        _refreshToken = null;
        _identityToken = null;
        _accessTokenExpiresAt = default;

        // 2. Khóa tất cả nút bấm và dọn sạch dữ liệu cũ trên màn hình trước khi chạy các thao tác mạng.
        // Đặc biệt phải khóa btnConnect trong lúc đang logout: nếu để sáng ngay mà người dùng bấm Sign In
        // trong khi trình duyệt logout vẫn đang chiếm cổng 7890 thì sẽ bị đụng cổng (port conflict).
        btnLogout.Enabled = false;
        btnConnect.Enabled = false;
        btnSend.Enabled = false;
        ResetUiAfterLogout();
        SetStatus("Logging out...");

        try
        {
            // 3. Ngắt kết nối SignalR khỏi Server.
            // Kiểm tra != Disconnected (thay vì chỉ == Connected) để ngắt được cả lúc đang Connecting/Reconnecting.
            if (_hubConnection.State != HubConnectionState.Disconnected)
                await _hubConnection.StopAsync();

            // 4. Đăng xuất phiên SSO trên trình duyệt (Keycloak).
            // Nếu chỉ xóa token trong app mà bỏ bước này, cookie đăng nhập trên trình duyệt vẫn còn sống,
            // lần sau bấm Sign In sẽ tự đăng nhập lại tài khoản cũ mà không đổi được tài khoản khác.
            if (!string.IsNullOrEmpty(idToken))
            {
                var result = await _oidcClient.LogoutAsync(new LogoutRequest
                {
                    // Gửi kèm id_token để Keycloak xác nhận đúng phiên cần đóng và tự redirect về cổng 7890.
                    IdTokenHint = idToken
                });

                // Nếu Keycloak báo lỗi (ví dụ sai PostLogoutRedirectUri hoặc mất mạng), hiển thị rõ cho người dùng biết.
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
            // Dù mạng rớt khiến bước gọi Keycloak lỗi, phiên cục bộ trên Desktop vẫn đã được xóa sạch.
            SetStatus($"Local sign-out done, error during SSO logout: {ex.Message}", true);
        }
        finally
        {
            // Khối finally LUÔN chạy dù try thành công, return sớm hay văng lỗi:
            // mở khóa lại nút và đổi chữ thành "Sign In" để người dùng có thể đăng nhập lại.
            if (!IsDisposed)
            {
                btnConnect.Text = "Sign In";
                btnConnect.Enabled = true;
            }
        }
    }

    // Gom phần dọn sạch màn hình sau khi đăng xuất ra hàm riêng để SignOutAsync gọn và dễ đọc.
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
        // Truyền lại đúng 1 instance _metricsClient dùng chung của Form1 (không new mới để tránh rò rỉ HttpClient).
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

    // Hàm trung tâm cung cấp Access Token CÒN HẠN cho cả SignalR lẫn MetricsApiClient.
    // Luồng hoạt động mỗi khi có nơi cần token:
    //   Chưa đăng nhập (_accessToken == null)        -> trả null
    //   Token vẫn còn hạn > 60 giây                  -> trả ngay _accessToken hiện tại (không gọi mạng)
    //   Token còn dưới 60 giây (hoặc đã hết hạn)     -> vào khóa _tokenLock -> gọi Keycloak xin token mới
    //                                                -> thành công: lưu token mới & trả về
    //                                                -> thất bại (hết phiên SSO): xóa phiên & báo đăng nhập lại
    private async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        // Chưa đăng nhập (hoặc vừa đăng xuất) thì không có token để gửi.
        if (string.IsNullOrEmpty(_accessToken))
            return null;

        // Trừ trước 60 giây (AddSeconds(-60)) làm khoảng an toàn:
        // ví dụ token sống 5 phút thì từ phút thứ 4 trở đi sẽ chủ động đi gia hạn sớm,
        // tránh trường hợp gửi lên tới Server mới vừa hết hạn dẫn đến lỗi 401.
        if (DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
            return _accessToken;

        // Chờ tới lượt đi qua "ổ khóa" để đảm bảo tại 1 thời điểm chỉ có duy nhất 1 request gọi refresh tới Keycloak.
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Kiểm tra lại lần 2 sau khi vào được trong khóa (kỹ thuật double-check):
            // vì trong lúc luồng này đứng chờ ở WaitAsync, có thể luồng khác vừa gia hạn xong rồi
            // (hoặc người dùng vừa bấm Logout xóa _accessToken). Nếu vậy thì trả luôn, không gọi Keycloak nữa.
            if (string.IsNullOrEmpty(_accessToken) || DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
                return _accessToken;

            // Lưu _refreshToken hiện tại vào biến tạm trước khi await gọi mạng.
            var currentRefreshToken = _refreshToken;
            if (string.IsNullOrEmpty(currentRefreshToken))
                return null;

            // Gọi ngầm tới Keycloak để đổi Refresh Token lấy Access Token mới (không cần mở trình duyệt).
            var refresh = await _oidcClient.RefreshTokenAsync(currentRefreshToken, cancellationToken: ct);

            // Chặn tình huống: trong lúc đang chờ Keycloak trả lời ở dòng trên, người dùng bấm nút Logout
            // (làm _refreshToken bị xóa về null hoặc đổi sang phiên khác). Khi đó phải bỏ qua kết quả này,
            // tuyệt đối không ghi đè lại _accessToken cũ lên trạng thái đã đăng xuất.
            if (_refreshToken != currentRefreshToken)
                return null;

            // Nếu Keycloak từ chối gia hạn (ví dụ quá 30 phút không dùng hoặc Admin đã thu hồi phiên):
            // xóa sạch phiên cục bộ và báo trên giao diện để người dùng bấm Sign In đăng nhập lại.
            if (refresh.IsError || string.IsNullOrEmpty(refresh.AccessToken))
            {
                _accessToken = null;
                _refreshToken = null;
                _accessTokenExpiresAt = default;

                // Vì hàm này có thể được gọi từ luồng nền (SignalR / HttpClient), phải dùng RunOnUiAsync
                // để đẩy thao tác cập nhật chữ & nút bấm về đúng luồng giao diện (UI thread).
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

            // Gia hạn thành công -> cập nhật Access Token mới và mốc hết hạn mới.
            _accessToken = refresh.AccessToken;
            _accessTokenExpiresAt = refresh.AccessTokenExpiration;

            // Keycloak có thể cấp Refresh Token / Identity Token mới khi refresh (hoặc để trống nếu giữ cái cũ),
            // nên chỉ ghi đè khi server thực sự trả về chuỗi không rỗng.
            if (!string.IsNullOrEmpty(refresh.RefreshToken))
                _refreshToken = refresh.RefreshToken;
            if (!string.IsNullOrEmpty(refresh.IdentityToken))
                _identityToken = refresh.IdentityToken;

            return _accessToken;
        }
        finally
        {
            // BẮT BUỘC mở khóa trong finally để dù thành công hay gặp lỗi mạng/hủy (CancellationToken),
            // các luồng khác cũng không bao giờ bị treo vĩnh viễn ở WaitAsync.
            _tokenLock.Release();
        }
    }

    // Giải phóng tài nguyên mạng (_metricsClient và _hubConnection) khi đóng cửa sổ chính.
    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        // Đóng HttpClient bên trong MetricsApiClient, hủy mọi request metrics còn dang dở.
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
