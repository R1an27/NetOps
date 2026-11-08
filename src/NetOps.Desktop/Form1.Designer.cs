namespace NetOps.Desktop;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        shellLayout = new TableLayoutPanel();
        titleBar = new AntdUI.PageHeader();
        contentLayout = new TableLayoutPanel();
        headingLayout = new TableLayoutPanel();
        headingText = new TableLayoutPanel();
        lblEyebrow = new AntdUI.Label();
        lblTitle = new AntdUI.Label();
        lblSubtitle = new AntdUI.Label();
        connectionPanel = new AntdUI.Panel();
        connectionLayout = new TableLayoutPanel();
        lblServer = new AntdUI.Label();
        badgeConnection = new AntdUI.Badge();
        btnConnect = new AntdUI.Button();
        bodyLayout = new TableLayoutPanel();
        commandPanel = new AntdUI.Panel();
        commandLayout = new TableLayoutPanel();
        lblDetails = new AntdUI.Label();
        lblAgentId = new AntdUI.Label();
        txtAgentId = new AntdUI.Input();
        lblCommand = new AntdUI.Label();
        txtCommand = new AntdUI.Input();
        timeoutLayout = new TableLayoutPanel();
        timeoutText = new TableLayoutPanel();
        lblTimeout = new AntdUI.Label();
        lblTimeoutHint = new AntdUI.Label();
        nudTimeout = new AntdUI.InputNumber();
        btnSend = new AntdUI.Button();
        lblSendHint = new AntdUI.Label();
        outputPanel = new AntdUI.Panel();
        outputLayout = new TableLayoutPanel();
        outputHeader = new TableLayoutPanel();
        lblTerminalIcon = new AntdUI.Label();
        lblResult = new AntdUI.Label();
        btnCopy = new AntdUI.Button();
        btnClear = new AntdUI.Button();
        outputDivider = new System.Windows.Forms.Panel();
        outputFooterDivider = new System.Windows.Forms.Panel();
        outputHost = new System.Windows.Forms.Panel();
        txtResult = new AntdUI.Input();
        emptyLayout = new TableLayoutPanel();
        emptyContent = new TableLayoutPanel();
        lblEmptyIcon = new System.Windows.Forms.Label();
        lblEmptyTitle = new AntdUI.Label();
        lblEmptyHint = new AntdUI.Label();
        outputFooter = new TableLayoutPanel();
        badgeOutput = new AntdUI.Badge();
        btnFollow = new AntdUI.Button();
        footerLayout = new TableLayoutPanel();
        lblStatus = new AntdUI.Label();
        lblOutputCount = new AntdUI.Label();
        shellLayout.SuspendLayout();
        contentLayout.SuspendLayout();
        headingLayout.SuspendLayout();
        headingText.SuspendLayout();
        connectionPanel.SuspendLayout();
        connectionLayout.SuspendLayout();
        bodyLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        commandLayout.SuspendLayout();
        timeoutLayout.SuspendLayout();
        timeoutText.SuspendLayout();
        outputPanel.SuspendLayout();
        outputLayout.SuspendLayout();
        outputHeader.SuspendLayout();
        outputHost.SuspendLayout();
        emptyLayout.SuspendLayout();
        emptyContent.SuspendLayout();
        outputFooter.SuspendLayout();
        footerLayout.SuspendLayout();
        SuspendLayout();
        shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // shellLayout
        shellLayout.Dock = DockStyle.Fill;
        shellLayout.Margin = new Padding(0);
        shellLayout.Name = "shellLayout";
        shellLayout.ColumnCount = 1;
        shellLayout.RowCount = 2;
        shellLayout.Controls.Add(titleBar, 0, 0);
        shellLayout.Controls.Add(contentLayout, 0, 1);

        // titleBar
        titleBar.Dock = DockStyle.Fill;
        titleBar.Margin = new Padding(0);
        titleBar.Name = "titleBar";
        titleBar.Text = "NetOps";
        titleBar.SubText = "/  Remote operations";
        titleBar.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        titleBar.SubFont = new Font("Segoe UI", 9F);
        titleBar.BackColor = Color.FromArgb(16, 28, 48);
        titleBar.ForeColor = Color.FromArgb(241, 245, 249);
        titleBar.ColorScheme = AntdUI.TAMode.Dark;
        titleBar.ShowIcon = true;
        titleBar.IconSvg = "<svg viewBox=\"0 0 32 32\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"32\" height=\"32\" rx=\"9\" fill=\"#2563EB\"/><path d=\"m8 9 2-2 9 9-9 9-2-2 7-7-7-7zm11 13h7v3h-7v-3z\" fill=\"#FFFFFF\"/></svg>";
        titleBar.IconRatio = 0.65F;
        titleBar.ShowButton = true;
        titleBar.UseForeColorDrawIcons = true;
        titleBar.CloseSize = 48;
        titleBar.Padding = new Padding(20, 0, 0, 0);

        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        // contentLayout
        contentLayout.Dock = DockStyle.Fill;
        contentLayout.Margin = new Padding(0);
        contentLayout.Name = "contentLayout";
        contentLayout.ColumnCount = 1;
        contentLayout.RowCount = 5;
        contentLayout.Padding = new Padding(28, 24, 28, 16);
        contentLayout.Controls.Add(headingLayout, 0, 0);
        contentLayout.Controls.Add(bodyLayout, 0, 2);
        contentLayout.Controls.Add(footerLayout, 0, 4);

        headingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320F));
        headingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // headingLayout
        headingLayout.Dock = DockStyle.Fill;
        headingLayout.Margin = new Padding(0);
        headingLayout.Name = "headingLayout";
        headingLayout.ColumnCount = 2;
        headingLayout.RowCount = 1;
        headingLayout.Controls.Add(headingText, 0, 0);
        headingLayout.Controls.Add(connectionPanel, 1, 0);

        headingText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        // headingText
        headingText.Dock = DockStyle.Fill;
        headingText.Margin = new Padding(0);
        headingText.Name = "headingText";
        headingText.ColumnCount = 1;
        headingText.RowCount = 3;
        headingText.Controls.Add(lblEyebrow, 0, 0);
        headingText.Controls.Add(lblTitle, 0, 1);
        headingText.Controls.Add(lblSubtitle, 0, 2);

        // lblEyebrow
        lblEyebrow.Dock = DockStyle.Fill;
        lblEyebrow.Margin = new Padding(0);
        lblEyebrow.Name = "lblEyebrow";
        lblEyebrow.Text = "WORKSPACE  /  COMMANDS";
        lblEyebrow.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblEyebrow.ForeColor = Color.FromArgb(100, 116, 139);
        lblEyebrow.TextMultiLine = false;

        // lblTitle
        lblTitle.Dock = DockStyle.Fill;
        lblTitle.Margin = new Padding(0);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "Command Center";
        lblTitle.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(21, 35, 57);
        lblTitle.TextMultiLine = false;

        // lblSubtitle
        lblSubtitle.Dock = DockStyle.Fill;
        lblSubtitle.Margin = new Padding(0);
        lblSubtitle.Name = "lblSubtitle";
        lblSubtitle.Text = "One workspace. Every connected agent.";
        lblSubtitle.Font = new Font("Segoe UI", 10F);
        lblSubtitle.ForeColor = Color.FromArgb(95, 112, 135);
        lblSubtitle.TextMultiLine = false;

        // connectionPanel
        connectionPanel.Dock = DockStyle.Fill;
        connectionPanel.Margin = new Padding(0, 6, 0, 6);
        connectionPanel.Name = "connectionPanel";
        connectionPanel.Back = Color.FromArgb(255, 255, 255);
        connectionPanel.Radius = 10;
        connectionPanel.Padding = new Padding(14, 10, 10, 10);
        connectionPanel.BorderWidth = 1F;
        connectionPanel.BorderColor = Color.FromArgb(220, 228, 238);
        connectionPanel.Controls.Add(connectionLayout);

        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        // connectionLayout
        connectionLayout.Dock = DockStyle.Fill;
        connectionLayout.Margin = new Padding(0);
        connectionLayout.Name = "connectionLayout";
        connectionLayout.ColumnCount = 2;
        connectionLayout.RowCount = 2;
        connectionLayout.BackColor = Color.White;
        connectionLayout.Controls.Add(lblServer, 0, 0);
        connectionLayout.Controls.Add(badgeConnection, 0, 1);
        connectionLayout.Controls.Add(btnConnect, 1, 0);

        connectionLayout.SetRowSpan(btnConnect, 2);

        // lblServer
        lblServer.Dock = DockStyle.Fill;
        lblServer.Margin = new Padding(0);
        lblServer.Name = "lblServer";
        lblServer.Text = "localhost:7027";
        lblServer.Font = new Font("Segoe UI", 9F);
        lblServer.ForeColor = Color.FromArgb(51, 65, 85);
        lblServer.TextMultiLine = false;
        lblServer.PrefixSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M3 3h18v8H3V3zm1.5 1.5v5h15v-5h-15zM3 13h18v8H3v-8zm1.5 1.5v5h15v-5h-15zM6 6h2v2H6V6zm0 10h2v2H6v-2zm6-10h6v1.5h-6V6zm0 10h6v1.5h-6V16z\"/></svg>";
        lblServer.PrefixColor = Color.FromArgb(100, 116, 139);
        lblServer.IconGap = 6;

        // badgeConnection
        badgeConnection.Dock = DockStyle.Fill;
        badgeConnection.Margin = new Padding(0);
        badgeConnection.Name = "badgeConnection";
        badgeConnection.Text = "Not connected";
        badgeConnection.Font = new Font("Segoe UI", 9F);
        badgeConnection.ForeColor = Color.FromArgb(100, 116, 139);
        badgeConnection.State = AntdUI.TState.Default;
        badgeConnection.Gap = 5;
        badgeConnection.DotRatio = 0.25F;

        // btnConnect
        btnConnect.Dock = DockStyle.Fill;
        btnConnect.Margin = new Padding(0);
        btnConnect.Name = "btnConnect";
        btnConnect.Text = "Connect";
        btnConnect.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M19 3v3.1A9 9 0 0 0 3 12h2a7 7 0 0 1 12.4-4.5H14v2h7V3h-2zM5 21v-3.1A9 9 0 0 0 21 12h-2a7 7 0 0 1-12.4 4.5H10v-2H3V21h2z\"/></svg>";
        btnConnect.IconSize = new Size(16, 16);
        btnConnect.Radius = 7;
        btnConnect.Cursor = Cursors.Hand;
        btnConnect.Font = new Font("Segoe UI", 9F);
        btnConnect.BorderWidth = 1F;
        btnConnect.DefaultBack = Color.FromArgb(248, 250, 252);
        btnConnect.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnConnect.ForeColor = Color.FromArgb(51, 65, 85);
        btnConnect.BackHover = Color.FromArgb(239, 244, 252);
        btnConnect.Click += btnConnect_Click;

        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // bodyLayout
        bodyLayout.Dock = DockStyle.Fill;
        bodyLayout.Margin = new Padding(0);
        bodyLayout.Name = "bodyLayout";
        bodyLayout.ColumnCount = 3;
        bodyLayout.RowCount = 1;
        bodyLayout.Controls.Add(commandPanel, 0, 0);
        bodyLayout.Controls.Add(outputPanel, 2, 0);

        // commandPanel
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.Margin = new Padding(0);
        commandPanel.Name = "commandPanel";
        commandPanel.Back = Color.FromArgb(255, 255, 255);
        commandPanel.Radius = 12;
        commandPanel.Padding = new Padding(22);
        commandPanel.BorderWidth = 1F;
        commandPanel.BorderColor = Color.FromArgb(220, 228, 238);
        commandPanel.Controls.Add(commandLayout);

        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        // commandLayout
        commandLayout.Dock = DockStyle.Fill;
        commandLayout.Margin = new Padding(0);
        commandLayout.Name = "commandLayout";
        commandLayout.ColumnCount = 1;
        commandLayout.RowCount = 12;
        commandLayout.BackColor = Color.FromArgb(255, 255, 255);
        commandLayout.Controls.Add(lblDetails, 0, 0);
        commandLayout.Controls.Add(lblAgentId, 0, 2);
        commandLayout.Controls.Add(txtAgentId, 0, 3);
        commandLayout.Controls.Add(lblCommand, 0, 5);
        commandLayout.Controls.Add(txtCommand, 0, 6);
        commandLayout.Controls.Add(timeoutLayout, 0, 8);
        commandLayout.Controls.Add(btnSend, 0, 10);
        commandLayout.Controls.Add(lblSendHint, 0, 11);

        // lblDetails
        lblDetails.Dock = DockStyle.Fill;
        lblDetails.Margin = new Padding(0);
        lblDetails.Name = "lblDetails";
        lblDetails.Text = "Compose command";
        lblDetails.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        lblDetails.ForeColor = Color.FromArgb(21, 35, 57);
        lblDetails.TextMultiLine = false;
        lblDetails.PrefixSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M4.7 6.7 6.1 5.3 12.8 12 6.1 18.7 4.7 17.3 10 12 4.7 6.7zM13 17h7v2h-7v-2z\"/></svg>";
        lblDetails.PrefixColor = Color.FromArgb(37, 99, 235);
        lblDetails.IconGap = 9;
        lblDetails.IconRatio = 0.85F;

        // lblAgentId
        lblAgentId.Dock = DockStyle.Fill;
        lblAgentId.Margin = new Padding(0);
        lblAgentId.Name = "lblAgentId";
        lblAgentId.Text = "01   AGENT ID";
        lblAgentId.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblAgentId.ForeColor = Color.FromArgb(98, 115, 138);
        lblAgentId.TextMultiLine = false;

        // txtAgentId
        txtAgentId.Dock = DockStyle.Fill;
        txtAgentId.Margin = new Padding(0);
        txtAgentId.Name = "txtAgentId";
        txtAgentId.Radius = 8;
        txtAgentId.BorderColor = Color.FromArgb(207, 216, 229);
        txtAgentId.BorderHover = Color.FromArgb(148, 163, 184);
        txtAgentId.BorderActive = Color.FromArgb(37, 99, 235);
        txtAgentId.PlaceholderText = "Enter a registered agent ID";
        txtAgentId.PlaceholderColor = Color.FromArgb(121, 135, 155);
        txtAgentId.PrefixSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M3 3h18v8H3V3zm1.5 1.5v5h15v-5h-15zM3 13h18v8H3v-8zm1.5 1.5v5h15v-5h-15zM6 6h2v2H6V6zm0 10h2v2H6v-2zm6-10h6v1.5h-6V6zm0 10h6v1.5h-6V16z\"/></svg>";
        txtAgentId.PrefixFore = Color.FromArgb(129, 144, 165);
        txtAgentId.AllowClear = true;
        txtAgentId.TabIndex = 0;
        txtAgentId.AccessibleName = "Agent ID";
        txtAgentId.SuffixText = "Browse";
        txtAgentId.SuffixFore = Color.FromArgb(37, 99, 235);
        txtAgentId.SuffixClick += txtAgentId_SuffixClick;
        txtAgentId.TextChanged += commandField_TextChanged;

        // lblCommand
        lblCommand.Dock = DockStyle.Fill;
        lblCommand.Margin = new Padding(0);
        lblCommand.Name = "lblCommand";
        lblCommand.Text = "02   SHELL COMMAND";
        lblCommand.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblCommand.ForeColor = Color.FromArgb(98, 115, 138);
        lblCommand.TextMultiLine = false;

        // txtCommand
        txtCommand.Dock = DockStyle.Fill;
        txtCommand.Margin = new Padding(0);
        txtCommand.Name = "txtCommand";
        txtCommand.Radius = 8;
        txtCommand.BorderColor = Color.FromArgb(207, 216, 229);
        txtCommand.BorderHover = Color.FromArgb(148, 163, 184);
        txtCommand.BorderActive = Color.FromArgb(37, 99, 235);
        txtCommand.BackColor = Color.FromArgb(248, 250, 253);
        txtCommand.ForeColor = Color.FromArgb(36, 54, 77);
        txtCommand.Font = new Font("Consolas", 10F);
        txtCommand.PlaceholderText = "Enter your command here...\r\n\r\ne.g. hostname";
        txtCommand.PlaceholderColor = Color.FromArgb(121, 135, 155);
        txtCommand.Multiline = true;
        txtCommand.AutoScroll = true;
        txtCommand.TabIndex = 1;
        txtCommand.AccessibleName = "Shell command";
        txtCommand.TextChanged += commandField_TextChanged;

        timeoutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        timeoutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
        timeoutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // timeoutLayout
        timeoutLayout.Dock = DockStyle.Fill;
        timeoutLayout.Margin = new Padding(0);
        timeoutLayout.Name = "timeoutLayout";
        timeoutLayout.ColumnCount = 2;
        timeoutLayout.RowCount = 1;
        timeoutLayout.BackColor = Color.FromArgb(255, 255, 255);
        timeoutLayout.Controls.Add(timeoutText, 0, 0);
        timeoutLayout.Controls.Add(nudTimeout, 1, 0);

        timeoutText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        timeoutText.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        timeoutText.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // timeoutText
        timeoutText.Dock = DockStyle.Fill;
        timeoutText.Margin = new Padding(0);
        timeoutText.Name = "timeoutText";
        timeoutText.ColumnCount = 1;
        timeoutText.RowCount = 2;
        timeoutText.BackColor = Color.FromArgb(255, 255, 255);
        timeoutText.Controls.Add(lblTimeout, 0, 0);
        timeoutText.Controls.Add(lblTimeoutHint, 0, 1);

        // lblTimeout
        lblTimeout.Dock = DockStyle.Fill;
        lblTimeout.Margin = new Padding(0);
        lblTimeout.Name = "lblTimeout";
        lblTimeout.Text = "Timeout";
        lblTimeout.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblTimeout.ForeColor = Color.FromArgb(51, 65, 85);
        lblTimeout.TextMultiLine = false;

        // lblTimeoutHint
        lblTimeoutHint.Dock = DockStyle.Fill;
        lblTimeoutHint.Margin = new Padding(0);
        lblTimeoutHint.Name = "lblTimeoutHint";
        lblTimeoutHint.Text = "Execution time limit";
        lblTimeoutHint.Font = new Font("Segoe UI", 8F);
        lblTimeoutHint.ForeColor = Color.FromArgb(100, 116, 139);
        lblTimeoutHint.TextMultiLine = false;

        // nudTimeout
        nudTimeout.Dock = DockStyle.Fill;
        nudTimeout.Margin = new Padding(0);
        nudTimeout.Name = "nudTimeout";
        nudTimeout.Radius = 8;
        nudTimeout.Minimum = 1;
        nudTimeout.Maximum = 3600;
        nudTimeout.Value = 60;
        nudTimeout.EnabledValueTextChange = true;
        nudTimeout.AlwaysShowControl = true;
        nudTimeout.WheelModifyEnabled = false;
        nudTimeout.SuffixText = "sec";
        nudTimeout.SuffixFore = Color.FromArgb(100, 116, 139);
        nudTimeout.BorderColor = Color.FromArgb(207, 216, 229);
        nudTimeout.BorderActive = Color.FromArgb(37, 99, 235);
        nudTimeout.TabIndex = 2;
        nudTimeout.AccessibleName = "Command timeout in seconds";

        // btnSend
        btnSend.Dock = DockStyle.Fill;
        btnSend.Margin = new Padding(0);
        btnSend.Name = "btnSend";
        btnSend.Text = "Send command";
        btnSend.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M4 11h12.2l-4.6-4.6L13 5l7 7-7 7-1.4-1.4 4.6-4.6H4v-2z\"/></svg>";
        btnSend.IconSize = new Size(18, 18);
        btnSend.Radius = 7;
        btnSend.Cursor = Cursors.Hand;
        btnSend.Type = AntdUI.TTypeMini.Primary;
        btnSend.BackColor = Color.FromArgb(37, 99, 235);
        btnSend.BackHover = Color.FromArgb(29, 78, 216);
        btnSend.BackActive = Color.FromArgb(30, 64, 175);
        btnSend.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnSend.IconPosition = AntdUI.TAlignMini.Right;
        btnSend.IconGap = 0.6F;
        btnSend.Enabled = false;
        btnSend.TabIndex = 3;
        btnSend.Click += btnSend_Click;

        // lblSendHint
        lblSendHint.Dock = DockStyle.Fill;
        lblSendHint.Margin = new Padding(0);
        lblSendHint.Name = "lblSendHint";
        lblSendHint.Text = "Ctrl + Enter to send";
        lblSendHint.Font = new Font("Segoe UI", 8F);
        lblSendHint.ForeColor = Color.FromArgb(100, 116, 139);
        lblSendHint.TextMultiLine = false;
        lblSendHint.TextAlign = ContentAlignment.MiddleCenter;

        // outputPanel
        outputPanel.Dock = DockStyle.Fill;
        outputPanel.Margin = new Padding(0);
        outputPanel.Name = "outputPanel";
        outputPanel.Back = Color.FromArgb(11, 20, 35);
        outputPanel.Radius = 12;
        outputPanel.Padding = new Padding(20, 12, 20, 12);
        outputPanel.BorderWidth = 1F;
        outputPanel.BorderColor = Color.FromArgb(29, 44, 67);
        outputPanel.Controls.Add(outputLayout);

        outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        // outputLayout
        outputLayout.Dock = DockStyle.Fill;
        outputLayout.Margin = new Padding(0);
        outputLayout.Name = "outputLayout";
        outputLayout.ColumnCount = 1;
        outputLayout.RowCount = 5;
        outputLayout.BackColor = Color.FromArgb(11, 20, 35);
        outputLayout.Controls.Add(outputHeader, 0, 0);
        outputLayout.Controls.Add(outputDivider, 0, 1);
        outputLayout.Controls.Add(outputHost, 0, 2);
        outputLayout.Controls.Add(outputFooterDivider, 0, 3);
        outputLayout.Controls.Add(outputFooter, 0, 4);

        outputHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
        outputHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outputHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        outputHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        outputHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // outputHeader
        outputHeader.Dock = DockStyle.Fill;
        outputHeader.Margin = new Padding(0);
        outputHeader.Name = "outputHeader";
        outputHeader.ColumnCount = 4;
        outputHeader.RowCount = 1;
        outputHeader.BackColor = Color.FromArgb(11, 20, 35);
        outputHeader.Controls.Add(lblTerminalIcon, 0, 0);
        outputHeader.Controls.Add(lblResult, 1, 0);
        outputHeader.Controls.Add(btnCopy, 2, 0);
        outputHeader.Controls.Add(btnClear, 3, 0);

        // lblTerminalIcon
        lblTerminalIcon.Dock = DockStyle.Fill;
        lblTerminalIcon.Margin = new Padding(0);
        lblTerminalIcon.Name = "lblTerminalIcon";
        lblTerminalIcon.Text = ">_";
        lblTerminalIcon.Font = new Font("Consolas", 13F, FontStyle.Bold);
        lblTerminalIcon.ForeColor = Color.FromArgb(126, 181, 255);
        lblTerminalIcon.TextMultiLine = false;

        // lblResult
        lblResult.Dock = DockStyle.Fill;
        lblResult.Margin = new Padding(0);
        lblResult.Name = "lblResult";
        lblResult.Text = "Command Output";
        lblResult.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        lblResult.ForeColor = Color.FromArgb(237, 243, 252);
        lblResult.TextMultiLine = false;

        // btnCopy
        btnCopy.Dock = DockStyle.Fill;
        btnCopy.Margin = new Padding(3, 6, 0, 6);
        btnCopy.Name = "btnCopy";
        btnCopy.Text = "Copy";
        btnCopy.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M8 7h12v14H8V7zm1.7 1.7v10.6h8.6V8.7H9.7zM4 3h12v2H6v12H4V3z\"/></svg>";
        btnCopy.IconSize = new Size(16, 16);
        btnCopy.Radius = 7;
        btnCopy.Cursor = Cursors.Hand;
        btnCopy.ColorScheme = AntdUI.TAMode.Dark;
        btnCopy.DefaultBack = Color.FromArgb(20, 33, 54);
        btnCopy.DefaultBorderColor = Color.FromArgb(44, 61, 85);
        btnCopy.BorderWidth = 1F;
        btnCopy.ForeColor = Color.FromArgb(194, 207, 225);
        btnCopy.BackHover = Color.FromArgb(34, 51, 77);
        btnCopy.ForeHover = Color.FromArgb(255, 255, 255);
        btnCopy.Font = new Font("Segoe UI", 9F);
        btnCopy.Enabled = false;
        btnCopy.TabIndex = 4;
        btnCopy.Click += btnCopy_Click;

        // btnClear
        btnClear.Dock = DockStyle.Fill;
        btnClear.Margin = new Padding(3, 6, 0, 6);
        btnClear.Name = "btnClear";
        btnClear.Text = "Clear";
        btnClear.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M9 3h6v3h5v2H4V6h5V3zm2 2v1h2V5zM6 9h12l-1 12H7L6 9zm2.2 2 .7 8h6.2l.7-8H8.2z\"/></svg>";
        btnClear.IconSize = new Size(16, 16);
        btnClear.Radius = 7;
        btnClear.Cursor = Cursors.Hand;
        btnClear.ColorScheme = AntdUI.TAMode.Dark;
        btnClear.DefaultBack = Color.FromArgb(20, 33, 54);
        btnClear.DefaultBorderColor = Color.FromArgb(44, 61, 85);
        btnClear.BorderWidth = 1F;
        btnClear.ForeColor = Color.FromArgb(194, 207, 225);
        btnClear.BackHover = Color.FromArgb(34, 51, 77);
        btnClear.ForeHover = Color.FromArgb(255, 255, 255);
        btnClear.Font = new Font("Segoe UI", 9F);
        btnClear.Enabled = false;
        btnClear.TabIndex = 5;
        btnClear.Click += btnClear_Click;

        // outputDivider
        outputDivider.Dock = DockStyle.Fill;
        outputDivider.Margin = new Padding(0);
        outputDivider.Name = "outputDivider";
        outputDivider.BackColor = Color.FromArgb(34, 48, 71);

        // outputFooterDivider
        outputFooterDivider.Dock = DockStyle.Fill;
        outputFooterDivider.Margin = new Padding(0);
        outputFooterDivider.Name = "outputFooterDivider";
        outputFooterDivider.BackColor = Color.FromArgb(34, 48, 71);

        // outputHost
        outputHost.Dock = DockStyle.Fill;
        outputHost.Margin = new Padding(0);
        outputHost.Name = "outputHost";
        outputHost.BackColor = Color.FromArgb(11, 20, 35);
        outputHost.Padding = new Padding(0, 14, 0, 14);
        outputHost.Controls.Add(txtResult);
        outputHost.Controls.Add(emptyLayout);

        // txtResult
        txtResult.Dock = DockStyle.Fill;
        txtResult.Margin = new Padding(0);
        txtResult.Name = "txtResult";
        txtResult.Font = new Font("Consolas", 10F);
        txtResult.ColorScheme = AntdUI.TAMode.Dark;
        txtResult.BackColor = Color.FromArgb(11, 20, 35);
        txtResult.ForeColor = Color.FromArgb(212, 226, 246);
        txtResult.SelectionColor = Color.FromArgb(52, 87, 134);
        txtResult.BorderWidth = 0F;
        txtResult.WaveSize = 0;
        txtResult.Radius = 0;
        txtResult.Multiline = true;
        txtResult.AutoScroll = true;
        txtResult.ReadOnly = true;
        txtResult.MaxLength = int.MaxValue;
        txtResult.TabIndex = 6;
        txtResult.AccessibleName = "Command output";

        emptyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        emptyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340F));
        emptyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210F));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        // emptyLayout
        emptyLayout.Dock = DockStyle.Fill;
        emptyLayout.Margin = new Padding(0);
        emptyLayout.Name = "emptyLayout";
        emptyLayout.ColumnCount = 3;
        emptyLayout.RowCount = 3;
        emptyLayout.BackColor = Color.FromArgb(11, 20, 35);
        emptyLayout.Controls.Add(emptyContent, 1, 1);

        emptyContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        emptyContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        emptyContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        emptyContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        emptyContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        // emptyContent
        emptyContent.Dock = DockStyle.Fill;
        emptyContent.Margin = new Padding(0);
        emptyContent.Name = "emptyContent";
        emptyContent.ColumnCount = 1;
        emptyContent.RowCount = 4;
        emptyContent.BackColor = Color.FromArgb(11, 20, 35);
        emptyContent.Controls.Add(lblEmptyIcon, 0, 0);
        emptyContent.Controls.Add(lblEmptyTitle, 0, 2);
        emptyContent.Controls.Add(lblEmptyHint, 0, 3);

        // lblEmptyIcon
        lblEmptyIcon.Dock = DockStyle.Fill;
        lblEmptyIcon.Margin = new Padding(0);
        lblEmptyIcon.Name = "lblEmptyIcon";
        lblEmptyIcon.Text = ">_";
        lblEmptyIcon.Font = new Font("Consolas", 27F, FontStyle.Bold);
        lblEmptyIcon.ForeColor = Color.FromArgb(126, 181, 255);
        lblEmptyIcon.BackColor = Color.FromArgb(11, 20, 35);
        lblEmptyIcon.TextAlign = ContentAlignment.MiddleCenter;

        // lblEmptyTitle
        lblEmptyTitle.Dock = DockStyle.Fill;
        lblEmptyTitle.Margin = new Padding(0);
        lblEmptyTitle.Name = "lblEmptyTitle";
        lblEmptyTitle.Text = "Your output starts here";
        lblEmptyTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        lblEmptyTitle.ForeColor = Color.FromArgb(226, 234, 247);
        lblEmptyTitle.TextMultiLine = false;
        lblEmptyTitle.TextAlign = ContentAlignment.MiddleCenter;

        // lblEmptyHint
        lblEmptyHint.Dock = DockStyle.Fill;
        lblEmptyHint.Margin = new Padding(0);
        lblEmptyHint.Name = "lblEmptyHint";
        lblEmptyHint.Text = "Choose an agent and send a command.\nIts output will appear here.";
        lblEmptyHint.Font = new Font("Segoe UI", 10F);
        lblEmptyHint.ForeColor = Color.FromArgb(143, 162, 189);
        lblEmptyHint.TextMultiLine = true;
        lblEmptyHint.TextAlign = ContentAlignment.TopCenter;

        outputFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outputFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144F));
        outputFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // outputFooter
        outputFooter.Dock = DockStyle.Fill;
        outputFooter.Margin = new Padding(0);
        outputFooter.Name = "outputFooter";
        outputFooter.ColumnCount = 2;
        outputFooter.RowCount = 1;
        outputFooter.BackColor = Color.FromArgb(11, 20, 35);
        outputFooter.Controls.Add(badgeOutput, 0, 0);
        outputFooter.Controls.Add(btnFollow, 1, 0);

        // badgeOutput
        badgeOutput.Dock = DockStyle.Fill;
        badgeOutput.Margin = new Padding(0);
        badgeOutput.Name = "badgeOutput";
        badgeOutput.Font = new Font("Segoe UI", 9F);
        badgeOutput.ForeColor = Color.FromArgb(148, 167, 192);
        badgeOutput.State = AntdUI.TState.Default;
        badgeOutput.Text = "Waiting for output";
        badgeOutput.Gap = 5;
        badgeOutput.DotRatio = 0.25F;
        badgeOutput.ColorScheme = AntdUI.TAMode.Dark;

        // btnFollow
        btnFollow.Dock = DockStyle.Fill;
        btnFollow.Margin = new Padding(0, 4, 0, 0);
        btnFollow.Name = "btnFollow";
        btnFollow.Text = "Follow output";
        btnFollow.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M11 4h2v12.2l4.6-4.6L19 13l-7 7-7-7 1.4-1.4 4.6 4.6V4z\"/></svg>";
        btnFollow.IconSize = new Size(16, 16);
        btnFollow.Radius = 7;
        btnFollow.Cursor = Cursors.Hand;
        btnFollow.ColorScheme = AntdUI.TAMode.Dark;
        btnFollow.Font = new Font("Segoe UI", 9F);
        btnFollow.DefaultBack = Color.FromArgb(11, 20, 35);
        btnFollow.ForeColor = Color.FromArgb(148, 167, 192);
        btnFollow.BackHover = Color.FromArgb(34, 51, 77);
        btnFollow.AutoToggle = true;
        btnFollow.Toggle = true;
        btnFollow.ToggleBack = Color.FromArgb(23, 43, 72);
        btnFollow.ToggleFore = Color.FromArgb(147, 197, 253);
        btnFollow.TabIndex = 7;
        btnFollow.AccessibleName = "Automatically scroll to new output";

        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // footerLayout
        footerLayout.Dock = DockStyle.Fill;
        footerLayout.Margin = new Padding(0);
        footerLayout.Name = "footerLayout";
        footerLayout.ColumnCount = 2;
        footerLayout.RowCount = 1;
        footerLayout.Controls.Add(lblStatus, 0, 0);
        footerLayout.Controls.Add(lblOutputCount, 1, 0);

        // lblStatus
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Margin = new Padding(0);
        lblStatus.Name = "lblStatus";
        lblStatus.Text = "Ready. Connect to the server to get started.";
        lblStatus.Font = new Font("Segoe UI", 9F);
        lblStatus.ForeColor = Color.FromArgb(97, 115, 139);
        lblStatus.TextMultiLine = false;
        lblStatus.AutoEllipsis = true;
        lblStatus.ShowTooltip = true;

        // lblOutputCount
        lblOutputCount.Dock = DockStyle.Fill;
        lblOutputCount.Margin = new Padding(0);
        lblOutputCount.Name = "lblOutputCount";
        lblOutputCount.Text = "0 lines";
        lblOutputCount.Font = new Font("Segoe UI", 9F);
        lblOutputCount.ForeColor = Color.FromArgb(97, 115, 139);
        lblOutputCount.TextMultiLine = false;
        lblOutputCount.TextAlign = ContentAlignment.MiddleRight;

        // Form1
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoHandDpi = false;
        DisableTheme = true;
        BackColor = Color.FromArgb(243, 246, 250);
        ForeColor = Color.FromArgb(21, 35, 57);
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1240, 800);
        MinimumSize = new Size(1040, 744);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(shellLayout);
        Name = "Form1";
        Text = "NetOps | Command Center";
        footerLayout.ResumeLayout(false);
        outputFooter.ResumeLayout(false);
        emptyContent.ResumeLayout(false);
        emptyLayout.ResumeLayout(false);
        outputHost.ResumeLayout(false);
        outputHeader.ResumeLayout(false);
        outputLayout.ResumeLayout(false);
        outputPanel.ResumeLayout(false);
        timeoutText.ResumeLayout(false);
        timeoutLayout.ResumeLayout(false);
        commandLayout.ResumeLayout(false);
        commandPanel.ResumeLayout(false);
        bodyLayout.ResumeLayout(false);
        connectionLayout.ResumeLayout(false);
        connectionPanel.ResumeLayout(false);
        headingText.ResumeLayout(false);
        headingLayout.ResumeLayout(false);
        contentLayout.ResumeLayout(false);
        shellLayout.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel shellLayout;
    private AntdUI.PageHeader titleBar;
    private TableLayoutPanel contentLayout;
    private TableLayoutPanel headingLayout;
    private TableLayoutPanel headingText;
    private AntdUI.Label lblEyebrow;
    private AntdUI.Label lblTitle;
    private AntdUI.Label lblSubtitle;
    private AntdUI.Panel connectionPanel;
    private TableLayoutPanel connectionLayout;
    private AntdUI.Label lblServer;
    private AntdUI.Badge badgeConnection;
    private AntdUI.Button btnConnect;
    private TableLayoutPanel bodyLayout;
    private AntdUI.Panel commandPanel;
    private TableLayoutPanel commandLayout;
    private AntdUI.Label lblDetails;
    private AntdUI.Label lblAgentId;
    private AntdUI.Input txtAgentId;
    private AntdUI.Label lblCommand;
    private AntdUI.Input txtCommand;
    private TableLayoutPanel timeoutLayout;
    private TableLayoutPanel timeoutText;
    private AntdUI.Label lblTimeout;
    private AntdUI.Label lblTimeoutHint;
    private AntdUI.InputNumber nudTimeout;
    private AntdUI.Button btnSend;
    private AntdUI.Label lblSendHint;
    private AntdUI.Panel outputPanel;
    private TableLayoutPanel outputLayout;
    private TableLayoutPanel outputHeader;
    private AntdUI.Label lblTerminalIcon;
    private AntdUI.Label lblResult;
    private AntdUI.Button btnCopy;
    private AntdUI.Button btnClear;
    private System.Windows.Forms.Panel outputDivider;
    private System.Windows.Forms.Panel outputFooterDivider;
    private System.Windows.Forms.Panel outputHost;
    private AntdUI.Input txtResult;
    private TableLayoutPanel emptyLayout;
    private TableLayoutPanel emptyContent;
    private System.Windows.Forms.Label lblEmptyIcon;
    private AntdUI.Label lblEmptyTitle;
    private AntdUI.Label lblEmptyHint;
    private TableLayoutPanel outputFooter;
    private AntdUI.Badge badgeOutput;
    private AntdUI.Button btnFollow;
    private TableLayoutPanel footerLayout;
    private AntdUI.Label lblStatus;
    private AntdUI.Label lblOutputCount;
}
