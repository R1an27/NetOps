using Client.WinForms.Services;
using ServerManager.Shared.Contracts.Models;
using ServerManager.Shared.Contracts.Payloads;

namespace Client.WinForms.Forms;

public partial class MainForm : Form
{
    private readonly NetOpsClientService _clientService;
    private readonly System.Windows.Forms.Timer _autoHeartbeatTimer;
    private int _sentCount;
    private int _recvCount;
    private FrameLogEntry? _selectedFrameEntry;

    public MainForm()
    {
        InitializeComponent();

        _clientService = new NetOpsClientService();
        _clientService.AutoReconnectEnabled = chkAutoReconnect.Checked;
        chkAutoReconnect.CheckedChanged += (_, _) => _clientService.AutoReconnectEnabled = chkAutoReconnect.Checked;

        _clientService.DetailedConnectionStateChanged += OnDetailedConnectionStateChanged;
        _clientService.FrameLogged += OnFrameLogged;
        _clientService.MessageReceived += OnMessageReceived;

        _autoHeartbeatTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 // 5 giây gửi nhịp tim 1 lần
        };
        _autoHeartbeatTimer.Tick += async (_, _) => await SendAutoHeartbeatAsync().ConfigureAwait(false);

        // Đăng ký sự kiện kết nối & chẩn đoán
        btnConnect.Click += async (_, _) => await HandleConnectAsync().ConfigureAwait(false);
        btnDisconnect.Click += async (_, _) => await HandleDisconnectAsync().ConfigureAwait(false);
        btnPing.Click += async (_, _) => await HandlePingAsync().ConfigureAwait(false);
        btnHeartbeat.Click += async (_, _) => await HandleHeartbeatAsync().ConfigureAwait(false);
        chkAutoHeartbeat.CheckedChanged += OnAutoHeartbeatCheckedChanged;

        // Đăng ký sự kiện thực thi lệnh Command & Terminal
        btnExecuteCommand.Click += async (_, _) => await HandleExecuteCommandAsync().ConfigureAwait(false);
        btnGenNewCmdId.Click += (_, _) => GenerateNewCommandId();
        btnQuickWhoami.Click += (_, _) => SetQuickCommand("whoami");
        btnQuickHostname.Click += (_, _) => SetQuickCommand("hostname");
        btnQuickIpconfig.Click += (_, _) => SetQuickCommand("ipconfig");
        btnQuickDir.Click += (_, _) => SetQuickCommand("dir /b");
        btnClearTerminal.Click += (_, _) => ClearTerminal();

        // Đăng ký sự kiện Protocol Frame Inspector
        dgvFrames.SelectionChanged += OnDgvFramesSelectionChanged;
        btnClearInspector.Click += (_, _) => ClearInspector();
        btnCopyJson.Click += (_, _) => CopyCurrentFrameJson();

        // Đăng ký sự kiện kiểm thử sự cố mạng & Phục hồi (T19.3)
        btnSimulateDrop.Click += (_, _) => HandleSimulateClientDrop();
        btnSimulateClientDrop.Click += (_, _) => HandleSimulateClientDrop();
        btnSimulateSilence.Click += (_, _) => HandleSimulateSilence();
        btnSimulateRemoteAgentDrop.Click += async (_, _) => await HandleSimulateRemoteAgentDropAsync().ConfigureAwait(false);
        btnSimulateFuzzing.Click += async (_, _) => await HandleSimulateFuzzingAsync().ConfigureAwait(false);
        btnClearResilienceLog.Click += (_, _) => ClearResilienceLog();
        btnCopyResilienceLog.Click += (_, _) => CopyResilienceLog();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _autoHeartbeatTimer.Stop();
        _clientService.Dispose();
        base.OnFormClosing(e);
    }

    #region Connection Event Handlers

    private async Task HandleConnectAsync()
    {
        string host = txtHost.Text.Trim();
        int port = (int)numPort.Value;

        if (string.IsNullOrEmpty(host))
        {
            MessageBox.Show(this, "Vui lòng nhập địa chỉ Host/IP hợp lệ.", "Lỗi nhập liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            btnConnect.Enabled = false;
            lblConnectionStatus.Text = $"Đang kết nối tới {host}:{port}...";

            await _clientService.ConnectAsync(host, port).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeInvoke(() =>
            {
                btnConnect.Enabled = true;
                lblConnectionStatus.Text = $"Kết nối thất bại: {ex.Message}";
                MessageBox.Show(this, $"Không thể kết nối tới {host}:{port}\n\nLỗi: {ex.Message}", "Lỗi kết nối", MessageBoxButtons.OK, MessageBoxIcon.Error);
            });
        }
    }

    private async Task HandleDisconnectAsync()
    {
        try
        {
            btnDisconnect.Enabled = false;
            await _clientService.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeInvoke(() =>
            {
                lblConnectionStatus.Text = $"Lỗi khi ngắt kết nối: {ex.Message}";
            });
        }
    }

    private void OnDetailedConnectionStateChanged(ConnectionStateChangedEventArgs e)
    {
        UpdateConnectionStateUi(e);
    }

    /// <summary>
    /// Giai đoạn T18.3: Cập nhật giao diện người dùng tương ứng với 4 trạng thái kết nối
    /// (Connected, Connecting, Reconnecting, Disconnected).
    /// Đảm bảo nguyên tắc an toàn đa luồng (Thread-safe) tuyệt đối thông qua SafeInvoke (BeginInvoke).
    /// </summary>
    private void UpdateConnectionStateUi(ConnectionStateChangedEventArgs e)
    {
        SafeInvoke(() =>
        {
            switch (e.NewState)
            {
                case ConnectionState.Connected:
                    // 1. Trạng thái Connected: Badge xanh lục Emerald (Đã kết nối)
                    pnlStatusBadge.BackColor = Color.FromArgb(16, 185, 129);
                    lblStatusBadge.Text = "CONNECTED";
                    lblStatusBadge.ForeColor = Color.White;

                    // Phân quyền điều khiển: Khóa nút Connect, cho phép Disconnect, mở toàn quyền kiểm tra Ping/Heartbeat/Command
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                    btnSimulateDrop.Enabled = true;
                    btnSimulateClientDrop.Enabled = true;
                    btnPing.Enabled = true;
                    btnHeartbeat.Enabled = true;
                    chkAutoHeartbeat.Enabled = true;
                    btnExecuteCommand.Enabled = true;
                    txtHost.Enabled = false;
                    numPort.Enabled = false;

                    lblConnectionStatus.Text = string.IsNullOrEmpty(e.Message)
                        ? $"Đã kết nối thành công tới {_clientService.ConnectedEndpoint}"
                        : e.Message;
                    lblConnectionStatus.ForeColor = Color.FromArgb(16, 185, 129);
                    statusLabel.Text = "Connected";
                    statusEndpoint.Text = $" | Endpoint: {_clientService.ConnectedEndpoint}";
                    LogResilience("CONNECTED", $"[{DateTime.Now:HH:mm:ss.fff}] Kết nối TCP đã thiết lập thành công tới {_clientService.ConnectedEndpoint}", Color.FromArgb(52, 211, 153));
                    break;

                case ConnectionState.Connecting:
                    // 2. Trạng thái Connecting: Badge vàng hổ phách Amber (Đang bắt tay kết nối)
                    pnlStatusBadge.BackColor = Color.FromArgb(245, 158, 11);
                    lblStatusBadge.Text = "CONNECTING";
                    lblStatusBadge.ForeColor = Color.White;

                    // Khóa mọi nút bấm để tránh người dùng thao tác trùng lặp trong khi đang bắt tay mạng
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = false;
                    btnSimulateDrop.Enabled = false;
                    btnSimulateClientDrop.Enabled = false;
                    btnPing.Enabled = false;
                    btnHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Enabled = false;
                    btnExecuteCommand.Enabled = false;
                    txtHost.Enabled = false;
                    numPort.Enabled = false;

                    lblConnectionStatus.Text = string.IsNullOrEmpty(e.Message)
                        ? "Đang thiết lập kết nối TCP..."
                        : e.Message;
                    lblConnectionStatus.ForeColor = Color.FromArgb(217, 119, 6);
                    statusLabel.Text = "Connecting...";
                    statusEndpoint.Text = $" | Endpoint: {_clientService.ConnectedEndpoint ?? "Connecting..."}";
                    LogResilience("CONNECTING", $"[{DateTime.Now:HH:mm:ss.fff}] Đang bắt tay kết nối TCP...", Color.FromArgb(251, 191, 36));
                    break;

                case ConnectionState.Reconnecting:
                    // 3. Trạng thái Reconnecting: Badge cam cảnh báo Deep Orange (Mất mạng, đang tự thử lại)
                    pnlStatusBadge.BackColor = Color.FromArgb(234, 88, 12);
                    lblStatusBadge.Text = e.Attempt > 0 ? $"RECONNECT ({e.Attempt})" : "RECONNECTING";
                    lblStatusBadge.ForeColor = Color.White;

                    // Khóa Connect, cho phép Disconnect để người dùng có thể chủ động hủy kết nối lại nếu muốn
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                    btnSimulateDrop.Enabled = false;
                    btnSimulateClientDrop.Enabled = false;
                    btnPing.Enabled = false;
                    btnHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Enabled = false;
                    btnExecuteCommand.Enabled = false;
                    txtHost.Enabled = false;
                    numPort.Enabled = false;

                    string retryDelayText = e.RetryDelay.HasValue ? $" (Chờ {e.RetryDelay.Value.TotalSeconds:F0}s)" : "";
                    lblConnectionStatus.Text = string.IsNullOrEmpty(e.Message)
                        ? $"Mất kết nối mạng. Đang tự động kết nối lại{retryDelayText}..."
                        : $"{e.Message}{retryDelayText}";
                    lblConnectionStatus.ForeColor = Color.FromArgb(234, 88, 12);
                    statusLabel.Text = e.Attempt > 0 ? $"Reconnecting (Attempt {e.Attempt})..." : "Reconnecting...";
                    statusEndpoint.Text = $" | Target: {_clientService.ConnectedEndpoint ?? "Waiting..."}";
                    LogResilience("RECONNECTING", $"[{DateTime.Now:HH:mm:ss.fff}] Phát hiện mất mạng! Đang tự động kết nối lại lần {e.Attempt}{retryDelayText}. Lý do: {e.Message}", Color.FromArgb(251, 146, 60));
                    break;

                case ConnectionState.Disconnected:
                default:
                    // 4. Trạng thái Disconnected: Badge xám đá Slate Gray (Chưa kết nối / Đã ngắt)
                    pnlStatusBadge.BackColor = Color.FromArgb(100, 116, 139);
                    lblStatusBadge.Text = "DISCONNECTED";
                    lblStatusBadge.ForeColor = Color.White;

                    // Mở lại các điều khiển Host, Port, nút Connect cho phép người dùng cấu hình lại
                    btnConnect.Enabled = true;
                    btnDisconnect.Enabled = false;
                    btnSimulateDrop.Enabled = false;
                    btnSimulateClientDrop.Enabled = false;
                    btnPing.Enabled = false;
                    btnHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Checked = false;
                    btnExecuteCommand.Enabled = false;
                    _autoHeartbeatTimer.Stop();
                    txtHost.Enabled = true;
                    numPort.Enabled = true;

                    lblConnectionStatus.Text = string.IsNullOrEmpty(e.Message)
                        ? "Chưa kết nối máy chủ"
                        : e.Message;
                    lblConnectionStatus.ForeColor = Color.FromArgb(100, 116, 139);
                    statusLabel.Text = "Disconnected";
                    statusEndpoint.Text = " | Endpoint: Not connected";
                    LogResilience("DISCONNECTED", $"[{DateTime.Now:HH:mm:ss.fff}] Đã ngắt kết nối. Thông điệp: {e.Message}", Color.FromArgb(148, 163, 184));
                    break;
            }
        });
    }

    #endregion

    #region Diagnostics: Ping / Pong & Heartbeat

    private async Task HandlePingAsync()
    {
        if (!_clientService.IsConnected) return;

        string payload = txtPingPayload.Text.Trim();
        SafeInvoke(() =>
        {
            btnPing.Enabled = false;
            lblPingResult.Text = "Đang gửi Ping và chờ Pong...";
        });

        try
        {
            var (rtt, response) = await _clientService.PingAsync(payload).ConfigureAwait(false);

            SafeInvoke(() =>
            {
                lblPingResult.Text = $"✔ Pong nhận được! RTT: {rtt.TotalMilliseconds:F1} ms | Payload: '{response.Payload}'";
                lblPingResult.ForeColor = Color.FromArgb(22, 163, 74); // Green
            });
        }
        catch (Exception ex)
        {
            SafeInvoke(() =>
            {
                lblPingResult.Text = $"✖ Ping thất bại: {ex.Message}";
                lblPingResult.ForeColor = Color.FromArgb(220, 38, 38); // Red
            });
        }
        finally
        {
            SafeInvoke(() =>
            {
                if (_clientService.IsConnected) btnPing.Enabled = true;
            });
        }
    }

    private async Task HandleHeartbeatAsync()
    {
        if (!_clientService.IsConnected) return;

        string agentId = txtAgentId.Text.Trim();
        if (string.IsNullOrEmpty(agentId)) agentId = "admin-client-01";

        SafeInvoke(() =>
        {
            btnHeartbeat.Enabled = false;
            lblHeartbeatResult.Text = "Đang gửi Heartbeat...";
        });

        try
        {
            var (ack, response) = await _clientService.SendHeartbeatAsync(agentId).ConfigureAwait(false);

            SafeInvoke(() =>
            {
                if (ack != null && ack.Acknowledged)
                {
                    lblHeartbeatResult.Text = $"✔ HeartbeatAck xác nhận thành công! Server Time: {DateTimeOffset.FromUnixTimeMilliseconds(ack.Timestamp):HH:mm:ss.fff}";
                    lblHeartbeatResult.ForeColor = Color.FromArgb(22, 163, 74);
                }
                else
                {
                    lblHeartbeatResult.Text = $"Nhận phản hồi nhưng HeartbeatAck không hợp lệ (Type: {response.MessageType})";
                    lblHeartbeatResult.ForeColor = Color.FromArgb(202, 138, 4);
                }
            });
        }
        catch (Exception ex)
        {
            SafeInvoke(() =>
            {
                lblHeartbeatResult.Text = $"✖ Gửi Heartbeat thất bại: {ex.Message}";
                lblHeartbeatResult.ForeColor = Color.FromArgb(220, 38, 38);
            });
        }
        finally
        {
            SafeInvoke(() =>
            {
                if (_clientService.IsConnected) btnHeartbeat.Enabled = true;
            });
        }
    }

    private void OnAutoHeartbeatCheckedChanged(object? sender, EventArgs e)
    {
        if (chkAutoHeartbeat.Checked)
        {
            _autoHeartbeatTimer.Start();
            lblHeartbeatResult.Text = "Auto Heartbeat đang hoạt động (mỗi 5s)...";
        }
        else
        {
            _autoHeartbeatTimer.Stop();
        }
    }

    private async Task SendAutoHeartbeatAsync()
    {
        if (!_clientService.IsConnected)
        {
            _autoHeartbeatTimer.Stop();
            return;
        }

        string agentId = string.Empty;
        SafeInvoke(() => agentId = txtAgentId.Text.Trim());
        if (string.IsNullOrEmpty(agentId)) agentId = "admin-client-01";

        try
        {
            var (ack, _) = await _clientService.SendHeartbeatAsync(agentId).ConfigureAwait(false);
            SafeInvoke(() =>
            {
                if (ack != null && ack.Acknowledged)
                {
                    lblHeartbeatResult.Text = $"✔ [Auto {DateTime.Now:HH:mm:ss}] HeartbeatAck: OK | Server Time: {DateTimeOffset.FromUnixTimeMilliseconds(ack.Timestamp):HH:mm:ss.fff}";
                    lblHeartbeatResult.ForeColor = Color.FromArgb(22, 163, 74);
                }
            });
        }
        catch (Exception ex)
        {
            SafeInvoke(() =>
            {
                lblHeartbeatResult.Text = $"✖ [Auto {DateTime.Now:HH:mm:ss}] Lỗi nhịp tim: {ex.Message}";
                lblHeartbeatResult.ForeColor = Color.FromArgb(220, 38, 38);
            });
        }
    }

    #endregion

    #region Packet Logging & Statistics

    private void OnFrameLogged(FrameLogEntry entry)
    {
        if (entry.Direction == FrameDirection.Outbound)
        {
            Interlocked.Increment(ref _sentCount);
        }
        else
        {
            Interlocked.Increment(ref _recvCount);
        }

        SafeInvoke(() =>
        {
            lblStatsSent.Text = $"Số gói tin đã gửi (OUT): {_sentCount}";
            lblStatsRecv.Text = $"Số gói tin đã nhận (IN): {_recvCount}";
            lblStatsLastSeen.Text = $"Hoạt động gần nhất: {entry.Timestamp:HH:mm:ss.fff} ({entry.Direction} - {entry.MessageType})";

            AddFrameToInspector(entry);
        });
    }

    private void OnMessageReceived(NetOpsMessage message)
    {
        if (message.MessageType == MessageType.CommandOutput)
        {
            var outputPayload = message.GetPayload<CommandOutputPayload>();
            if (outputPayload != null)
            {
                var color = outputPayload.IsError 
                    ? Color.FromArgb(248, 113, 113) // Red-400 (stderr)
                    : Color.FromArgb(226, 232, 240); // Slate-200 (stdout)
                AppendTerminalText(outputPayload.Output, color);
            }
        }
        else if (message.MessageType == MessageType.CommandDone)
        {
            var donePayload = message.GetPayload<CommandDonePayload>();
            if (donePayload != null)
            {
                var isSuccess = donePayload.ExitCode == 0;
                var color = isSuccess 
                    ? Color.FromArgb(52, 211, 153)  // Emerald-400
                    : Color.FromArgb(251, 146, 60); // Orange-400
                
                AppendTerminalText(
                    $"\n<<< [{DateTime.Now:HH:mm:ss.fff}] [DONE] Lệnh [{donePayload.CommandId}] hoàn tất | ExitCode: {donePayload.ExitCode} | Thời gian: {donePayload.TotalExecutionTimeMs} ms\n", 
                    color);

                SafeInvoke(() =>
                {
                    lblTerminalStatus.Text = $"Hoàn tất lệnh [{donePayload.CommandId}] với ExitCode: {donePayload.ExitCode} ({donePayload.TotalExecutionTimeMs} ms)";
                });
            }
        }
        else if (message.MessageType == MessageType.Error)
        {
            AppendTerminalText($"\n<<< [{DateTime.Now:HH:mm:ss.fff}] [SERVER ERROR] {message.Payload}\n", Color.FromArgb(248, 113, 113));
            SafeInvoke(() => lblTerminalStatus.Text = "Nhận thông báo lỗi từ server");
        }
    }

    #endregion

    #region Remote Command Center Handlers

    private async Task HandleExecuteCommandAsync()
    {
        if (!_clientService.IsConnected)
        {
            MessageBox.Show(this, "Chưa kết nối tới máy chủ. Vui lòng kết nối trước khi thực thi lệnh.", "Chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string commandLine = string.Empty;
        string commandId = string.Empty;
        int timeoutSeconds = 30;

        SafeInvoke(() =>
        {
            commandLine = txtCommandLine.Text.Trim();
            commandId = txtCommandId.Text.Trim();
            if (string.IsNullOrEmpty(commandId))
            {
                commandId = $"cmd-{Guid.NewGuid().ToString("N")[..6]}";
                txtCommandId.Text = commandId;
            }
            timeoutSeconds = (int)numCommandTimeout.Value;
        });

        if (string.IsNullOrEmpty(commandLine))
        {
            MessageBox.Show(this, "Vui lòng nhập chuỗi lệnh shell cần thực thi.", "Lệnh rỗng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SafeInvoke(() =>
        {
            btnExecuteCommand.Enabled = false;
            lblTerminalStatus.Text = $"Đang gửi và thực thi lệnh [{commandId}]...";
        });

        AppendTerminalText(
            $"\n>>> [{DateTime.Now:HH:mm:ss.fff}] [CMD] Gửi lệnh: {commandLine} (ID: {commandId}, Timeout: {timeoutSeconds}s)\n", 
            Color.FromArgb(96, 165, 250)); // Blue-400

        try
        {
            var payload = new CommandPayload(commandId, commandLine, timeoutSeconds);
            var message = NetOpsMessage.CreateRequest(MessageType.Command, payload, requestId: commandId);

            await _clientService.SendMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendTerminalText(
                $"✖ [{DateTime.Now:HH:mm:ss.fff}] [ERROR] Lỗi khi gửi lệnh [{commandId}]: {ex.Message}\n", 
                Color.FromArgb(248, 113, 113)); // Red-400
            
            SafeInvoke(() =>
            {
                lblTerminalStatus.Text = $"Lỗi gửi lệnh: {ex.Message}";
            });
        }
        finally
        {
            SafeInvoke(() =>
            {
                if (_clientService.IsConnected)
                {
                    btnExecuteCommand.Enabled = true;
                }
            });
        }
    }

    private void GenerateNewCommandId()
    {
        SafeInvoke(() =>
        {
            txtCommandId.Text = $"cmd-{Guid.NewGuid().ToString("N")[..6]}";
        });
    }

    private void SetQuickCommand(string cmd)
    {
        SafeInvoke(() =>
        {
            txtCommandLine.Text = cmd;
            GenerateNewCommandId();
        });
    }

    private void ClearTerminal()
    {
        SafeInvoke(() =>
        {
            rtbTerminal.Clear();
            lblTerminalStatus.Text = "Terminal đã được xóa";
        });
    }

    private void AppendTerminalText(string text, Color color)
    {
        SafeInvoke(() =>
        {
            if (rtbTerminal.IsDisposed) return;

            rtbTerminal.SelectionStart = rtbTerminal.TextLength;
            rtbTerminal.SelectionLength = 0;
            rtbTerminal.SelectionColor = color;
            rtbTerminal.AppendText(text);
            rtbTerminal.SelectionColor = rtbTerminal.ForeColor;

            if (chkAutoScroll.Checked)
            {
                rtbTerminal.SelectionStart = rtbTerminal.TextLength;
                rtbTerminal.ScrollToCaret();
            }
        });
    }

    #endregion

    #region Protocol Frame Inspector Handlers

    private void AddFrameToInspector(FrameLogEntry entry)
    {
        if (dgvFrames.IsDisposed) return;

        int rowIndex = dgvFrames.Rows.Add(
            entry.Direction == FrameDirection.Outbound ? "OUT" : "IN",
            entry.Timestamp.ToString("HH:mm:ss.fff"),
            entry.MessageType,
            $"{entry.PayloadSize} B",
            entry.MessageId,
            entry.RequestId ?? "-",
            entry.InReplyTo ?? "-"
        );

        var row = dgvFrames.Rows[rowIndex];
        row.Tag = entry;

        if (entry.Direction == FrameDirection.Inbound)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 244); // Green tint
            row.DefaultCellStyle.ForeColor = Color.FromArgb(22, 101, 52);   // Green-800
        }
        else
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(239, 246, 255); // Blue tint
            row.DefaultCellStyle.ForeColor = Color.FromArgb(30, 64, 175);   // Blue-800
        }

        // Giới hạn tối đa 500 dòng gần nhất trong bộ nhớ
        while (dgvFrames.Rows.Count > 500)
        {
            dgvFrames.Rows.RemoveAt(0);
        }

        lblInspectorCount.Text = $"Tổng: {dgvFrames.Rows.Count} gói tin (Giới hạn 500 gói gần nhất)";

        // Tự động cuộn và chọn gói tin mới nhất nếu được bật
        if (chkAutoSelectLatest.Checked)
        {
            dgvFrames.ClearSelection();
            row.Selected = true;
            dgvFrames.FirstDisplayedScrollingRowIndex = row.Index;
            DisplayFrameDetails(entry);
        }
        else if (dgvFrames.SelectedRows.Count == 0)
        {
            DisplayFrameDetails(entry);
        }
    }

    private void OnDgvFramesSelectionChanged(object? sender, EventArgs e)
    {
        if (dgvFrames.SelectedRows.Count > 0)
        {
            var selectedRow = dgvFrames.SelectedRows[0];
            if (selectedRow.Tag is FrameLogEntry selectedEntry)
            {
                DisplayFrameDetails(selectedEntry);
            }
        }
    }

    private void DisplayFrameDetails(FrameLogEntry entry)
    {
        _selectedFrameEntry = entry;

        byte b0 = entry.HeaderBytes.Length > 0 ? entry.HeaderBytes[0] : (byte)0;
        byte b1 = entry.HeaderBytes.Length > 1 ? entry.HeaderBytes[1] : (byte)0;
        byte b2 = entry.HeaderBytes.Length > 2 ? entry.HeaderBytes[2] : (byte)0;
        byte b3 = entry.HeaderBytes.Length > 3 ? entry.HeaderBytes[3] : (byte)0;

        txtWireHeaderHex.Text = $"[ 0x{b0:X2} 0x{b1:X2} 0x{b2:X2} 0x{b3:X2} ]  ->  {entry.PayloadSize} bytes";
        txtWireTotalSize.Text = $"{entry.PayloadSize + 4} bytes (4B Header + {entry.PayloadSize}B Payload)";
        rtbFrameJson.Text = entry.FormattedJson;
    }

    private void ClearInspector()
    {
        SafeInvoke(() =>
        {
            dgvFrames.Rows.Clear();
            txtWireHeaderHex.Clear();
            txtWireTotalSize.Clear();
            rtbFrameJson.Clear();
            _selectedFrameEntry = null;
            lblInspectorCount.Text = "Tổng: 0 gói tin (Giới hạn 500 gói gần nhất)";
        });
    }

    private void CopyCurrentFrameJson()
    {
        SafeInvoke(() =>
        {
            string jsonToCopy = _selectedFrameEntry?.FormattedJson ?? rtbFrameJson.Text;
            if (!string.IsNullOrEmpty(jsonToCopy))
            {
                Clipboard.SetText(jsonToCopy);
                MessageBox.Show(this, "Đã sao chép chuỗi JSON vào Clipboard thành công!", "Đã sao chép", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        });
    }

    #endregion

    #region Failure & Resilience Event Handlers (T19.3)
    
    /// <summary>
    /// Giai đoạn T19.3: Xử lý sự cố mạng - Tấn công giả lập Local Client Drop Socket
    /// </summary>
    private void HandleSimulateClientDrop()
    {
        if (!_clientService.IsConnected)
        {
            MessageBox.Show(this, "Client hiện chưa kết nối đến server để ngắt socket.", "Chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        LogResilience("TRIGGER", $"[{DateTime.Now:HH:mm:ss.fff}] Cưỡng chế ngắt socket kết nối Client (Abrupt Socket Drop / TCP RST)...", Color.FromArgb(245, 158, 11));
        _clientService.SimulateAbruptDisconnect();
    }

    /// <summary>
    /// Giai đoạn T19.3: Xử lý sự cố mạng - Tấn công giả lập Client Silence (Im lặng)
    /// </summary>
    private void HandleSimulateSilence()
    {
        if (chkAutoHeartbeat.Checked)
        {
            chkAutoHeartbeat.Checked = false;
            LogResilience("SILENCE", $"[{DateTime.Now:HH:mm:ss.fff}] Đã tạm dừng Auto Heartbeat! Client chuyển sang trạng thái im lặng để kiểm thử Watchdog Master phát hiện kết nối chết ngầm (Half-Open)...", Color.FromArgb(239, 68, 68));
        }
        else
        {
            chkAutoHeartbeat.Checked = true;
            LogResilience("RESUME", $"[{DateTime.Now:HH:mm:ss.fff}] Đã kích hoạt lại Auto Heartbeat bình thường.", Color.FromArgb(52, 211, 153));
        }
    }

    /// <summary>
    /// Giai đoạn T19.3: Xử lý sự cố mạng - Tấn công giả lập Remote Agent Drop Socket
    /// </summary>
    private async Task HandleSimulateRemoteAgentDropAsync()
    {
        string targetAgentId = txtTargetAgentId.Text.Trim();
        if (string.IsNullOrEmpty(targetAgentId))
        {
            MessageBox.Show(this, "Vui lòng nhập Agent ID cần ngắt socket từ xa.", "Lỗi nhập liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string host = txtHost.Text.Trim();
        if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

        try
        {
            LogResilience("HTTP_CALL", $"[{DateTime.Now:HH:mm:ss.fff}] Đang gửi yêu cầu tới Master API để kill socket của Agent '{targetAgentId}'...", Color.FromArgb(129, 140, 248));
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.PostAsync($"http://{host}:5000/api/fault/drop-socket/{targetAgentId}", null).ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                LogResilience("KILL_OK", $"[{DateTime.Now:HH:mm:ss.fff}] Master đã cưỡng chế ngắt socket của Agent '{targetAgentId}' thành công! Phản hồi: {responseBody}", Color.FromArgb(52, 211, 153));
            }
            else
            {
                LogResilience("KILL_WARN", $"[{DateTime.Now:HH:mm:ss.fff}] Master phản hồi HTTP {(int)response.StatusCode}: {responseBody}", Color.FromArgb(251, 191, 36));
            }
        }
        catch (Exception ex)
        {
            LogResilience("ERROR", $"[{DateTime.Now:HH:mm:ss.fff}] Không thể kết nối tới Master API (http://{host}:5000): {ex.Message}", Color.FromArgb(239, 68, 68));
        }
    }

    /// <summary>
    /// Giai đoạn T19.3: Xử lý sự cố mạng - Tấn công giả lập Protocol Fuzzing
    /// </summary>
    private async Task HandleSimulateFuzzingAsync()
    {
        string host = txtHost.Text.Trim();
        if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

        try
        {
            LogResilience("FUZZING", $"[{DateTime.Now:HH:mm:ss.fff}] Bắt đầu kiểm thử tính năng phòng thủ DoS OOM 16MB (Protocol Fuzzing Defense)...", Color.FromArgb(167, 139, 250));
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var requestObj = new FailureTestRequest
            {
                Scenario = FailureScenarioType.MalformedPacketAttack,
                TargetAgentId = "Self-Defense"
            };

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestObj),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync($"http://{host}:5000/api/fault/simulate", content).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            LogResilience("DEFENSE_OK", $"[{DateTime.Now:HH:mm:ss.fff}] Kết quả thẩm định: {responseBody}", Color.FromArgb(52, 211, 153));
        }
        catch (Exception ex)
        {
            LogResilience("ERROR", $"[{DateTime.Now:HH:mm:ss.fff}] Lỗi khi kiểm thử Fuzzing qua HTTP API: {ex.Message}", Color.FromArgb(239, 68, 68));
        }
    }

    private void LogResilience(string phase, string message, Color color)
    {
        SafeInvoke(() =>
        {
            rtbResilienceLog.SelectionStart = rtbResilienceLog.TextLength;
            rtbResilienceLog.SelectionLength = 0;
            rtbResilienceLog.SelectionColor = color;
            rtbResilienceLog.AppendText($"[{phase}] {message}\n");
            rtbResilienceLog.ScrollToCaret();

            lblResilienceStatus.Text = $"Trạng thái gần nhất: [{phase}] {DateTime.Now:HH:mm:ss}";
        });
    }

    private void ClearResilienceLog()
    {
        SafeInvoke(() =>
        {
            rtbResilienceLog.Clear();
            lblResilienceStatus.Text = "Trạng thái: Đã xóa nhật ký";
        });
    }

    private void CopyResilienceLog()
    {
        SafeInvoke(() =>
        {
            if (!string.IsNullOrEmpty(rtbResilienceLog.Text))
            {
                Clipboard.SetText(rtbResilienceLog.Text);
                MessageBox.Show(this, "Đã sao chép toàn bộ báo cáo kiểm thử sự cố vào Clipboard!", "Đã sao chép", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        });
    }

    #endregion

    #region Thread-safe UI Dispatch Helper

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || Disposing) return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
                // Form đã đóng, bỏ qua
            }
        }
        else
        {
            action();
        }
    }

    #endregion
}
