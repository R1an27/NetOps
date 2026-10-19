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
        _clientService.ConnectionStateChanged += OnConnectionStateChanged;
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

    private void OnConnectionStateChanged(ConnectionState newState)
    {
        SafeInvoke(() =>
        {
            switch (newState)
            {
                case ConnectionState.Connected:
                    pnlStatusBadge.BackColor = Color.FromArgb(16, 185, 129); // Green
                    lblStatusBadge.Text = "CONNECTED";
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                    btnPing.Enabled = true;
                    btnHeartbeat.Enabled = true;
                    chkAutoHeartbeat.Enabled = true;
                    btnExecuteCommand.Enabled = true;
                    txtHost.Enabled = false;
                    numPort.Enabled = false;
                    lblConnectionStatus.Text = $"Đã kết nối thành công tới {_clientService.ConnectedEndpoint}";
                    statusLabel.Text = "Connected";
                    statusEndpoint.Text = $" | Endpoint: {_clientService.ConnectedEndpoint}";
                    break;

                case ConnectionState.Connecting:
                    pnlStatusBadge.BackColor = Color.FromArgb(245, 158, 11); // Amber
                    lblStatusBadge.Text = "CONNECTING";
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = false;
                    btnExecuteCommand.Enabled = false;
                    lblConnectionStatus.Text = "Đang kết nối...";
                    statusLabel.Text = "Connecting...";
                    break;

                case ConnectionState.Disconnected:
                default:
                    pnlStatusBadge.BackColor = Color.FromArgb(100, 116, 139); // Slate Gray
                    lblStatusBadge.Text = "DISCONNECTED";
                    btnConnect.Enabled = true;
                    btnDisconnect.Enabled = false;
                    btnPing.Enabled = false;
                    btnHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Enabled = false;
                    chkAutoHeartbeat.Checked = false;
                    btnExecuteCommand.Enabled = false;
                    _autoHeartbeatTimer.Stop();
                    txtHost.Enabled = true;
                    numPort.Enabled = true;
                    lblConnectionStatus.Text = "Chưa kết nối máy chủ";
                    statusLabel.Text = "Disconnected";
                    statusEndpoint.Text = " | Endpoint: Not connected";
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
