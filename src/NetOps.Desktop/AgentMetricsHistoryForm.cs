using NetOps.Shared;
using ScottPlot;
// Giải quyết xung đột FontStyle giữa Winforms và ScottPlot
using FontStyle = System.Drawing.FontStyle;

namespace NetOps.Desktop;

/// <summary>
/// Cửa sổ Pop-up độc lập hiển thị lịch sử và xu hướng hiệu năng của Agent (CPU, RAM, Disk)
/// theo các dải thời gian: 5m, 15m, 1h, 3h, 6h sử dụng ScottPlot 5.
/// </summary>
public partial class AgentMetricsHistoryForm : AntdUI.Window
{
    private readonly string _agentId;
    private readonly string _hostname;
    private readonly string _ipAddress;
    private readonly MetricsApiClient? _metricsClient;

    // Trạng thái truy vấn
    private string _currentRange = "15m";
    private MetricViewType _currentMetricType = MetricViewType.Cpu;
    private CancellationTokenSource? _queryCts;

    // Bộ nhớ đệm dữ liệu chuỗi thời gian
    private List<MetricDataPointItem> _cpuPoints = new();
    private List<MetricDataPointItem> _ramPoints = new();
    private List<MetricDataPointItem> _diskPoints = new();

    // Controls
    private AntdUI.Button[] _rangeButtons = null!;
    private AntdUI.Button[] _metricTypeButtons = null!;
    private AntdUI.Button _btnRefresh = null!;
    private AntdUI.Label _lblStatus = null!;
    private AntdUI.Label _lblPeakValue = null!;
    private AntdUI.Label _lblPeakSub = null!;
    private AntdUI.Label _lblAvgValue = null!;
    private AntdUI.Label _lblMinValue = null!;
    private ScottPlot.WinForms.FormsPlot _formsPlot = null!;

    public enum MetricViewType { Cpu, Memory, Disk }
    public record MetricDataPointItem(DateTime Timestamp, double Value);

    public AgentMetricsHistoryForm(string agentId, string hostname, string ipAddress, MetricsApiClient? metricsClient)
    {
        _agentId = agentId;
        _hostname = hostname;
        _ipAddress = ipAddress;
        _metricsClient = metricsClient;

        InitializeFormProperties();
        BuildLayout();
    }

    public AgentMetricsHistoryForm(AgentSummary agent, MetricsApiClient? metricsClient)
        : this(agent.AgentId, agent.Hostname, agent.IpAddress, metricsClient)
    {
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = null;
        base.OnFormClosed(e);
    }

    private void InitializeFormProperties()
    {
        Text = $"Metrics History — {_hostname}";
        ClientSize = new Size(960, 650);
        MinimumSize = new Size(880, 580);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
        ShowInTaskbar = false;
        MaximizeBox = true;
        MinimizeBox = false;
    }

    private void BuildLayout()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = System.Drawing.Color.Transparent,
            Padding = new Padding(20, 16, 20, 16)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));  // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));  // Toolbar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));  // 3 KPI Cards
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Biểu đồ ScottPlot
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));  // Footer

        mainLayout.Controls.Add(CreateHeaderPanel(), 0, 0);
        mainLayout.Controls.Add(CreateToolbarPanel(), 0, 1);
        mainLayout.Controls.Add(CreateKpiCardsPanel(), 0, 2);

        // Khởi tạo ScottPlot 5
        _formsPlot = new ScottPlot.WinForms.FormsPlot
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8)
        };
        mainLayout.Controls.Add(_formsPlot, 0, 3);

        mainLayout.Controls.Add(CreateFooterPanel(), 0, 4);
        Controls.Add(mainLayout);

        Shown += async (_, _) => await FetchHistoryDataAsync();
    }

    private Control CreateHeaderPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var lblTitle = new AntdUI.Label
        {
            Text = "HISTORICAL PERFORMANCE METRICS",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(15, 23, 42),
            Dock = DockStyle.Fill
        };
        var lblSubtitle = new AntdUI.Label
        {
            Text = $"Host: {_hostname}  •  IP: {_ipAddress}  •  ID: {_agentId}",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = System.Drawing.Color.FromArgb(100, 116, 139),
            Dock = DockStyle.Fill
        };

        panel.Controls.Add(lblTitle, 0, 0);
        panel.Controls.Add(lblSubtitle, 0, 1);
        return panel;
    }

    private Control CreateToolbarPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = System.Drawing.Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Cột 0: Dải thời gian (Range)
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Cột 1: Loại chỉ số (CPU/RAM/Disk)
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Cột 2: Status & Refresh căn phải

        // 1. Nhóm nút dải: 5m, 15m, 1h, 3h, 6h
        var rangePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, // KHÔNG CHO PHÉP XUỐNG DÒNG
            AutoSize = true,
            Margin = new Padding(0, 0, 16, 0)
        };
        var lblRange = new AntdUI.Label
        {
            Text = "Range:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(71, 85, 105),
            AutoSize = true,
            Margin = new Padding(0, 7, 6, 0)
        };
        rangePanel.Controls.Add(lblRange);

        string[] ranges = { "5m", "15m", "1h", "3h", "6h" };
        _rangeButtons = new AntdUI.Button[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            string r = ranges[i];
            var btn = new AntdUI.Button
            {
                Text = r,
                Width = 48,
                Height = 32,
                Radius = 6,
                Margin = new Padding(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Type = (r == _currentRange) ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default
            };
            btn.Click += async (_, _) =>
            {
                if (_currentRange == r) return;
                _currentRange = r;
                UpdateRangeButtonsStyle();
                await FetchHistoryDataAsync();
            };
            _rangeButtons[i] = btn;
            rangePanel.Controls.Add(btn);
        }
        panel.Controls.Add(rangePanel, 0, 0);

        // 2. Nhóm nút chỉ số (CPU / Memory / Disk)
        var metricTypePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, // KHÔNG CHO PHÉP XUỐNG DÒNG
            AutoSize = true,
            Margin = new Padding(0)
        };
        string[] metricNames = { "CPU (%)", "Memory (GB)", "Disk (GB)" };
        MetricViewType[] metricTypes = { MetricViewType.Cpu, MetricViewType.Memory, MetricViewType.Disk };
        _metricTypeButtons = new AntdUI.Button[3];

        for (int i = 0; i < 3; i++)
        {
            var type = metricTypes[i];
            var btn = new AntdUI.Button
            {
                Text = metricNames[i],
                Width = 96,
                Height = 32,
                Radius = 6,
                Margin = new Padding(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Type = (type == _currentMetricType) ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default
            };
            btn.Click += (_, _) =>
            {
                if (_currentMetricType == type) return;
                _currentMetricType = type;
                UpdateMetricTypeButtonsStyle();
                UpdateChartAndKpiDisplay();
            };
            _metricTypeButtons[i] = btn;
            metricTypePanel.Controls.Add(btn);
        }
        panel.Controls.Add(metricTypePanel, 1, 0);

        // 3. Nhóm nút Refresh & Status căn phải
        var rightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, // KHÔNG CHO PHÉP XUỐNG DÒNG
            AutoSize = true,
            Margin = new Padding(0)
        };
        _btnRefresh = new AntdUI.Button
        {
            Text = "Refresh",
            Width = 84,
            Height = 32,
            Radius = 6,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Type = AntdUI.TTypeMini.Primary,
            Cursor = Cursors.Hand,
            Margin = new Padding(4, 0, 0, 0)
        };
        _btnRefresh.Click += async (_, _) => await FetchHistoryDataAsync();

        _lblStatus = new AntdUI.Label
        {
            Text = "Ready",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = System.Drawing.Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0)
        };
        rightPanel.Controls.Add(_btnRefresh);
        rightPanel.Controls.Add(_lblStatus);
        panel.Controls.Add(rightPanel, 2, 0);

        return panel;
    }

    private void UpdateRangeButtonsStyle()
    {
        string[] ranges = { "5m", "15m", "1h", "3h", "6h" };
        for (int i = 0; i < _rangeButtons.Length; i++)
            _rangeButtons[i].Type = (ranges[i] == _currentRange) ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
    }

    private void UpdateMetricTypeButtonsStyle()
    {
        MetricViewType[] types = { MetricViewType.Cpu, MetricViewType.Memory, MetricViewType.Disk };
        for (int i = 0; i < _metricTypeButtons.Length; i++)
            _metricTypeButtons[i].Type = (types[i] == _currentMetricType) ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
    }

    private Control CreateKpiCardsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 6, 0, 6) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));

        panel.Controls.Add(CreateSingleCard("PEAK (MAX)", out _lblPeakValue, out _lblPeakSub), 0, 0);
        panel.Controls.Add(CreateSingleCard("AVERAGE", out _lblAvgValue, out _), 1, 0);
        panel.Controls.Add(CreateSingleCard("MINIMUM", out _lblMinValue, out _), 2, 0);
        return panel;
    }

    private AntdUI.Panel CreateSingleCard(string caption, out AntdUI.Label valLabel, out AntdUI.Label subLabel)
    {
        var card = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 8, Back = System.Drawing.Color.White, BorderWidth = 1F, BorderColor = System.Drawing.Color.FromArgb(226, 232, 240), Margin = new Padding(4, 0, 4, 0), Padding = new Padding(12, 8, 12, 6) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = System.Drawing.Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));

        var lblCap = new AntdUI.Label { Text = caption, Font = new Font("Segoe UI", 7.5F, FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(100, 116, 139), Dock = DockStyle.Fill };
        valLabel = new AntdUI.Label { Text = "—", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(15, 23, 42), Dock = DockStyle.Fill };
        subLabel = new AntdUI.Label { Text = "In selected window", Font = new Font("Segoe UI", 7.5F), ForeColor = System.Drawing.Color.FromArgb(148, 163, 184), Dock = DockStyle.Fill };

        layout.Controls.Add(lblCap, 0, 0);
        layout.Controls.Add(valLabel, 0, 1);
        layout.Controls.Add(subLabel, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private Control CreateFooterPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblNote = new AntdUI.Label { Text = "Powered by ScottPlot 5. Use mouse wheel to Zoom, click & drag to Pan, right-click to Save image.", Font = new Font("Segoe UI", 8F), ForeColor = System.Drawing.Color.FromArgb(148, 163, 184), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var btnClose = new AntdUI.Button { Text = "Close", Width = 90, Height = 34, Radius = 6, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
        btnClose.Click += (_, _) => Close();

        panel.Controls.Add(lblNote, 0, 0);
        panel.Controls.Add(btnClose, 1, 0);
        return panel;
    }

    private async Task FetchHistoryDataAsync()
    {
        //  Giải phóng '_queryCts' khi đóng Form
        if (IsDisposed || Disposing) return;
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        try
        {
            _btnRefresh.Loading = true;
            _lblStatus.Text = "Querying Prometheus...";

            bool success = false;
            if (_metricsClient is not null)
            {
                // 1. Gọi API thực tế thông qua MetricsApiClient (kèm JWT Bearer Token)
                var history = await _metricsClient.GetHostMetricsHistoryAsync(_agentId, _currentRange, ct);
                if (ct.IsCancellationRequested || IsDisposed || Disposing) return;

                if (history is not null && (history.CpuHistory.Count > 0 || history.MemoryHistory.Count > 0 || history.DiskHistory.Count > 0))
                {
                    // 2. CPU: Giữ nguyên tỷ lệ phần trăm (0 - 100%), chuyển timestamp sang Giờ địa phương
                    _cpuPoints = history.CpuHistory
                        .Select(p => new MetricDataPointItem(p.Timestamp.ToLocalTime(), Math.Round(p.Value, 1)))
                        .ToList();

                    // 3. RAM: Đổi từ Byte sang Gigabyte (GB)
                    _ramPoints = history.MemoryHistory
                        .Select(p => new MetricDataPointItem(p.Timestamp.ToLocalTime(), Math.Round(p.Value / (1024.0 * 1024 * 1024), 2)))
                        .ToList();

                    // 4. Disk: Đổi từ Byte sang Gigabyte (GB)
                    _diskPoints = history.DiskHistory
                        .Select(p => new MetricDataPointItem(p.Timestamp.ToLocalTime(), Math.Round(p.Value / (1024.0 * 1024 * 1024), 2)))
                        .ToList();

                    success = true;
                }
            }
            if (IsDisposed || Disposing) return;

            // 5. Nếu không có dữ liệu thật (Agent offline hoặc Prometheus chưa thu thập)
            if (!success)
            {
                _cpuPoints.Clear();
                _ramPoints.Clear();
                _diskPoints.Clear();
                _lblStatus.Text = "No telemetry data recorded in this range";
            }
            else
            {
                _lblStatus.Text = $"Live • Updated: {DateTime.Now:HH:mm:ss}";
            }

            UpdateChartAndKpiDisplay();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            _lblStatus.Text = "Query failed";
            _formsPlot.Plot.Title($"Error: {ex.Message}");
            _formsPlot.Refresh();
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                _btnRefresh.Loading = false;
        }
    }

    //-- Đã từng dùng hàm này để giả lập thời gian để kiểm thử và vẽ đồ thị cho UI
    //private void LoadSimulatedMetricsData()
    //{
    //    int minutes = _currentRange switch { "5m" => 5, "15m" => 15, "1h" => 60, "3h" => 180, "6h" => 360, _ => 15 };
    //    int stepSeconds = Math.Max(5, minutes * 60 / 40);

    //    _cpuPoints.Clear();
    //    _ramPoints.Clear();
    //    _diskPoints.Clear();

    //    var now = DateTime.Now;
    //    var start = now.AddMinutes(-minutes);
    //    var rnd = new Random();
    //    double currentCpu = 25.0;
    //    double currentRamGb = 5.2;

    //    for (var t = start; t <= now; t = t.AddSeconds(stepSeconds))
    //    {
    //        currentCpu = Math.Clamp(currentCpu + (rnd.NextDouble() * 16 - 8), 10.0, 95.0);
    //        currentRamGb = Math.Clamp(currentRamGb + (rnd.NextDouble() * 0.4 - 0.2), 3.0, 14.0);

    //        _cpuPoints.Add(new MetricDataPointItem(t, Math.Round(currentCpu, 1)));
    //        _ramPoints.Add(new MetricDataPointItem(t, Math.Round(currentRamGb, 2)));
    //        _diskPoints.Add(new MetricDataPointItem(t, 82.5));
    //    }
    //}

    private void UpdateChartAndKpiDisplay()
    {
        List<MetricDataPointItem> points = _currentMetricType switch
        {
            MetricViewType.Cpu => _cpuPoints,
            MetricViewType.Memory => _ramPoints,
            MetricViewType.Disk => _diskPoints,
            _ => _cpuPoints
        };

        if (points.Count == 0)
        {
            _lblPeakValue.Text = "—";
            _lblAvgValue.Text = "—";
            _lblMinValue.Text = "—";
            _formsPlot.Plot.Clear();
            _formsPlot.Refresh();
            return;
        }

        double max = points.Max(p => p.Value);
        double min = points.Min(p => p.Value);
        double avg = points.Average(p => p.Value);
        string unit = _currentMetricType == MetricViewType.Cpu ? "%" : " GB";

        _lblPeakValue.Text = $"{max:F1}{unit}";
        _lblAvgValue.Text = $"{avg:F1}{unit}";
        _lblMinValue.Text = $"{min:F1}{unit}";

        if (_currentMetricType == MetricViewType.Cpu)
        {
            if (max >= 85)
            {
                _lblPeakValue.ForeColor = System.Drawing.Color.FromArgb(239, 68, 68);
                if (_lblPeakSub is not null) _lblPeakSub.Text = "Critical spike observed";
            }
            else if (max >= 60)
            {
                _lblPeakValue.ForeColor = System.Drawing.Color.FromArgb(245, 158, 11);
                if (_lblPeakSub is not null) _lblPeakSub.Text = "Moderate load spike";
            }
            else
            {
                _lblPeakValue.ForeColor = System.Drawing.Color.FromArgb(34, 197, 94);
                if (_lblPeakSub is not null) _lblPeakSub.Text = "Optimal performance";
            }
        }
        else
        {
            _lblPeakValue.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            if (_lblPeakSub is not null) _lblPeakSub.Text = "In selected window";
        }

        // Render biểu đồ ScottPlot 5
        _formsPlot.Plot.Clear();
        double[] xs = points.Select(p => p.Timestamp.ToOADate()).ToArray();
        double[] ys = points.Select(p => p.Value).ToArray();

        var scatter = _formsPlot.Plot.Add.Scatter(xs, ys);
        scatter.LineWidth = 2.5f;
        scatter.Color = _currentMetricType == MetricViewType.Cpu
            ? ScottPlot.Color.FromHex("#2563EB")
            : ScottPlot.Color.FromHex("#10B981");

        if (_currentMetricType == MetricViewType.Cpu)
        {
            var lineWarn = _formsPlot.Plot.Add.HorizontalLine(60);
            lineWarn.Color = ScottPlot.Color.FromHex("#F59E0B");
            lineWarn.LinePattern = ScottPlot.LinePattern.Dashed;

            var lineCrit = _formsPlot.Plot.Add.HorizontalLine(85);
            lineCrit.Color = ScottPlot.Color.FromHex("#EF4444");
            lineCrit.LinePattern = ScottPlot.LinePattern.Dashed;

            _formsPlot.Plot.Axes.SetLimitsY(0, 100);
        }
        else
        {
            _formsPlot.Plot.Axes.AutoScale();
        }

        _formsPlot.Plot.Axes.DateTimeTicksBottom();
        _formsPlot.Refresh();
    }
}