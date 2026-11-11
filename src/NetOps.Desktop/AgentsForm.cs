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
        ApplyFilters();
        InitializeMetricsUi(); // Bật giao diện metrics
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
        if (agent is not null && agent.IsOnline)
        {
            pnlMetrics.Visible = true;
            _ = FetchMetricsForSelectedAgentAsync();
        }
        else
        {
            pnlMetrics.Visible = false;
            tmrMetrics.Stop();
            swAutoRefresh.Checked = false;
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
        // Khối Card bo tròn chứa các chỉ số Prometheus đặt ở cột bên phải
        pnlMetrics = new AntdUI.Panel
        {
            Dock = DockStyle.Bottom,
            Height = 220,
            Radius = 8,
            BorderWidth = 1F,
            BorderColor = Color.FromArgb(220, 228, 238),
            Back = Color.FromArgb(249, 250, 252),
            Padding = new Padding(12),
            Margin = new Padding(0, 8, 0, 0),
            Visible = false // Ẩn khi chưa chọn máy nào
        };

        // 1. Nhãn & Thanh tiến trình CPU (%)
        lblCpuTitle = new AntdUI.Label
        {
            Text = "CPU: 0%",
            Height = 20,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(36, 54, 77)
        };

        prgCpu = new AntdUI.Progress
        {
            Dock = DockStyle.Top,
            Height = 16,
            Value = 0,
            Fill = Color.FromArgb(52, 199, 89) // Mặc định xanh lá
        };

        // 2. Nhãn hiển thị RAM
        var lblRamTitle = new AntdUI.Label
        {
            Text = "MEMORY USED",
            Height = 18,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(116, 134, 158)
        };
        lblRamValue = new AntdUI.Label
        {
            Text = "—",
            Height = 22,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 35, 57)
        };

        // 3. Nhãn hiển thị Ổ đĩa Disk
        var lblDiskTitle = new AntdUI.Label
        {
            Text = "DISK FREE",
            Height = 18,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(116, 134, 158)
        };
        lblDiskValue = new AntdUI.Label
        {
            Text = "—",
            Height = 22,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 35, 57)
        };

        // 4. Thanh công cụ Refresh & Switch Auto 5s
        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            FlowDirection = FlowDirection.LeftToRight
        };

        btnRefreshMetrics = new AntdUI.Button
        {
            Text = "Refresh",
            Width = 80,
            Height = 28,
            Type = AntdUI.TTypeMini.Default
        };
        btnRefreshMetrics.Click += async (_, _) => await FetchMetricsForSelectedAgentAsync();

        var lblAuto = new AntdUI.Label
        {
            Text = "Auto (5s):",
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
            Font = new Font("Segoe UI", 8F)
        };

        swAutoRefresh = new AntdUI.Switch
        {
            Checked = false,
            Margin = new Padding(4, 4, 0, 0)
        };
        swAutoRefresh.CheckedChanged += (_, _) => tmrMetrics.Enabled = swAutoRefresh.Checked;

        pnlActions.Controls.Add(btnRefreshMetrics);
        pnlActions.Controls.Add(lblAuto);
        pnlActions.Controls.Add(swAutoRefresh);

        // Timer tự động cập nhật mỗi 5 giây
        tmrMetrics = new System.Windows.Forms.Timer { Interval = 5000 };
        tmrMetrics.Tick += async (_, _) => await FetchMetricsForSelectedAgentAsync();

        // Gắn tất cả các control vào thẻ Card pnlMetrics
        pnlMetrics.Controls.Add(pnlActions);
        pnlMetrics.Controls.Add(lblDiskValue);
        pnlMetrics.Controls.Add(lblDiskTitle);
        pnlMetrics.Controls.Add(lblRamValue);
        pnlMetrics.Controls.Add(lblRamTitle);
        pnlMetrics.Controls.Add(prgCpu);
        pnlMetrics.Controls.Add(lblCpuTitle);

        // Gắn pnlMetrics vào đáy của cột detailPanel
        detailPanel.Controls.Add(pnlMetrics);
        pnlMetrics.BringToFront();
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
        prgCpu.Value = (float)Math.Clamp(cpuPercent, 0, 100);

        if (cpuPercent < 60)
        {
            // Tải bình thường: Xanh lá cây (Normal)
            prgCpu.Fill = Color.FromArgb(52, 199, 89);
            badgeMetricStatus.State = AntdUI.TState.Success;
            badgeMetricStatus.Text = "Optimal";
        }
        else if (cpuPercent < 85)
        {
            // Tải tăng cao: Vàng cam (Warning)
            prgCpu.Fill = Color.FromArgb(255, 149, 0);
            badgeMetricStatus.State = AntdUI.TState.Warn;
            badgeMetricStatus.Text = "Moderate load";
        }
        else
        {
            // Tải nghiêm trọng: Đỏ (Critical)
            prgCpu.Fill = Color.FromArgb(255, 59, 48);
            badgeMetricStatus.State = AntdUI.TState.Error;
            badgeMetricStatus.Text = "High load alert";
        }
    }
}
