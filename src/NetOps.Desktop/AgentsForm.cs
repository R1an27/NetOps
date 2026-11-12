using System.ComponentModel;
using NetOps.Shared;

namespace NetOps.Desktop;

public partial class AgentsForm : AntdUI.Window
{
    private enum AgentFilter { All, Online, Offline }

    private readonly CancellationTokenSource _lifetime = new();
    private List<AgentSummary> _agents = new();
    private List<AgentSummary> _visibleAgents = new();
    private AgentSummary? _candidate;
    private AgentFilter _filter;
    private bool _refreshing;
    private bool _refreshFailed;
    private bool _lifetimeDisposed;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AgentSummary? SelectedAgent { get; private set; }

    // Dùng kết nối SignalR của Form chính để tải lại dữ liệu.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<CancellationToken, Task<List<AgentSummary>>>? ReloadAgentsAsync { get; set; }

    // Cung cấp công cụ gọi API Prometheus từ Form chính truyền vào
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MetricsApiClient? MetricsClient { get; set; }
    // Khai báo các control hiển thị Metric của AntdUI
    private AntdUI.Panel pnlMetrics = null!;
    private AntdUI.Label lblCpuTitle = null!;
    private AntdUI.Progress prgCpu = null!;
    private AntdUI.Label lblRamValue = null!;
    private AntdUI.Label lblDiskValue = null!;
    private AntdUI.Button btnRefreshMetrics = null!;
    private AntdUI.Switch swAutoRefresh = null!;
    private AntdUI.Badge badgeMetricStatus = null!;
    private System.Windows.Forms.Timer tmrMetrics = null!;
    public AgentsForm()
    {
        InitializeComponent();
        ConfigureTable();
        InitializeMetricsUi(); // Bật giao diện metrics
        ApplyFilters();
    }

    private void ConfigureTable()
    {
        tblAgents.Columns.Add(new AntdUI.Column(nameof(AgentSummary.Hostname), "HOSTNAME")
        {
            Ellipsis = true,
            SortOrder = true
        });
        tblAgents.Columns.Add(new AntdUI.Column(nameof(AgentSummary.IpAddress), "IP ADDRESS")
        {
            Ellipsis = true,
            SortOrder = true
        });
        tblAgents.Columns.Add(new AntdUI.Column(nameof(AgentSummary.OsDescription), "OPERATING SYSTEM")
        {
            Ellipsis = true,
            SortOrder = true
        });
        tblAgents.Columns.Add(new AntdUI.Column(nameof(AgentSummary.IsOnline), "STATUS")
        {
            Render = (value, record, index) => value is true
                ? new AntdUI.CellBadge(AntdUI.TState.Success, "Online")
                : new AntdUI.CellBadge(AntdUI.TState.Default, "Offline")
        });
        tblAgents.SortRows += (_, _) =>
        {
            tblAgents.SelectedIndex = -1;
            SelectCandidate(null);
        };
    }

    public void SetAgents(IEnumerable<AgentSummary> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents.ToList();
        _refreshFailed = false;
        lblTotalCount.Text = _agents.Count.ToString();
        lblOnlineCount.Text = _agents.Count(agent => agent.IsOnline).ToString();
        lblOfflineCount.Text = _agents.Count(agent => !agent.IsOnline).ToString();
        lblUpdated.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        btnRefresh.Enabled = ReloadAgentsAsync is not null && !_refreshing;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = txtSearch.Text.Trim();
        _visibleAgents = _agents
            .Where(agent => _filter == AgentFilter.All
                || (_filter == AgentFilter.Online ? agent.IsOnline : !agent.IsOnline))
            .Where(agent => query.Length == 0
                || agent.Hostname.Contains(query, StringComparison.OrdinalIgnoreCase)
                || agent.IpAddress.Contains(query, StringComparison.OrdinalIgnoreCase)
                || agent.OsDescription.Contains(query, StringComparison.OrdinalIgnoreCase)
                || agent.AgentId.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(agent => agent.IsOnline)
            .ThenBy(agent => agent.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SelectCandidate(null);
        tblAgents.EmptyText = _agents.Count == 0
            ? "No agents registered yet. Connect an agent and refresh."
            : "No agents match your search or status filter.";
        tblAgents.DataSource = _visibleAgents;
        lblResults.Text = $"{_visibleAgents.Count} of {_agents.Count} agents";
        UpdateFilterButtons();
    }

    private void SetFilter(AgentFilter filter)
    {
        _filter = filter;
        ApplyFilters();
    }

    private void UpdateFilterButtons()
    {
        StyleFilter(btnAll, _filter == AgentFilter.All);
        StyleFilter(btnOnline, _filter == AgentFilter.Online);
        StyleFilter(btnOffline, _filter == AgentFilter.Offline);
    }

    private static void StyleFilter(AntdUI.Button button, bool selected)
    {
        button.DefaultBack = selected ? Color.FromArgb(232, 240, 255) : Color.White;
        button.DefaultBorderColor = selected ? Color.FromArgb(177, 204, 255) : Color.FromArgb(220, 228, 238);
        button.ForeColor = selected ? Color.FromArgb(29, 78, 216) : Color.FromArgb(90, 108, 132);
        button.BackHover = selected ? Color.FromArgb(220, 233, 255) : Color.FromArgb(246, 248, 252);
    }

    private void SelectCandidate(AgentSummary? agent)
    {
        _candidate = agent;
        lblSelectedName.Text = agent is null ? "No agent selected" : agent.Hostname;
        badgeSelected.Text = agent is null ? "Choose a row" : agent.IsOnline ? "Online" : "Offline";
        badgeSelected.State = agent?.IsOnline == true ? AntdUI.TState.Success : AntdUI.TState.Default;
        lblSelectedIp.Text = agent?.IpAddress ?? "—";
        lblSelectedOs.Text = agent?.OsDescription ?? "—";
        lblSelectedId.Text = agent?.AgentId ?? "—";
        detailPanel.BorderColor = agent is null
            ? Color.FromArgb(220, 228, 238)
            : Color.FromArgb(177, 204, 255);

        btnUseAgent.Enabled = CanUseCandidate;
        if (!_refreshing && !_refreshFailed)
        {
            lblMessage.ForeColor = agent is not null && !agent.IsOnline
                ? Color.FromArgb(146, 64, 14) : Color.FromArgb(97, 115, 139);
            lblMessage.Text = agent is null
                ? "Use arrow keys to select; Enter to use an online agent."
                : agent.IsOnline
                    ? $"Ready to run commands on {agent.Hostname}."
                    : "This agent is offline. Select an online machine.";
        }

        // KÍCH HOẠT TẢI METRICS KHI CHỌN MÁY ONLINE
        if (pnlMetrics is not null)
        {
            if (agent is not null && agent.IsOnline)
            {
                pnlMetrics.Visible = true;
                _ = FetchMetricsForSelectedAgentAsync();
            }
            else
            {
                pnlMetrics.Visible = false;
                tmrMetrics?.Stop();
                if (swAutoRefresh is not null)
                    swAutoRefresh.Checked = false;
            }
        }
    }

    private bool CanUseCandidate =>
        _candidate?.IsOnline == true && !_refreshing && !_refreshFailed;

    private void UseSelectedAgent()
    {
        if (!CanUseCandidate)
            return;

        SelectedAgent = _candidate;
        DialogResult = DialogResult.OK;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        txtSearch.Focus();
        if (ReloadAgentsAsync is not null)
            await RefreshAgentsAsync();
    }

    private async Task RefreshAgentsAsync()
    {
        if (_refreshing || ReloadAgentsAsync is null || _lifetimeDisposed)
            return;

        var cancellationToken = _lifetime.Token;
        _refreshing = true;
        btnRefresh.Loading = true;
        btnRefresh.Enabled = false;
        btnUseAgent.Enabled = false;
        lblMessage.ForeColor = Color.FromArgb(97, 115, 139);
        lblMessage.Text = "Refreshing agent directory...";
        try
        {
            var agents = await ReloadAgentsAsync(cancellationToken);
            if (_lifetimeDisposed || IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
                return;

            SetAgents(agents);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cửa sổ đã đóng, không cập nhật giao diện nữa.
        }
        catch (Exception ex)
        {
            if (_lifetimeDisposed || IsDisposed || Disposing)
                return;

            _refreshFailed = true;
            lblMessage.ForeColor = Color.FromArgb(185, 28, 28);
            lblMessage.Text = $"Could not refresh agents: {ex.Message}";
        }
        finally
        {
            _refreshing = false;
            if (!_lifetimeDisposed && !IsDisposed && !Disposing)
            {
                btnRefresh.Loading = false;
                btnRefresh.Enabled = ReloadAgentsAsync is not null;
                SelectCandidate(_candidate);
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if ((keyData == Keys.Up || keyData == Keys.Down)
            && (txtSearch.ContainsFocus || tblAgents.ContainsFocus))
        {
            MoveSelection(keyData == Keys.Down ? 1 : -1);
            return true;
        }
        if (keyData == Keys.Enter)
        {
            UseSelectedAgent();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void MoveSelection(int direction)
    {
        var rows = tblAgents.SortList().OfType<AgentSummary>().ToList();
        if (rows.Count == 0)
            return;

        var current = rows.FindIndex(agent => agent.AgentId == _candidate?.AgentId);
        var next = current < 0
            ? (direction > 0 ? 0 : rows.Count - 1)
            : Math.Clamp(current + direction, 0, rows.Count - 1);
        // SelectedIndex bao gồm hàng tiêu đề; danh sách dữ liệu bắt đầu từ 0.
        tblAgents.SelectedIndex = next + 1;
        tblAgents.ScrollLine(rows[next], true);
        SelectCandidate(rows[next]);
        tblAgents.Focus();
    }

    private void txtSearch_TextChanged(object? sender, EventArgs e) => ApplyFilters();
    private void btnAll_Click(object? sender, EventArgs e) => SetFilter(AgentFilter.All);
    private void btnOnline_Click(object? sender, EventArgs e) => SetFilter(AgentFilter.Online);
    private void btnOffline_Click(object? sender, EventArgs e) => SetFilter(AgentFilter.Offline);
    private async void btnRefresh_Click(object? sender, EventArgs e) => await RefreshAgentsAsync();
    private void btnUseAgent_Click(object? sender, EventArgs e) => UseSelectedAgent();
    private void btnCancel_Click(object? sender, EventArgs e) => DialogResult = DialogResult.Cancel;

    private void tblAgents_CellClick(object sender, AntdUI.TableClickEventArgs e)
    {
        if (e.Record is AgentSummary agent)
            SelectCandidate(agent);
    }

    private void tblAgents_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
    {
        if (e.Record is AgentSummary agent)
        {
            SelectCandidate(agent);
            UseSelectedAgent();
        }
    }

    private void tblAgents_CellFocused(object sender, AntdUI.TableCellFocusedEventArgs e)
    {
        if (e.Record is AgentSummary agent)
            SelectCandidate(agent);
    }

    private void DisposeLifetime()
    {
        if (_lifetimeDisposed)
            return;
        _lifetimeDisposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void InitializeMetricsUi()
    {
        badgeMetricStatus = new AntdUI.Badge
        {
            Text = "Optimal",
            State = AntdUI.TState.Success,
            Dock = DockStyle.Right
        };

        // 1. Thẻ Card bo tròn lớn, hiện đại chứa các chỉ số Prometheus
        pnlMetrics = new AntdUI.Panel
        {
            Dock = DockStyle.Top,
            Height = 240,
            Radius = 12,
            BorderWidth = 1F,
            BorderColor = Color.FromArgb(215, 225, 238),
            Back = Color.FromArgb(248, 250, 253),
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0),
            Visible = false // Mặc định ẩn khi chưa chọn máy nào
        };

        // Bố cục dạng TableLayoutPanel chia 5 hàng khoa học, không bao giờ bị đè chữ
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F)); // Hàng 0: Tiêu đề + Badge
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F)); // Hàng 1: CPU Utilization
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F)); // Hàng 2: RAM & DISK Cards
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));  // Hàng 3: Khoảng đệm
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); // Hàng 4: Toolbar nút bấm to rõ

        // --- HÀNG 0: Tiêu đề + Badge trạng thái ---
        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lblHeader = new AntdUI.Label
        {
            Text = "LIVE PERFORMANCE METRICS",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(71, 85, 105),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        headerPanel.Controls.Add(lblHeader, 0, 0);
        headerPanel.Controls.Add(badgeMetricStatus, 1, 0);
        layout.Controls.Add(headerPanel, 0, 0);

        // --- HÀNG 1: CPU Utilization (Tiêu đề + % góc phải + Progress to rõ) ---
        var cpuPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        cpuPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        cpuPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cpuPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        cpuPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

        var lblCpuCaption = new AntdUI.Label
        {
            Text = "CPU UTILIZATION",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 116, 139),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        lblCpuTitle = new AntdUI.Label
        {
            Text = "0.0%",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 199, 89),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };
        prgCpu = new AntdUI.Progress
        {
            Dock = DockStyle.Fill,
            Height = 22,
            Value = 0,
            Radius = 6,
            Fill = Color.FromArgb(52, 199, 89)
        };
        cpuPanel.Controls.Add(lblCpuCaption, 0, 0);
        cpuPanel.Controls.Add(lblCpuTitle, 1, 0);
        cpuPanel.Controls.Add(prgCpu, 0, 1);
        cpuPanel.SetColumnSpan(prgCpu, 2);
        layout.Controls.Add(cpuPanel, 0, 1);

        // --- HÀNG 2: RAM & DISK (2 thẻ Card riêng biệt, nền trắng bo góc sang trọng) ---
        var statsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = Color.Transparent
        };
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Subcard 1: MEMORY
        var pnlRamCard = new AntdUI.Panel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            Back = Color.White,
            BorderWidth = 1F,
            BorderColor = Color.FromArgb(226, 232, 240),
            Margin = new Padding(0, 0, 6, 0)
        };
        var ramLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 8, 12, 6)
        };
        ramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        ramLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var lblRamCaption = new AntdUI.Label
        {
            Text = "MEMORY USED",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 116, 139),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        lblRamValue = new AntdUI.Label
        {
            Text = "—",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        ramLayout.Controls.Add(lblRamCaption, 0, 0);
        ramLayout.Controls.Add(lblRamValue, 0, 1);
        pnlRamCard.Controls.Add(ramLayout);

        // Subcard 2: DISK
        var pnlDiskCard = new AntdUI.Panel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            Back = Color.White,
            BorderWidth = 1F,
            BorderColor = Color.FromArgb(226, 232, 240),
            Margin = new Padding(6, 0, 0, 0)
        };
        var diskLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 8, 12, 6)
        };
        diskLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        diskLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var lblDiskCaption = new AntdUI.Label
        {
            Text = "DISK FREE",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 116, 139),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        lblDiskValue = new AntdUI.Label
        {
            Text = "—",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        diskLayout.Controls.Add(lblDiskCaption, 0, 0);
        diskLayout.Controls.Add(lblDiskValue, 0, 1);
        pnlDiskCard.Controls.Add(diskLayout);

        statsGrid.Controls.Add(pnlRamCard, 0, 0);
        statsGrid.Controls.Add(pnlDiskCard, 1, 0);
        layout.Controls.Add(statsGrid, 0, 2);

        // --- HÀNG 4: Toolbar (Nút Refresh to đẹp + Công tắc Auto-refresh) ---
        var actionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = Color.Transparent
        };
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        actionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        btnRefreshMetrics = new AntdUI.Button
        {
            Text = "Refresh",
            Width = 110,
            Height = 36,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Type = AntdUI.TTypeMini.Primary,
            Dock = DockStyle.Left,
            Cursor = Cursors.Hand
        };
        btnRefreshMetrics.Click += async (_, _) => await FetchMetricsForSelectedAgentAsync();

        var lblAuto = new AntdUI.Label
        {
            Text = "Auto-refresh (5s):",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(71, 85, 105),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };

        swAutoRefresh = new AntdUI.Switch
        {
            Checked = false,
            Dock = DockStyle.Right,
            Margin = new Padding(6, 6, 0, 0)
        };
        swAutoRefresh.CheckedChanged += (_, _) => tmrMetrics.Enabled = swAutoRefresh.Checked;

        actionsPanel.Controls.Add(btnRefreshMetrics, 0, 0);
        actionsPanel.Controls.Add(lblAuto, 1, 0);
        actionsPanel.Controls.Add(swAutoRefresh, 2, 0);
        layout.Controls.Add(actionsPanel, 0, 4);

        // Timer tự động cập nhật mỗi 5 giây
        tmrMetrics = new System.Windows.Forms.Timer { Interval = 5000 };
        tmrMetrics.Tick += async (_, _) => await FetchMetricsForSelectedAgentAsync();

        pnlMetrics.Controls.Add(layout);

        // Gắn vào hàng 16 của detailLayout
        detailLayout.Controls.Add(pnlMetrics, 0, 16);
    }

    // Hàm gọi API và cập nhật số liệu
    private async Task FetchMetricsForSelectedAgentAsync()
    {
        if (_candidate is null || !_candidate.IsOnline || MetricsClient is null)
        {
            pnlMetrics.Visible = false;
            return;
        }

        try
        {
            btnRefreshMetrics.Loading = true;
            btnRefreshMetrics.Enabled = false;

            // Gọi API lấy dữ liệu từ Prometheus qua MetricsApiClient
            var metrics = await MetricsClient.GetHostMetricsAsync(_candidate.AgentId);
            if (metrics is null)
            {
                lblCpuTitle.Text = "CPU: —";
                lblRamValue.Text = "No data";
                lblDiskValue.Text = "No data";
                prgCpu.Value = 0;
                return;
            }

            // 1. Cập nhật CPU và đổi màu cảnh báo theo ngưỡng tải
            UpdateCpuDisplay(metrics.CpuPercent);

            // 2. Cập nhật RAM và Ổ đĩa (Quy đổi sang GB/MB dễ đọc)
            lblRamValue.Text = MetricsFormatter.FormatBytes(metrics.MemoryUsedBytes);
            lblDiskValue.Text = $"{MetricsFormatter.FormatBytes(metrics.DiskFreeBytes)} free";
        }
        catch
        {
            // Bỏ qua lỗi mạng
        }
        finally
        {
            btnRefreshMetrics.Loading = false;
            btnRefreshMetrics.Enabled = true;
        }
    }

    private void UpdateCpuDisplay(double cpuPercent)
    {
        // Thang đo AntdUI.Progress.Value quy định từ 0.0F đến 1.0F (0.05F = 5%, 1.0F = 100%)
        float normalized = (float)Math.Clamp(cpuPercent / 100.0, 0.0, 1.0);
        prgCpu.Value = normalized;
        lblCpuTitle.Text = $"{cpuPercent:F1}%";
        if (cpuPercent < 60)
        {
            // Tải bình thường: Xanh lá cây (Normal)
            prgCpu.Fill = Color.FromArgb(52, 199, 89);
            lblCpuTitle.ForeColor = Color.FromArgb(52, 199, 89);
            if (badgeMetricStatus is not null)
            {
                badgeMetricStatus.State = AntdUI.TState.Success;
                badgeMetricStatus.Text = "Optimal";
            }
        }
        else if (cpuPercent < 85)
        {
            // Tải tăng cao: Vàng cam (Warning)
            prgCpu.Fill = Color.FromArgb(255, 149, 0);
            lblCpuTitle.ForeColor = Color.FromArgb(255, 149, 0);
            if (badgeMetricStatus is not null)
            {
                badgeMetricStatus.State = AntdUI.TState.Warn;
                badgeMetricStatus.Text = "Moderate load";
            }
        }
        else
        {
            // Tải nghiêm trọng: Đỏ (Critical)
            prgCpu.Fill = Color.FromArgb(255, 59, 48);
            lblCpuTitle.ForeColor = Color.FromArgb(255, 59, 48);
            if (badgeMetricStatus is not null)
            {
                badgeMetricStatus.State = AntdUI.TState.Error;
                badgeMetricStatus.Text = "High load alert";
            }
        }
    }
}
