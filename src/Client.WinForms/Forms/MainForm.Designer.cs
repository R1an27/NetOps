namespace Client.WinForms.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        
        // Header Controls
        this.pnlHeader = new System.Windows.Forms.Panel();
        this.lblHeaderTitle = new System.Windows.Forms.Label();
        this.lblHeaderSubtitle = new System.Windows.Forms.Label();
        this.pnlStatusBadge = new System.Windows.Forms.Panel();
        this.lblStatusBadge = new System.Windows.Forms.Label();

        // Main Tab Control
        this.tabMain = new System.Windows.Forms.TabControl();
        this.tabDashboard = new System.Windows.Forms.TabPage();
        this.tabCommands = new System.Windows.Forms.TabPage();
        this.tabInspector = new System.Windows.Forms.TabPage();

        // Dashboard Tab Controls
        this.grpConnection = new System.Windows.Forms.GroupBox();
        this.lblHost = new System.Windows.Forms.Label();
        this.txtHost = new System.Windows.Forms.TextBox();
        this.lblPort = new System.Windows.Forms.Label();
        this.numPort = new System.Windows.Forms.NumericUpDown();
        this.btnConnect = new System.Windows.Forms.Button();
        this.btnDisconnect = new System.Windows.Forms.Button();
        this.lblConnectionStatus = new System.Windows.Forms.Label();

        this.grpDiagnostics = new System.Windows.Forms.GroupBox();
        this.lblPingPrompt = new System.Windows.Forms.Label();
        this.txtPingPayload = new System.Windows.Forms.TextBox();
        this.btnPing = new System.Windows.Forms.Button();
        this.lblPingResult = new System.Windows.Forms.Label();

        this.lblAgentIdPrompt = new System.Windows.Forms.Label();
        this.txtAgentId = new System.Windows.Forms.TextBox();
        this.btnHeartbeat = new System.Windows.Forms.Button();
        this.chkAutoHeartbeat = new System.Windows.Forms.CheckBox();
        this.lblHeartbeatResult = new System.Windows.Forms.Label();

        this.grpStats = new System.Windows.Forms.GroupBox();
        this.lblStatsSent = new System.Windows.Forms.Label();
        this.lblStatsRecv = new System.Windows.Forms.Label();
        this.lblStatsLastSeen = new System.Windows.Forms.Label();

        // Command Tab Controls
        this.grpCommandControl = new System.Windows.Forms.GroupBox();
        this.lblCommandLinePrompt = new System.Windows.Forms.Label();
        this.txtCommandLine = new System.Windows.Forms.TextBox();
        this.lblCommandTimeoutPrompt = new System.Windows.Forms.Label();
        this.numCommandTimeout = new System.Windows.Forms.NumericUpDown();
        this.btnExecuteCommand = new System.Windows.Forms.Button();
        this.lblCommandIdPrompt = new System.Windows.Forms.Label();
        this.txtCommandId = new System.Windows.Forms.TextBox();
        this.btnGenNewCmdId = new System.Windows.Forms.Button();
        this.lblQuickCommands = new System.Windows.Forms.Label();
        this.btnQuickWhoami = new System.Windows.Forms.Button();
        this.btnQuickHostname = new System.Windows.Forms.Button();
        this.btnQuickIpconfig = new System.Windows.Forms.Button();
        this.btnQuickDir = new System.Windows.Forms.Button();

        this.grpTerminal = new System.Windows.Forms.GroupBox();
        this.rtbTerminal = new System.Windows.Forms.RichTextBox();
        this.pnlTerminalBottom = new System.Windows.Forms.Panel();
        this.btnClearTerminal = new System.Windows.Forms.Button();
        this.chkAutoScroll = new System.Windows.Forms.CheckBox();
        this.lblTerminalStatus = new System.Windows.Forms.Label();

        // Inspector Tab Controls
        this.splitInspector = new System.Windows.Forms.SplitContainer();
        this.pnlInspectorTop = new System.Windows.Forms.Panel();
        this.btnClearInspector = new System.Windows.Forms.Button();
        this.chkAutoSelectLatest = new System.Windows.Forms.CheckBox();
        this.lblInspectorCount = new System.Windows.Forms.Label();
        this.dgvFrames = new System.Windows.Forms.DataGridView();
        this.colDirection = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colType = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colLength = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colMessageId = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colRequestId = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.colInReplyTo = new System.Windows.Forms.DataGridViewTextBoxColumn();
        this.grpFrameDetails = new System.Windows.Forms.GroupBox();
        this.pnlDetailsHeader = new System.Windows.Forms.Panel();
        this.lblWireHeader = new System.Windows.Forms.Label();
        this.txtWireHeaderHex = new System.Windows.Forms.TextBox();
        this.lblWireTotalSize = new System.Windows.Forms.Label();
        this.txtWireTotalSize = new System.Windows.Forms.TextBox();
        this.btnCopyJson = new System.Windows.Forms.Button();
        this.rtbFrameJson = new System.Windows.Forms.RichTextBox();

        // Status Strip
        this.statusStrip = new System.Windows.Forms.StatusStrip();
        this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
        this.statusEndpoint = new System.Windows.Forms.ToolStripStatusLabel();
        this.statusProtocol = new System.Windows.Forms.ToolStripStatusLabel();

        this.pnlHeader.SuspendLayout();
        this.tabMain.SuspendLayout();
        this.tabDashboard.SuspendLayout();
        this.tabCommands.SuspendLayout();
        this.tabInspector.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.splitInspector)).BeginInit();
        this.splitInspector.Panel1.SuspendLayout();
        this.splitInspector.Panel2.SuspendLayout();
        this.splitInspector.SuspendLayout();
        this.pnlInspectorTop.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvFrames)).BeginInit();
        this.grpFrameDetails.SuspendLayout();
        this.pnlDetailsHeader.SuspendLayout();
        this.grpConnection.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
        this.grpDiagnostics.SuspendLayout();
        this.grpStats.SuspendLayout();
        this.grpCommandControl.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numCommandTimeout)).BeginInit();
        this.grpTerminal.SuspendLayout();
        this.pnlTerminalBottom.SuspendLayout();
        this.statusStrip.SuspendLayout();
        this.SuspendLayout();

        // ==========================================
        // pnlHeader
        // ==========================================
        this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(30, 41, 59); // Slate Dark
        this.pnlHeader.Controls.Add(this.pnlStatusBadge);
        this.pnlHeader.Controls.Add(this.lblHeaderSubtitle);
        this.pnlHeader.Controls.Add(this.lblHeaderTitle);
        this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
        this.pnlHeader.Location = new System.Drawing.Point(0, 0);
        this.pnlHeader.Name = "pnlHeader";
        this.pnlHeader.Size = new System.Drawing.Size(984, 75);
        this.pnlHeader.TabIndex = 0;

        // lblHeaderTitle
        this.lblHeaderTitle.AutoSize = true;
        this.lblHeaderTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
        this.lblHeaderTitle.ForeColor = System.Drawing.Color.White;
        this.lblHeaderTitle.Location = new System.Drawing.Point(18, 14);
        this.lblHeaderTitle.Name = "lblHeaderTitle";
        this.lblHeaderTitle.Size = new System.Drawing.Size(288, 25);
        this.lblHeaderTitle.TabIndex = 0;
        this.lblHeaderTitle.Text = "NETOPS MANAGEMENT CLIENT";

        // lblHeaderSubtitle
        this.lblHeaderSubtitle.AutoSize = true;
        this.lblHeaderSubtitle.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblHeaderSubtitle.ForeColor = System.Drawing.Color.FromArgb(148, 163, 184);
        this.lblHeaderSubtitle.Location = new System.Drawing.Point(20, 43);
        this.lblHeaderSubtitle.Name = "lblHeaderSubtitle";
        this.lblHeaderSubtitle.Size = new System.Drawing.Size(430, 15);
        this.lblHeaderSubtitle.TabIndex = 1;
        this.lblHeaderSubtitle.Text = "NetOps Protocol v0 Console | 4-byte Big-Endian Length-Prefixed Safe Channel";

        // pnlStatusBadge
        this.pnlStatusBadge.BackColor = System.Drawing.Color.FromArgb(100, 116, 139);
        this.pnlStatusBadge.Controls.Add(this.lblStatusBadge);
        this.pnlStatusBadge.Location = new System.Drawing.Point(820, 20);
        this.pnlStatusBadge.Name = "pnlStatusBadge";
        this.pnlStatusBadge.Size = new System.Drawing.Size(140, 35);
        this.pnlStatusBadge.TabIndex = 2;

        // lblStatusBadge
        this.lblStatusBadge.Dock = System.Windows.Forms.DockStyle.Fill;
        this.lblStatusBadge.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.lblStatusBadge.ForeColor = System.Drawing.Color.White;
        this.lblStatusBadge.Location = new System.Drawing.Point(0, 0);
        this.lblStatusBadge.Name = "lblStatusBadge";
        this.lblStatusBadge.Size = new System.Drawing.Size(140, 35);
        this.lblStatusBadge.TabIndex = 0;
        this.lblStatusBadge.Text = "DISCONNECTED";
        this.lblStatusBadge.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

        // ==========================================
        // tabMain
        // ==========================================
        this.tabMain.Controls.Add(this.tabDashboard);
        this.tabMain.Controls.Add(this.tabCommands);
        this.tabMain.Controls.Add(this.tabInspector);
        this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
        this.tabMain.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.tabMain.Location = new System.Drawing.Point(0, 75);
        this.tabMain.Name = "tabMain";
        this.tabMain.SelectedIndex = 0;
        this.tabMain.Size = new System.Drawing.Size(984, 560);
        this.tabMain.TabIndex = 1;

        // tabDashboard
        this.tabDashboard.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
        this.tabDashboard.Controls.Add(this.grpStats);
        this.tabDashboard.Controls.Add(this.grpDiagnostics);
        this.tabDashboard.Controls.Add(this.grpConnection);
        this.tabDashboard.Location = new System.Drawing.Point(4, 26);
        this.tabDashboard.Name = "tabDashboard";
        this.tabDashboard.Padding = new System.Windows.Forms.Padding(16);
        this.tabDashboard.Size = new System.Drawing.Size(976, 530);
        this.tabDashboard.TabIndex = 0;
        this.tabDashboard.Text = "  Dashboard & Health  ";

        // tabCommands
        this.tabCommands.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
        this.tabCommands.Controls.Add(this.grpTerminal);
        this.tabCommands.Controls.Add(this.grpCommandControl);
        this.tabCommands.Location = new System.Drawing.Point(4, 26);
        this.tabCommands.Name = "tabCommands";
        this.tabCommands.Padding = new System.Windows.Forms.Padding(16);
        this.tabCommands.Size = new System.Drawing.Size(976, 530);
        this.tabCommands.TabIndex = 1;
        this.tabCommands.Text = "  Remote Commands  ";

        // ==========================================
        // grpCommandControl
        // ==========================================
        this.grpCommandControl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
        this.grpCommandControl.Controls.Add(this.btnQuickDir);
        this.grpCommandControl.Controls.Add(this.btnQuickIpconfig);
        this.grpCommandControl.Controls.Add(this.btnQuickHostname);
        this.grpCommandControl.Controls.Add(this.btnQuickWhoami);
        this.grpCommandControl.Controls.Add(this.lblQuickCommands);
        this.grpCommandControl.Controls.Add(this.btnGenNewCmdId);
        this.grpCommandControl.Controls.Add(this.txtCommandId);
        this.grpCommandControl.Controls.Add(this.lblCommandIdPrompt);
        this.grpCommandControl.Controls.Add(this.btnExecuteCommand);
        this.grpCommandControl.Controls.Add(this.numCommandTimeout);
        this.grpCommandControl.Controls.Add(this.lblCommandTimeoutPrompt);
        this.grpCommandControl.Controls.Add(this.txtCommandLine);
        this.grpCommandControl.Controls.Add(this.lblCommandLinePrompt);
        this.grpCommandControl.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpCommandControl.Location = new System.Drawing.Point(16, 16);
        this.grpCommandControl.Name = "grpCommandControl";
        this.grpCommandControl.Size = new System.Drawing.Size(944, 150);
        this.grpCommandControl.TabIndex = 0;
        this.grpCommandControl.TabStop = false;
        this.grpCommandControl.Text = "Bảng Điều Khiển Lệnh Shell Từ Xa (Remote Shell Execution)";

        // lblCommandLinePrompt
        this.lblCommandLinePrompt.AutoSize = true;
        this.lblCommandLinePrompt.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblCommandLinePrompt.Location = new System.Drawing.Point(20, 28);
        this.lblCommandLinePrompt.Name = "lblCommandLinePrompt";
        this.lblCommandLinePrompt.Size = new System.Drawing.Size(161, 15);
        this.lblCommandLinePrompt.TabIndex = 0;
        this.lblCommandLinePrompt.Text = "Chuỗi lệnh shell cần thực thi:";

        // txtCommandLine
        this.txtCommandLine.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
        this.txtCommandLine.Font = new System.Drawing.Font("Consolas", 10F);
        this.txtCommandLine.Location = new System.Drawing.Point(20, 48);
        this.txtCommandLine.Name = "txtCommandLine";
        this.txtCommandLine.Size = new System.Drawing.Size(560, 23);
        this.txtCommandLine.TabIndex = 1;
        this.txtCommandLine.Text = "whoami";

        // lblCommandTimeoutPrompt
        this.lblCommandTimeoutPrompt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.lblCommandTimeoutPrompt.AutoSize = true;
        this.lblCommandTimeoutPrompt.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblCommandTimeoutPrompt.Location = new System.Drawing.Point(595, 28);
        this.lblCommandTimeoutPrompt.Name = "lblCommandTimeoutPrompt";
        this.lblCommandTimeoutPrompt.Size = new System.Drawing.Size(70, 15);
        this.lblCommandTimeoutPrompt.TabIndex = 2;
        this.lblCommandTimeoutPrompt.Text = "Timeout (s):";

        // numCommandTimeout
        this.numCommandTimeout.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.numCommandTimeout.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.numCommandTimeout.Location = new System.Drawing.Point(598, 48);
        this.numCommandTimeout.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
        this.numCommandTimeout.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.numCommandTimeout.Name = "numCommandTimeout";
        this.numCommandTimeout.Size = new System.Drawing.Size(75, 24);
        this.numCommandTimeout.TabIndex = 3;
        this.numCommandTimeout.Value = new decimal(new int[] { 30, 0, 0, 0 });

        // btnExecuteCommand
        this.btnExecuteCommand.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
        this.btnExecuteCommand.BackColor = System.Drawing.Color.FromArgb(79, 70, 229); // Indigo
        this.btnExecuteCommand.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnExecuteCommand.Enabled = false;
        this.btnExecuteCommand.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnExecuteCommand.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.btnExecuteCommand.ForeColor = System.Drawing.Color.White;
        this.btnExecuteCommand.Location = new System.Drawing.Point(690, 43);
        this.btnExecuteCommand.Name = "btnExecuteCommand";
        this.btnExecuteCommand.Size = new System.Drawing.Size(235, 32);
        this.btnExecuteCommand.TabIndex = 4;
        this.btnExecuteCommand.Text = "▶ Thực Thi Lệnh (Execute)";
        this.btnExecuteCommand.UseVisualStyleBackColor = false;

        // lblCommandIdPrompt
        this.lblCommandIdPrompt.AutoSize = true;
        this.lblCommandIdPrompt.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblCommandIdPrompt.Location = new System.Drawing.Point(20, 85);
        this.lblCommandIdPrompt.Name = "lblCommandIdPrompt";
        this.lblCommandIdPrompt.Size = new System.Drawing.Size(81, 15);
        this.lblCommandIdPrompt.TabIndex = 5;
        this.lblCommandIdPrompt.Text = "Command ID:";

        // txtCommandId
        this.txtCommandId.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtCommandId.Location = new System.Drawing.Point(20, 105);
        this.txtCommandId.Name = "txtCommandId";
        this.txtCommandId.Size = new System.Drawing.Size(180, 23);
        this.txtCommandId.TabIndex = 6;
        this.txtCommandId.Text = "cmd-001";

        // btnGenNewCmdId
        this.btnGenNewCmdId.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnGenNewCmdId.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnGenNewCmdId.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnGenNewCmdId.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnGenNewCmdId.Location = new System.Drawing.Point(206, 103);
        this.btnGenNewCmdId.Name = "btnGenNewCmdId";
        this.btnGenNewCmdId.Size = new System.Drawing.Size(85, 26);
        this.btnGenNewCmdId.TabIndex = 7;
        this.btnGenNewCmdId.Text = "Tạo mới ID";
        this.btnGenNewCmdId.UseVisualStyleBackColor = true;

        // lblQuickCommands
        this.lblQuickCommands.AutoSize = true;
        this.lblQuickCommands.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblQuickCommands.Location = new System.Drawing.Point(315, 85);
        this.lblQuickCommands.Name = "lblQuickCommands";
        this.lblQuickCommands.Size = new System.Drawing.Size(124, 15);
        this.lblQuickCommands.TabIndex = 8;
        this.lblQuickCommands.Text = "Mẫu lệnh nhanh (Quick):";

        // btnQuickWhoami
        this.btnQuickWhoami.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnQuickWhoami.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnQuickWhoami.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnQuickWhoami.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnQuickWhoami.Location = new System.Drawing.Point(318, 103);
        this.btnQuickWhoami.Name = "btnQuickWhoami";
        this.btnQuickWhoami.Size = new System.Drawing.Size(75, 26);
        this.btnQuickWhoami.TabIndex = 9;
        this.btnQuickWhoami.Text = "whoami";
        this.btnQuickWhoami.UseVisualStyleBackColor = true;

        // btnQuickHostname
        this.btnQuickHostname.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnQuickHostname.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnQuickHostname.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnQuickHostname.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnQuickHostname.Location = new System.Drawing.Point(399, 103);
        this.btnQuickHostname.Name = "btnQuickHostname";
        this.btnQuickHostname.Size = new System.Drawing.Size(85, 26);
        this.btnQuickHostname.TabIndex = 10;
        this.btnQuickHostname.Text = "hostname";
        this.btnQuickHostname.UseVisualStyleBackColor = true;

        // btnQuickIpconfig
        this.btnQuickIpconfig.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnQuickIpconfig.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnQuickIpconfig.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnQuickIpconfig.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnQuickIpconfig.Location = new System.Drawing.Point(490, 103);
        this.btnQuickIpconfig.Name = "btnQuickIpconfig";
        this.btnQuickIpconfig.Size = new System.Drawing.Size(80, 26);
        this.btnQuickIpconfig.TabIndex = 11;
        this.btnQuickIpconfig.Text = "ipconfig";
        this.btnQuickIpconfig.UseVisualStyleBackColor = true;

        // btnQuickDir
        this.btnQuickDir.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnQuickDir.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnQuickDir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnQuickDir.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnQuickDir.Location = new System.Drawing.Point(576, 103);
        this.btnQuickDir.Name = "btnQuickDir";
        this.btnQuickDir.Size = new System.Drawing.Size(75, 26);
        this.btnQuickDir.TabIndex = 12;
        this.btnQuickDir.Text = "dir /b";
        this.btnQuickDir.UseVisualStyleBackColor = true;

        // ==========================================
        // grpTerminal
        // ==========================================
        this.grpTerminal.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
        this.grpTerminal.Controls.Add(this.rtbTerminal);
        this.grpTerminal.Controls.Add(this.pnlTerminalBottom);
        this.grpTerminal.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpTerminal.Location = new System.Drawing.Point(16, 180);
        this.grpTerminal.Name = "grpTerminal";
        this.grpTerminal.Size = new System.Drawing.Size(944, 335);
        this.grpTerminal.TabIndex = 1;
        this.grpTerminal.TabStop = false;
        this.grpTerminal.Text = "Terminal Output Thời Gian Thực (Console Shell Stream)";

        // rtbTerminal
        this.rtbTerminal.BackColor = System.Drawing.Color.FromArgb(15, 23, 42); // Slate-900 / Dark
        this.rtbTerminal.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.rtbTerminal.Dock = System.Windows.Forms.DockStyle.Fill;
        this.rtbTerminal.Font = new System.Drawing.Font("Consolas", 10F);
        this.rtbTerminal.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153); // Emerald-400
        this.rtbTerminal.Location = new System.Drawing.Point(3, 20);
        this.rtbTerminal.Name = "rtbTerminal";
        this.rtbTerminal.ReadOnly = true;
        this.rtbTerminal.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Both;
        this.rtbTerminal.Size = new System.Drawing.Size(938, 274);
        this.rtbTerminal.TabIndex = 0;
        this.rtbTerminal.Text = "";
        this.rtbTerminal.WordWrap = false;

        // pnlTerminalBottom
        this.pnlTerminalBottom.Controls.Add(this.lblTerminalStatus);
        this.pnlTerminalBottom.Controls.Add(this.chkAutoScroll);
        this.pnlTerminalBottom.Controls.Add(this.btnClearTerminal);
        this.pnlTerminalBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
        this.pnlTerminalBottom.Location = new System.Drawing.Point(3, 294);
        this.pnlTerminalBottom.Name = "pnlTerminalBottom";
        this.pnlTerminalBottom.Size = new System.Drawing.Size(938, 38);
        this.pnlTerminalBottom.TabIndex = 1;

        // btnClearTerminal
        this.btnClearTerminal.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnClearTerminal.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnClearTerminal.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnClearTerminal.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.btnClearTerminal.Location = new System.Drawing.Point(10, 5);
        this.btnClearTerminal.Name = "btnClearTerminal";
        this.btnClearTerminal.Size = new System.Drawing.Size(120, 28);
        this.btnClearTerminal.TabIndex = 0;
        this.btnClearTerminal.Text = "Xóa Terminal";
        this.btnClearTerminal.UseVisualStyleBackColor = true;

        // chkAutoScroll
        this.chkAutoScroll.AutoSize = true;
        this.chkAutoScroll.Checked = true;
        this.chkAutoScroll.CheckState = System.Windows.Forms.CheckState.Checked;
        this.chkAutoScroll.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.chkAutoScroll.Location = new System.Drawing.Point(145, 10);
        this.chkAutoScroll.Name = "chkAutoScroll";
        this.chkAutoScroll.Size = new System.Drawing.Size(161, 19);
        this.chkAutoScroll.TabIndex = 1;
        this.chkAutoScroll.Text = "Tự động cuộn xuống cuối";
        this.chkAutoScroll.UseVisualStyleBackColor = true;

        // lblTerminalStatus
        this.lblTerminalStatus.AutoSize = true;
        this.lblTerminalStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lblTerminalStatus.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
        this.lblTerminalStatus.Location = new System.Drawing.Point(325, 11);
        this.lblTerminalStatus.Name = "lblTerminalStatus";
        this.lblTerminalStatus.Size = new System.Drawing.Size(183, 15);
        this.lblTerminalStatus.TabIndex = 2;
        this.lblTerminalStatus.Text = "Trạng thái: Sẵn sàng thực thi lệnh";

        // ==========================================
        // tabInspector
        // ==========================================
        this.tabInspector.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
        this.tabInspector.Controls.Add(this.splitInspector);
        this.tabInspector.Location = new System.Drawing.Point(4, 26);
        this.tabInspector.Name = "tabInspector";
        this.tabInspector.Padding = new System.Windows.Forms.Padding(12);
        this.tabInspector.Size = new System.Drawing.Size(976, 530);
        this.tabInspector.TabIndex = 2;
        this.tabInspector.Text = "  Protocol Frame Inspector  ";

        // splitInspector
        this.splitInspector.Dock = System.Windows.Forms.DockStyle.Fill;
        this.splitInspector.Location = new System.Drawing.Point(12, 12);
        this.splitInspector.Name = "splitInspector";
        this.splitInspector.Orientation = System.Windows.Forms.Orientation.Horizontal;
        this.splitInspector.Panel1.Controls.Add(this.dgvFrames);
        this.splitInspector.Panel1.Controls.Add(this.pnlInspectorTop);
        this.splitInspector.Panel2.Controls.Add(this.grpFrameDetails);
        this.splitInspector.Size = new System.Drawing.Size(952, 506);
        this.splitInspector.SplitterDistance = 240;
        this.splitInspector.TabIndex = 0;

        // pnlInspectorTop
        this.pnlInspectorTop.Controls.Add(this.lblInspectorCount);
        this.pnlInspectorTop.Controls.Add(this.chkAutoSelectLatest);
        this.pnlInspectorTop.Controls.Add(this.btnClearInspector);
        this.pnlInspectorTop.Dock = System.Windows.Forms.DockStyle.Top;
        this.pnlInspectorTop.Location = new System.Drawing.Point(0, 0);
        this.pnlInspectorTop.Name = "pnlInspectorTop";
        this.pnlInspectorTop.Size = new System.Drawing.Size(952, 36);
        this.pnlInspectorTop.TabIndex = 0;

        // btnClearInspector
        this.btnClearInspector.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnClearInspector.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnClearInspector.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnClearInspector.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnClearInspector.Location = new System.Drawing.Point(0, 5);
        this.btnClearInspector.Name = "btnClearInspector";
        this.btnClearInspector.Size = new System.Drawing.Size(120, 26);
        this.btnClearInspector.TabIndex = 0;
        this.btnClearInspector.Text = "Xóa Danh Sách";
        this.btnClearInspector.UseVisualStyleBackColor = true;

        // chkAutoSelectLatest
        this.chkAutoSelectLatest.AutoSize = true;
        this.chkAutoSelectLatest.Checked = true;
        this.chkAutoSelectLatest.CheckState = System.Windows.Forms.CheckState.Checked;
        this.chkAutoSelectLatest.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.chkAutoSelectLatest.Location = new System.Drawing.Point(135, 9);
        this.chkAutoSelectLatest.Name = "chkAutoSelectLatest";
        this.chkAutoSelectLatest.Size = new System.Drawing.Size(188, 19);
        this.chkAutoSelectLatest.TabIndex = 1;
        this.chkAutoSelectLatest.Text = "Tự động chọn gói tin mới nhất";
        this.chkAutoSelectLatest.UseVisualStyleBackColor = true;

        // lblInspectorCount
        this.lblInspectorCount.AutoSize = true;
        this.lblInspectorCount.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lblInspectorCount.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
        this.lblInspectorCount.Location = new System.Drawing.Point(340, 10);
        this.lblInspectorCount.Name = "lblInspectorCount";
        this.lblInspectorCount.Size = new System.Drawing.Size(252, 15);
        this.lblInspectorCount.TabIndex = 2;
        this.lblInspectorCount.Text = "Tổng: 0 gói tin (Giới hạn 500 gói gần nhất)";

        // dgvFrames
        this.dgvFrames.AllowUserToAddRows = false;
        this.dgvFrames.AllowUserToDeleteRows = false;
        this.dgvFrames.AllowUserToResizeRows = false;
        this.dgvFrames.BackgroundColor = System.Drawing.Color.White;
        this.dgvFrames.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
        this.dgvFrames.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.dgvFrames.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colDirection,
            this.colTime,
            this.colType,
            this.colLength,
            this.colMessageId,
            this.colRequestId,
            this.colInReplyTo
        });
        this.dgvFrames.Dock = System.Windows.Forms.DockStyle.Fill;
        this.dgvFrames.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.dgvFrames.Location = new System.Drawing.Point(0, 36);
        this.dgvFrames.MultiSelect = false;
        this.dgvFrames.Name = "dgvFrames";
        this.dgvFrames.ReadOnly = true;
        this.dgvFrames.RowHeadersVisible = false;
        this.dgvFrames.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        this.dgvFrames.Size = new System.Drawing.Size(952, 204);
        this.dgvFrames.TabIndex = 1;

        // colDirection
        this.colDirection.HeaderText = "Dir";
        this.colDirection.Name = "colDirection";
        this.colDirection.ReadOnly = true;
        this.colDirection.Width = 65;
        this.colDirection.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;

        // colTime
        this.colTime.HeaderText = "Thời Gian";
        this.colTime.Name = "colTime";
        this.colTime.ReadOnly = true;
        this.colTime.Width = 95;

        // colType
        this.colType.HeaderText = "Loại Gói (Type)";
        this.colType.Name = "colType";
        this.colType.ReadOnly = true;
        this.colType.Width = 135;

        // colLength
        this.colLength.HeaderText = "Payload Size";
        this.colLength.Name = "colLength";
        this.colLength.ReadOnly = true;
        this.colLength.Width = 90;
        this.colLength.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;

        // colMessageId
        this.colMessageId.HeaderText = "MessageId";
        this.colMessageId.Name = "colMessageId";
        this.colMessageId.ReadOnly = true;
        this.colMessageId.Width = 190;

        // colRequestId
        this.colRequestId.HeaderText = "RequestId";
        this.colRequestId.Name = "colRequestId";
        this.colRequestId.ReadOnly = true;
        this.colRequestId.Width = 180;

        // colInReplyTo
        this.colInReplyTo.HeaderText = "InReplyTo";
        this.colInReplyTo.Name = "colInReplyTo";
        this.colInReplyTo.ReadOnly = true;
        this.colInReplyTo.Width = 180;

        // grpFrameDetails
        this.grpFrameDetails.Controls.Add(this.rtbFrameJson);
        this.grpFrameDetails.Controls.Add(this.pnlDetailsHeader);
        this.grpFrameDetails.Dock = System.Windows.Forms.DockStyle.Fill;
        this.grpFrameDetails.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpFrameDetails.Location = new System.Drawing.Point(0, 0);
        this.grpFrameDetails.Name = "grpFrameDetails";
        this.grpFrameDetails.Size = new System.Drawing.Size(952, 262);
        this.grpFrameDetails.TabIndex = 0;
        this.grpFrameDetails.TabStop = false;
        this.grpFrameDetails.Text = "Chi Tiết Khung Truyền Nhị Phân & Envelope JSON (Protocol Frame Details)";

        // pnlDetailsHeader
        this.pnlDetailsHeader.Controls.Add(this.btnCopyJson);
        this.pnlDetailsHeader.Controls.Add(this.txtWireTotalSize);
        this.pnlDetailsHeader.Controls.Add(this.lblWireTotalSize);
        this.pnlDetailsHeader.Controls.Add(this.txtWireHeaderHex);
        this.pnlDetailsHeader.Controls.Add(this.lblWireHeader);
        this.pnlDetailsHeader.Dock = System.Windows.Forms.DockStyle.Top;
        this.pnlDetailsHeader.Location = new System.Drawing.Point(3, 20);
        this.pnlDetailsHeader.Name = "pnlDetailsHeader";
        this.pnlDetailsHeader.Size = new System.Drawing.Size(946, 62);
        this.pnlDetailsHeader.TabIndex = 0;

        // lblWireHeader
        this.lblWireHeader.AutoSize = true;
        this.lblWireHeader.Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblWireHeader.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
        this.lblWireHeader.Location = new System.Drawing.Point(5, 5);
        this.lblWireHeader.Name = "lblWireHeader";
        this.lblWireHeader.Size = new System.Drawing.Size(225, 15);
        this.lblWireHeader.TabIndex = 0;
        this.lblWireHeader.Text = "Wire Length Header (4-byte Big-Endian):";

        // txtWireHeaderHex
        this.txtWireHeaderHex.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.txtWireHeaderHex.Font = new System.Drawing.Font("Consolas", 9.5F, System.Drawing.FontStyle.Bold);
        this.txtWireHeaderHex.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
        this.txtWireHeaderHex.Location = new System.Drawing.Point(8, 25);
        this.txtWireHeaderHex.Name = "txtWireHeaderHex";
        this.txtWireHeaderHex.ReadOnly = true;
        this.txtWireHeaderHex.Size = new System.Drawing.Size(300, 22);
        this.txtWireHeaderHex.TabIndex = 1;

        // lblWireTotalSize
        this.lblWireTotalSize.AutoSize = true;
        this.lblWireTotalSize.Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
        this.lblWireTotalSize.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
        this.lblWireTotalSize.Location = new System.Drawing.Point(325, 5);
        this.lblWireTotalSize.Name = "lblWireTotalSize";
        this.lblWireTotalSize.Size = new System.Drawing.Size(227, 15);
        this.lblWireTotalSize.TabIndex = 2;
        this.lblWireTotalSize.Text = "Tổng Wire Frame Size (Header + Payload):";

        // txtWireTotalSize
        this.txtWireTotalSize.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.txtWireTotalSize.Font = new System.Drawing.Font("Consolas", 9.5F);
        this.txtWireTotalSize.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
        this.txtWireTotalSize.Location = new System.Drawing.Point(328, 25);
        this.txtWireTotalSize.Name = "txtWireTotalSize";
        this.txtWireTotalSize.ReadOnly = true;
        this.txtWireTotalSize.Size = new System.Drawing.Size(230, 22);
        this.txtWireTotalSize.TabIndex = 3;

        // btnCopyJson
        this.btnCopyJson.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.btnCopyJson.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnCopyJson.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnCopyJson.Font = new System.Drawing.Font("Segoe UI", 8.5F);
        this.btnCopyJson.Location = new System.Drawing.Point(575, 23);
        this.btnCopyJson.Name = "btnCopyJson";
        this.btnCopyJson.Size = new System.Drawing.Size(130, 26);
        this.btnCopyJson.TabIndex = 4;
        this.btnCopyJson.Text = "Copy JSON";
        this.btnCopyJson.UseVisualStyleBackColor = true;

        // rtbFrameJson
        this.rtbFrameJson.BackColor = System.Drawing.Color.FromArgb(15, 23, 42); // Slate-900
        this.rtbFrameJson.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.rtbFrameJson.Dock = System.Windows.Forms.DockStyle.Fill;
        this.rtbFrameJson.Font = new System.Drawing.Font("Consolas", 9.5F);
        this.rtbFrameJson.ForeColor = System.Drawing.Color.FromArgb(241, 245, 249);
        this.rtbFrameJson.Location = new System.Drawing.Point(3, 82);
        this.rtbFrameJson.Name = "rtbFrameJson";
        this.rtbFrameJson.ReadOnly = true;
        this.rtbFrameJson.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Both;
        this.rtbFrameJson.Size = new System.Drawing.Size(946, 177);
        this.rtbFrameJson.TabIndex = 1;
        this.rtbFrameJson.Text = "";
        this.rtbFrameJson.WordWrap = false;

        // ==========================================
        // grpConnection
        // ==========================================
        this.grpConnection.Controls.Add(this.lblConnectionStatus);
        this.grpConnection.Controls.Add(this.btnDisconnect);
        this.grpConnection.Controls.Add(this.btnConnect);
        this.grpConnection.Controls.Add(this.numPort);
        this.grpConnection.Controls.Add(this.lblPort);
        this.grpConnection.Controls.Add(this.txtHost);
        this.grpConnection.Controls.Add(this.lblHost);
        this.grpConnection.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpConnection.Location = new System.Drawing.Point(20, 20);
        this.grpConnection.Name = "grpConnection";
        this.grpConnection.Size = new System.Drawing.Size(936, 100);
        this.grpConnection.TabIndex = 0;
        this.grpConnection.TabStop = false;
        this.grpConnection.Text = "1. Quản Lý Kết Nối TCP";

        // lblHost
        this.lblHost.AutoSize = true;
        this.lblHost.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblHost.Location = new System.Drawing.Point(20, 35);
        this.lblHost.Name = "lblHost";
        this.lblHost.Size = new System.Drawing.Size(81, 15);
        this.lblHost.TabIndex = 0;
        this.lblHost.Text = "Master Host/IP:";

        // txtHost
        this.txtHost.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtHost.Location = new System.Drawing.Point(23, 55);
        this.txtHost.Name = "txtHost";
        this.txtHost.Size = new System.Drawing.Size(180, 24);
        this.txtHost.TabIndex = 1;
        this.txtHost.Text = "127.0.0.1";

        // lblPort
        this.lblPort.AutoSize = true;
        this.lblPort.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblPort.Location = new System.Drawing.Point(220, 35);
        this.lblPort.Name = "lblPort";
        this.lblPort.Size = new System.Drawing.Size(56, 15);
        this.lblPort.TabIndex = 2;
        this.lblPort.Text = "TCP Port:";

        // numPort
        this.numPort.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.numPort.Location = new System.Drawing.Point(223, 55);
        this.numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        this.numPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.numPort.Name = "numPort";
        this.numPort.Size = new System.Drawing.Size(90, 24);
        this.numPort.TabIndex = 3;
        this.numPort.Value = new decimal(new int[] { 5000, 0, 0, 0 });

        // btnConnect
        this.btnConnect.BackColor = System.Drawing.Color.FromArgb(16, 185, 129); // Emerald
        this.btnConnect.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnConnect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnConnect.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.btnConnect.ForeColor = System.Drawing.Color.White;
        this.btnConnect.Location = new System.Drawing.Point(340, 50);
        this.btnConnect.Name = "btnConnect";
        this.btnConnect.Size = new System.Drawing.Size(120, 32);
        this.btnConnect.TabIndex = 4;
        this.btnConnect.Text = "Connect";
        this.btnConnect.UseVisualStyleBackColor = false;

        // btnDisconnect
        this.btnDisconnect.BackColor = System.Drawing.Color.FromArgb(239, 68, 68); // Red
        this.btnDisconnect.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnDisconnect.Enabled = false;
        this.btnDisconnect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnDisconnect.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.btnDisconnect.ForeColor = System.Drawing.Color.White;
        this.btnDisconnect.Location = new System.Drawing.Point(475, 50);
        this.btnDisconnect.Name = "btnDisconnect";
        this.btnDisconnect.Size = new System.Drawing.Size(120, 32);
        this.btnDisconnect.TabIndex = 5;
        this.btnDisconnect.Text = "Disconnect";
        this.btnDisconnect.UseVisualStyleBackColor = false;

        // lblConnectionStatus
        this.lblConnectionStatus.AutoSize = true;
        this.lblConnectionStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lblConnectionStatus.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
        this.lblConnectionStatus.Location = new System.Drawing.Point(620, 59);
        this.lblConnectionStatus.Name = "lblConnectionStatus";
        this.lblConnectionStatus.Size = new System.Drawing.Size(193, 15);
        this.lblConnectionStatus.TabIndex = 6;
        this.lblConnectionStatus.Text = "Trạng thái: Chưa kết nối máy chủ";

        // ==========================================
        // grpDiagnostics
        // ==========================================
        this.grpDiagnostics.Controls.Add(this.lblHeartbeatResult);
        this.grpDiagnostics.Controls.Add(this.chkAutoHeartbeat);
        this.grpDiagnostics.Controls.Add(this.btnHeartbeat);
        this.grpDiagnostics.Controls.Add(this.txtAgentId);
        this.grpDiagnostics.Controls.Add(this.lblAgentIdPrompt);
        this.grpDiagnostics.Controls.Add(this.lblPingResult);
        this.grpDiagnostics.Controls.Add(this.btnPing);
        this.grpDiagnostics.Controls.Add(this.txtPingPayload);
        this.grpDiagnostics.Controls.Add(this.lblPingPrompt);
        this.grpDiagnostics.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpDiagnostics.Location = new System.Drawing.Point(20, 135);
        this.grpDiagnostics.Name = "grpDiagnostics";
        this.grpDiagnostics.Size = new System.Drawing.Size(936, 190);
        this.grpDiagnostics.TabIndex = 1;
        this.grpDiagnostics.TabStop = false;
        this.grpDiagnostics.Text = "2. Kiểm Tra Đường Truyền & Nhịp Tim (Ping / Pong & Heartbeat)";

        // lblPingPrompt
        this.lblPingPrompt.AutoSize = true;
        this.lblPingPrompt.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblPingPrompt.Location = new System.Drawing.Point(20, 35);
        this.lblPingPrompt.Name = "lblPingPrompt";
        this.lblPingPrompt.Size = new System.Drawing.Size(117, 15);
        this.lblPingPrompt.TabIndex = 0;
        this.lblPingPrompt.Text = "Ping Payload (text):";

        // txtPingPayload
        this.txtPingPayload.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtPingPayload.Location = new System.Drawing.Point(23, 55);
        this.txtPingPayload.Name = "txtPingPayload";
        this.txtPingPayload.Size = new System.Drawing.Size(290, 24);
        this.txtPingPayload.TabIndex = 1;
        this.txtPingPayload.Text = "ping-probe-check";

        // btnPing
        this.btnPing.BackColor = System.Drawing.Color.FromArgb(59, 130, 246); // Blue
        this.btnPing.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnPing.Enabled = false;
        this.btnPing.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnPing.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.btnPing.ForeColor = System.Drawing.Color.White;
        this.btnPing.Location = new System.Drawing.Point(340, 51);
        this.btnPing.Name = "btnPing";
        this.btnPing.Size = new System.Drawing.Size(120, 30);
        this.btnPing.TabIndex = 2;
        this.btnPing.Text = "Gửi Ping";
        this.btnPing.UseVisualStyleBackColor = false;

        // lblPingResult
        this.lblPingResult.AutoSize = true;
        this.lblPingResult.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblPingResult.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
        this.lblPingResult.Location = new System.Drawing.Point(475, 59);
        this.lblPingResult.Name = "lblPingResult";
        this.lblPingResult.Size = new System.Drawing.Size(199, 15);
        this.lblPingResult.TabIndex = 3;
        this.lblPingResult.Text = "Chưa gửi gói tin kiểm tra kết nối Ping";

        // lblAgentIdPrompt
        this.lblAgentIdPrompt.AutoSize = true;
        this.lblAgentIdPrompt.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblAgentIdPrompt.Location = new System.Drawing.Point(20, 105);
        this.lblAgentIdPrompt.Name = "lblAgentIdPrompt";
        this.lblAgentIdPrompt.Size = new System.Drawing.Size(107, 15);
        this.lblAgentIdPrompt.TabIndex = 4;
        this.lblAgentIdPrompt.Text = "Client / Agent Id:";

        // txtAgentId
        this.txtAgentId.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.txtAgentId.Location = new System.Drawing.Point(23, 125);
        this.txtAgentId.Name = "txtAgentId";
        this.txtAgentId.Size = new System.Drawing.Size(290, 24);
        this.txtAgentId.TabIndex = 5;
        this.txtAgentId.Text = "admin-client-01";

        // btnHeartbeat
        this.btnHeartbeat.BackColor = System.Drawing.Color.FromArgb(14, 165, 233); // Sky
        this.btnHeartbeat.Cursor = System.Windows.Forms.Cursors.Hand;
        this.btnHeartbeat.Enabled = false;
        this.btnHeartbeat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnHeartbeat.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.btnHeartbeat.ForeColor = System.Drawing.Color.White;
        this.btnHeartbeat.Location = new System.Drawing.Point(340, 121);
        this.btnHeartbeat.Name = "btnHeartbeat";
        this.btnHeartbeat.Size = new System.Drawing.Size(120, 30);
        this.btnHeartbeat.TabIndex = 6;
        this.btnHeartbeat.Text = "Gửi Heartbeat";
        this.btnHeartbeat.UseVisualStyleBackColor = false;

        // chkAutoHeartbeat
        this.chkAutoHeartbeat.AutoSize = true;
        this.chkAutoHeartbeat.Enabled = false;
        this.chkAutoHeartbeat.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.chkAutoHeartbeat.Location = new System.Drawing.Point(475, 127);
        this.chkAutoHeartbeat.Name = "chkAutoHeartbeat";
        this.chkAutoHeartbeat.Size = new System.Drawing.Size(180, 19);
        this.chkAutoHeartbeat.TabIndex = 7;
        this.chkAutoHeartbeat.Text = "Tự động gửi nhịp tim (mỗi 5s)";
        this.chkAutoHeartbeat.UseVisualStyleBackColor = true;

        // lblHeartbeatResult
        this.lblHeartbeatResult.AutoSize = true;
        this.lblHeartbeatResult.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblHeartbeatResult.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
        this.lblHeartbeatResult.Location = new System.Drawing.Point(20, 160);
        this.lblHeartbeatResult.Name = "lblHeartbeatResult";
        this.lblHeartbeatResult.Size = new System.Drawing.Size(217, 15);
        this.lblHeartbeatResult.TabIndex = 8;
        this.lblHeartbeatResult.Text = "Trạng thái nhịp tim: Chưa gửi heartbeat";

        // ==========================================
        // grpStats
        // ==========================================
        this.grpStats.Controls.Add(this.lblStatsLastSeen);
        this.grpStats.Controls.Add(this.lblStatsRecv);
        this.grpStats.Controls.Add(this.lblStatsSent);
        this.grpStats.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
        this.grpStats.Location = new System.Drawing.Point(20, 340);
        this.grpStats.Name = "grpStats";
        this.grpStats.Size = new System.Drawing.Size(936, 120);
        this.grpStats.TabIndex = 2;
        this.grpStats.TabStop = false;
        this.grpStats.Text = "3. Thống Kê Phiên Truyền Thông";

        // lblStatsSent
        this.lblStatsSent.AutoSize = true;
        this.lblStatsSent.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.lblStatsSent.Location = new System.Drawing.Point(20, 35);
        this.lblStatsSent.Name = "lblStatsSent";
        this.lblStatsSent.Size = new System.Drawing.Size(155, 17);
        this.lblStatsSent.TabIndex = 0;
        this.lblStatsSent.Text = "Số gói tin đã gửi (OUT): 0";

        // lblStatsRecv
        this.lblStatsRecv.AutoSize = true;
        this.lblStatsRecv.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.lblStatsRecv.Location = new System.Drawing.Point(20, 65);
        this.lblStatsRecv.Name = "lblStatsRecv";
        this.lblStatsRecv.Size = new System.Drawing.Size(161, 17);
        this.lblStatsRecv.TabIndex = 1;
        this.lblStatsRecv.Text = "Số gói tin đã nhận (IN): 0";

        // lblStatsLastSeen
        this.lblStatsLastSeen.AutoSize = true;
        this.lblStatsLastSeen.Font = new System.Drawing.Font("Segoe UI", 9.5F);
        this.lblStatsLastSeen.Location = new System.Drawing.Point(340, 35);
        this.lblStatsLastSeen.Name = "lblStatsLastSeen";
        this.lblStatsLastSeen.Size = new System.Drawing.Size(201, 17);
        this.lblStatsLastSeen.TabIndex = 2;
        this.lblStatsLastSeen.Text = "Hoạt động gần nhất: Chưa có";

        // ==========================================
        // statusStrip
        // ==========================================
        this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel,
            this.statusEndpoint,
            this.statusProtocol
        });
        this.statusStrip.Location = new System.Drawing.Point(0, 635);
        this.statusStrip.Name = "statusStrip";
        this.statusStrip.Size = new System.Drawing.Size(984, 22);
        this.statusStrip.TabIndex = 2;

        // statusLabel
        this.statusLabel.Name = "statusLabel";
        this.statusLabel.Size = new System.Drawing.Size(82, 17);
        this.statusLabel.Text = "Ready to connect";

        // statusEndpoint
        this.statusEndpoint.Name = "statusEndpoint";
        this.statusEndpoint.Size = new System.Drawing.Size(130, 17);
        this.statusEndpoint.Text = " | Endpoint: Not connected";

        // statusProtocol
        this.statusProtocol.Name = "statusProtocol";
        this.statusProtocol.Size = new System.Drawing.Size(240, 17);
        this.statusProtocol.Text = " | Protocol: v0 (Length-Prefix 4B Big-Endian)";

        // ==========================================
        // MainForm
        // ==========================================
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
        this.ClientSize = new System.Drawing.Size(984, 657);
        this.Controls.Add(this.tabMain);
        this.Controls.Add(this.pnlHeader);
        this.Controls.Add(this.statusStrip);
        this.MinimumSize = new System.Drawing.Size(900, 650);
        this.Name = "MainForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "NetOps Management Client (NetOps Protocol v0)";

        this.pnlHeader.ResumeLayout(false);
        this.pnlHeader.PerformLayout();
        this.tabMain.ResumeLayout(false);
        this.tabDashboard.ResumeLayout(false);
        this.grpConnection.ResumeLayout(false);
        this.grpConnection.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
        this.grpDiagnostics.ResumeLayout(false);
        this.grpDiagnostics.PerformLayout();
        this.grpStats.ResumeLayout(false);
        this.grpStats.PerformLayout();
        this.tabCommands.ResumeLayout(false);
        this.grpCommandControl.ResumeLayout(false);
        this.grpCommandControl.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.numCommandTimeout)).EndInit();
        this.grpTerminal.ResumeLayout(false);
        this.pnlTerminalBottom.ResumeLayout(false);
        this.pnlTerminalBottom.PerformLayout();
        this.pnlInspectorTop.ResumeLayout(false);
        this.pnlInspectorTop.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvFrames)).EndInit();
        this.pnlDetailsHeader.ResumeLayout(false);
        this.pnlDetailsHeader.PerformLayout();
        this.grpFrameDetails.ResumeLayout(false);
        this.splitInspector.Panel1.ResumeLayout(false);
        this.splitInspector.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.splitInspector)).EndInit();
        this.splitInspector.ResumeLayout(false);
        this.tabInspector.ResumeLayout(false);
        this.statusStrip.ResumeLayout(false);
        this.statusStrip.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.Panel pnlHeader;
    private System.Windows.Forms.Label lblHeaderTitle;
    private System.Windows.Forms.Label lblHeaderSubtitle;
    private System.Windows.Forms.Panel pnlStatusBadge;
    private System.Windows.Forms.Label lblStatusBadge;

    private System.Windows.Forms.TabControl tabMain;
    private System.Windows.Forms.TabPage tabDashboard;
    private System.Windows.Forms.TabPage tabCommands;
    private System.Windows.Forms.TabPage tabInspector;

    private System.Windows.Forms.GroupBox grpConnection;
    private System.Windows.Forms.Label lblHost;
    private System.Windows.Forms.TextBox txtHost;
    private System.Windows.Forms.Label lblPort;
    private System.Windows.Forms.NumericUpDown numPort;
    private System.Windows.Forms.Button btnConnect;
    private System.Windows.Forms.Button btnDisconnect;
    private System.Windows.Forms.Label lblConnectionStatus;

    private System.Windows.Forms.GroupBox grpDiagnostics;
    private System.Windows.Forms.Label lblPingPrompt;
    private System.Windows.Forms.TextBox txtPingPayload;
    private System.Windows.Forms.Button btnPing;
    private System.Windows.Forms.Label lblPingResult;
    private System.Windows.Forms.Label lblAgentIdPrompt;
    private System.Windows.Forms.TextBox txtAgentId;
    private System.Windows.Forms.Button btnHeartbeat;
    private System.Windows.Forms.CheckBox chkAutoHeartbeat;
    private System.Windows.Forms.Label lblHeartbeatResult;

    private System.Windows.Forms.GroupBox grpStats;
    private System.Windows.Forms.Label lblStatsSent;
    private System.Windows.Forms.Label lblStatsRecv;
    private System.Windows.Forms.Label lblStatsLastSeen;

    private System.Windows.Forms.GroupBox grpCommandControl;
    private System.Windows.Forms.Label lblCommandLinePrompt;
    private System.Windows.Forms.TextBox txtCommandLine;
    private System.Windows.Forms.Label lblCommandTimeoutPrompt;
    private System.Windows.Forms.NumericUpDown numCommandTimeout;
    private System.Windows.Forms.Button btnExecuteCommand;
    private System.Windows.Forms.Label lblCommandIdPrompt;
    private System.Windows.Forms.TextBox txtCommandId;
    private System.Windows.Forms.Button btnGenNewCmdId;
    private System.Windows.Forms.Label lblQuickCommands;
    private System.Windows.Forms.Button btnQuickWhoami;
    private System.Windows.Forms.Button btnQuickHostname;
    private System.Windows.Forms.Button btnQuickIpconfig;
    private System.Windows.Forms.Button btnQuickDir;

    private System.Windows.Forms.GroupBox grpTerminal;
    private System.Windows.Forms.RichTextBox rtbTerminal;
    private System.Windows.Forms.Panel pnlTerminalBottom;
    private System.Windows.Forms.Button btnClearTerminal;
    private System.Windows.Forms.CheckBox chkAutoScroll;
    private System.Windows.Forms.Label lblTerminalStatus;

    private System.Windows.Forms.SplitContainer splitInspector;
    private System.Windows.Forms.Panel pnlInspectorTop;
    private System.Windows.Forms.Button btnClearInspector;
    private System.Windows.Forms.CheckBox chkAutoSelectLatest;
    private System.Windows.Forms.Label lblInspectorCount;
    private System.Windows.Forms.DataGridView dgvFrames;
    private System.Windows.Forms.DataGridViewTextBoxColumn colDirection;
    private System.Windows.Forms.DataGridViewTextBoxColumn colTime;
    private System.Windows.Forms.DataGridViewTextBoxColumn colType;
    private System.Windows.Forms.DataGridViewTextBoxColumn colLength;
    private System.Windows.Forms.DataGridViewTextBoxColumn colMessageId;
    private System.Windows.Forms.DataGridViewTextBoxColumn colRequestId;
    private System.Windows.Forms.DataGridViewTextBoxColumn colInReplyTo;
    private System.Windows.Forms.GroupBox grpFrameDetails;
    private System.Windows.Forms.Panel pnlDetailsHeader;
    private System.Windows.Forms.Label lblWireHeader;
    private System.Windows.Forms.TextBox txtWireHeaderHex;
    private System.Windows.Forms.Label lblWireTotalSize;
    private System.Windows.Forms.TextBox txtWireTotalSize;
    private System.Windows.Forms.Button btnCopyJson;
    private System.Windows.Forms.RichTextBox rtbFrameJson;

    private System.Windows.Forms.StatusStrip statusStrip;
    private System.Windows.Forms.ToolStripStatusLabel statusLabel;
    private System.Windows.Forms.ToolStripStatusLabel statusEndpoint;
    private System.Windows.Forms.ToolStripStatusLabel statusProtocol;
}
