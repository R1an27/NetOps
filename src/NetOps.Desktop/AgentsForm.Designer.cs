namespace NetOps.Desktop;

partial class AgentsForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeLifetime();
            components?.Dispose();
        }
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
        statsLayout = new TableLayoutPanel();
        totalPanel = new AntdUI.Panel();
        totalStatLayout = new TableLayoutPanel();
        lblTotalCaption = new AntdUI.Label();
        lblTotalCount = new AntdUI.Label();
        onlinePanel = new AntdUI.Panel();
        onlineStatLayout = new TableLayoutPanel();
        lblOnlineCaption = new AntdUI.Label();
        lblOnlineCount = new AntdUI.Label();
        offlinePanel = new AntdUI.Panel();
        offlineStatLayout = new TableLayoutPanel();
        lblOfflineCaption = new AntdUI.Label();
        lblOfflineCount = new AntdUI.Label();
        toolbarLayout = new TableLayoutPanel();
        txtSearch = new AntdUI.Input();
        btnAll = new AntdUI.Button();
        btnOnline = new AntdUI.Button();
        btnOffline = new AntdUI.Button();
        btnRefresh = new AntdUI.Button();
        bodyLayout = new TableLayoutPanel();
        listPanel = new AntdUI.Panel();
        listLayout = new TableLayoutPanel();
        listHeading = new TableLayoutPanel();
        lblListTitle = new AntdUI.Label();
        lblResults = new AntdUI.Label();
        listDivider = new System.Windows.Forms.Panel();
        tblAgents = new AntdUI.Table();
        lblUpdated = new AntdUI.Label();
        detailPanel = new AntdUI.Panel();
        detailLayout = new TableLayoutPanel();
        lblDetailsTitle = new AntdUI.Label();
        lblSelectedName = new AntdUI.Label();
        badgeSelected = new AntdUI.Badge();
        detailDivider = new System.Windows.Forms.Panel();
        lblIpCaption = new AntdUI.Label();
        lblSelectedIp = new AntdUI.Label();
        lblOsCaption = new AntdUI.Label();
        lblSelectedOs = new AntdUI.Label();
        lblIdCaption = new AntdUI.Label();
        lblSelectedId = new AntdUI.Label();
        footerLayout = new TableLayoutPanel();
        lblMessage = new AntdUI.Label();
        btnCancel = new AntdUI.Button();
        btnUseAgent = new AntdUI.Button();
        shellLayout.SuspendLayout();
        contentLayout.SuspendLayout();
        headingLayout.SuspendLayout();
        headingText.SuspendLayout();
        statsLayout.SuspendLayout();
        totalPanel.SuspendLayout();
        totalStatLayout.SuspendLayout();
        onlinePanel.SuspendLayout();
        onlineStatLayout.SuspendLayout();
        offlinePanel.SuspendLayout();
        offlineStatLayout.SuspendLayout();
        toolbarLayout.SuspendLayout();
        bodyLayout.SuspendLayout();
        listPanel.SuspendLayout();
        listLayout.SuspendLayout();
        listHeading.SuspendLayout();
        detailPanel.SuspendLayout();
        detailLayout.SuspendLayout();
        footerLayout.SuspendLayout();
        SuspendLayout();
        shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // shellLayout
        shellLayout.Name = "shellLayout";
        shellLayout.Dock = DockStyle.Fill;
        shellLayout.Margin = new Padding(0);
        shellLayout.ColumnCount = 1;
        shellLayout.RowCount = 2;
        shellLayout.Controls.Add(titleBar, 0, 0);
        shellLayout.Controls.Add(contentLayout, 0, 1);

        // titleBar
        titleBar.Name = "titleBar";
        titleBar.Dock = DockStyle.Fill;
        titleBar.Margin = new Padding(0);
        titleBar.Text = "NetOps";
        titleBar.SubText = "/  Agent directory";
        titleBar.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        titleBar.SubFont = new Font("Segoe UI", 9F);
        titleBar.BackColor = Color.FromArgb(16, 28, 48);
        titleBar.ForeColor = Color.FromArgb(241, 245, 249);
        titleBar.ColorScheme = AntdUI.TAMode.Dark;
        titleBar.ShowIcon = true;
        titleBar.IconSvg = "<svg viewBox=\"0 0 32 32\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"32\" height=\"32\" rx=\"9\" fill=\"#2563EB\"/><path d=\"m8 9 2-2 9 9-9 9-2-2 7-7-7-7zm11 13h7v3h-7v-3z\" fill=\"#FFFFFF\"/></svg>";
        titleBar.IconRatio = 0.65F;
        titleBar.ShowButton = true;
        titleBar.MinimizeBox = false;
        titleBar.UseForeColorDrawIcons = true;
        titleBar.CloseSize = 48;
        titleBar.Padding = new Padding(18, 0, 0, 0);

        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        // contentLayout
        contentLayout.Name = "contentLayout";
        contentLayout.Dock = DockStyle.Fill;
        contentLayout.Margin = new Padding(0);
        contentLayout.ColumnCount = 1;
        contentLayout.RowCount = 7;
        contentLayout.Padding = new Padding(24, 20, 24, 16);
        contentLayout.Controls.Add(headingLayout, 0, 0);
        contentLayout.Controls.Add(toolbarLayout, 0, 2);
        contentLayout.Controls.Add(bodyLayout, 0, 4);
        contentLayout.Controls.Add(footerLayout, 0, 6);

        headingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
        headingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // headingLayout
        headingLayout.Name = "headingLayout";
        headingLayout.Dock = DockStyle.Fill;
        headingLayout.Margin = new Padding(0);
        headingLayout.ColumnCount = 2;
        headingLayout.RowCount = 1;
        headingLayout.Controls.Add(headingText, 0, 0);
        headingLayout.Controls.Add(statsLayout, 1, 0);

        headingText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        headingText.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // headingText
        headingText.Name = "headingText";
        headingText.Dock = DockStyle.Fill;
        headingText.Margin = new Padding(0);
        headingText.ColumnCount = 1;
        headingText.RowCount = 3;
        headingText.Controls.Add(lblEyebrow, 0, 0);
        headingText.Controls.Add(lblTitle, 0, 1);
        headingText.Controls.Add(lblSubtitle, 0, 2);

        // lblEyebrow
        lblEyebrow.Name = "lblEyebrow";
        lblEyebrow.Dock = DockStyle.Fill;
        lblEyebrow.Margin = new Padding(0);
        lblEyebrow.Text = "FLEET  /  AGENTS";
        lblEyebrow.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblEyebrow.ForeColor = Color.FromArgb(100, 116, 139);
        lblEyebrow.TextMultiLine = false;

        // lblTitle
        lblTitle.Name = "lblTitle";
        lblTitle.Dock = DockStyle.Fill;
        lblTitle.Margin = new Padding(0);
        lblTitle.Text = "Choose your agent";
        lblTitle.Font = new Font("Segoe UI", 23F, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(21, 35, 57);
        lblTitle.TextMultiLine = false;

        // lblSubtitle
        lblSubtitle.Name = "lblSubtitle";
        lblSubtitle.Dock = DockStyle.Fill;
        lblSubtitle.Margin = new Padding(0);
        lblSubtitle.Text = "Find an online machine for your next command.";
        lblSubtitle.Font = new Font("Segoe UI", 10F);
        lblSubtitle.ForeColor = Color.FromArgb(97, 115, 139);
        lblSubtitle.TextMultiLine = false;

        statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // statsLayout
        statsLayout.Name = "statsLayout";
        statsLayout.Dock = DockStyle.Fill;
        statsLayout.Margin = new Padding(0);
        statsLayout.ColumnCount = 3;
        statsLayout.RowCount = 1;
        statsLayout.Controls.Add(totalPanel, 0, 0);
        statsLayout.Controls.Add(onlinePanel, 1, 0);
        statsLayout.Controls.Add(offlinePanel, 2, 0);

        // totalPanel
        totalPanel.Name = "totalPanel";
        totalPanel.Dock = DockStyle.Fill;
        totalPanel.Margin = new Padding(8, 6, 0, 6);
        totalPanel.Back = Color.FromArgb(255, 255, 255);
        totalPanel.Radius = 10;
        totalPanel.BorderWidth = 1F;
        totalPanel.BorderColor = Color.FromArgb(220, 228, 238);
        totalPanel.Padding = new Padding(12, 8, 8, 8);
        totalPanel.Controls.Add(totalStatLayout);

        totalStatLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        totalStatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        totalStatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // totalStatLayout
        totalStatLayout.Name = "totalStatLayout";
        totalStatLayout.Dock = DockStyle.Fill;
        totalStatLayout.Margin = new Padding(0);
        totalStatLayout.ColumnCount = 1;
        totalStatLayout.RowCount = 2;
        totalStatLayout.BackColor = Color.FromArgb(255, 255, 255);
        totalStatLayout.Controls.Add(lblTotalCaption, 0, 0);
        totalStatLayout.Controls.Add(lblTotalCount, 0, 1);

        // lblTotalCaption
        lblTotalCaption.Name = "lblTotalCaption";
        lblTotalCaption.Dock = DockStyle.Fill;
        lblTotalCaption.Margin = new Padding(0);
        lblTotalCaption.Text = "KNOWN";
        lblTotalCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblTotalCaption.ForeColor = Color.FromArgb(100, 116, 139);
        lblTotalCaption.TextMultiLine = false;

        // lblTotalCount
        lblTotalCount.Name = "lblTotalCount";
        lblTotalCount.Dock = DockStyle.Fill;
        lblTotalCount.Margin = new Padding(0);
        lblTotalCount.Text = "0";
        lblTotalCount.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
        lblTotalCount.ForeColor = Color.FromArgb(51, 65, 85);
        lblTotalCount.TextMultiLine = false;

        // onlinePanel
        onlinePanel.Name = "onlinePanel";
        onlinePanel.Dock = DockStyle.Fill;
        onlinePanel.Margin = new Padding(8, 6, 0, 6);
        onlinePanel.Back = Color.FromArgb(255, 255, 255);
        onlinePanel.Radius = 10;
        onlinePanel.BorderWidth = 1F;
        onlinePanel.BorderColor = Color.FromArgb(220, 228, 238);
        onlinePanel.Padding = new Padding(12, 8, 8, 8);
        onlinePanel.Controls.Add(onlineStatLayout);

        onlineStatLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        onlineStatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        onlineStatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // onlineStatLayout
        onlineStatLayout.Name = "onlineStatLayout";
        onlineStatLayout.Dock = DockStyle.Fill;
        onlineStatLayout.Margin = new Padding(0);
        onlineStatLayout.ColumnCount = 1;
        onlineStatLayout.RowCount = 2;
        onlineStatLayout.BackColor = Color.FromArgb(255, 255, 255);
        onlineStatLayout.Controls.Add(lblOnlineCaption, 0, 0);
        onlineStatLayout.Controls.Add(lblOnlineCount, 0, 1);

        // lblOnlineCaption
        lblOnlineCaption.Name = "lblOnlineCaption";
        lblOnlineCaption.Dock = DockStyle.Fill;
        lblOnlineCaption.Margin = new Padding(0);
        lblOnlineCaption.Text = "ONLINE";
        lblOnlineCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblOnlineCaption.ForeColor = Color.FromArgb(100, 116, 139);
        lblOnlineCaption.TextMultiLine = false;

        // lblOnlineCount
        lblOnlineCount.Name = "lblOnlineCount";
        lblOnlineCount.Dock = DockStyle.Fill;
        lblOnlineCount.Margin = new Padding(0);
        lblOnlineCount.Text = "0";
        lblOnlineCount.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
        lblOnlineCount.ForeColor = Color.FromArgb(21, 128, 61);
        lblOnlineCount.TextMultiLine = false;

        // offlinePanel
        offlinePanel.Name = "offlinePanel";
        offlinePanel.Dock = DockStyle.Fill;
        offlinePanel.Margin = new Padding(8, 6, 0, 6);
        offlinePanel.Back = Color.FromArgb(255, 255, 255);
        offlinePanel.Radius = 10;
        offlinePanel.BorderWidth = 1F;
        offlinePanel.BorderColor = Color.FromArgb(220, 228, 238);
        offlinePanel.Padding = new Padding(12, 8, 8, 8);
        offlinePanel.Controls.Add(offlineStatLayout);

        offlineStatLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        offlineStatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        offlineStatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // offlineStatLayout
        offlineStatLayout.Name = "offlineStatLayout";
        offlineStatLayout.Dock = DockStyle.Fill;
        offlineStatLayout.Margin = new Padding(0);
        offlineStatLayout.ColumnCount = 1;
        offlineStatLayout.RowCount = 2;
        offlineStatLayout.BackColor = Color.FromArgb(255, 255, 255);
        offlineStatLayout.Controls.Add(lblOfflineCaption, 0, 0);
        offlineStatLayout.Controls.Add(lblOfflineCount, 0, 1);

        // lblOfflineCaption
        lblOfflineCaption.Name = "lblOfflineCaption";
        lblOfflineCaption.Dock = DockStyle.Fill;
        lblOfflineCaption.Margin = new Padding(0);
        lblOfflineCaption.Text = "OFFLINE";
        lblOfflineCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblOfflineCaption.ForeColor = Color.FromArgb(100, 116, 139);
        lblOfflineCaption.TextMultiLine = false;

        // lblOfflineCount
        lblOfflineCount.Name = "lblOfflineCount";
        lblOfflineCount.Dock = DockStyle.Fill;
        lblOfflineCount.Margin = new Padding(0);
        lblOfflineCount.Text = "0";
        lblOfflineCount.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
        lblOfflineCount.ForeColor = Color.FromArgb(100, 116, 139);
        lblOfflineCount.TextMultiLine = false;

        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12F));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
        toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // toolbarLayout
        toolbarLayout.Name = "toolbarLayout";
        toolbarLayout.Dock = DockStyle.Fill;
        toolbarLayout.Margin = new Padding(0);
        toolbarLayout.ColumnCount = 9;
        toolbarLayout.RowCount = 1;
        toolbarLayout.Controls.Add(txtSearch, 0, 0);
        toolbarLayout.Controls.Add(btnAll, 2, 0);
        toolbarLayout.Controls.Add(btnOnline, 4, 0);
        toolbarLayout.Controls.Add(btnOffline, 6, 0);
        toolbarLayout.Controls.Add(btnRefresh, 8, 0);

        // txtSearch
        txtSearch.Name = "txtSearch";
        txtSearch.Dock = DockStyle.Fill;
        txtSearch.Margin = new Padding(0);
        txtSearch.Radius = 8;
        txtSearch.BorderColor = Color.FromArgb(207, 216, 229);
        txtSearch.BorderActive = Color.FromArgb(37, 99, 235);
        txtSearch.BorderHover = Color.FromArgb(148, 163, 184);
        txtSearch.BackColor = Color.FromArgb(255, 255, 255);
        txtSearch.PlaceholderText = "Search hostname, IP, OS or agent ID";
        txtSearch.PlaceholderColor = Color.FromArgb(120, 136, 156);
        txtSearch.PrefixSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M10 3a7 7 0 1 0 4.9 12l5.4 5.4 1.4-1.4-5.4-5.4A7 7 0 0 0 10 3zm0 2a5 5 0 1 1 0 10 5 5 0 0 1 0-10z\"/></svg>";
        txtSearch.PrefixFore = Color.FromArgb(116, 134, 158);
        txtSearch.AllowClear = true;
        txtSearch.TabIndex = 0;
        txtSearch.AccessibleName = "Search agents";
        txtSearch.AccessibleDescription = "Type to filter. Use arrow keys to select a row and Enter to choose an online agent.";
        txtSearch.TextChanged += txtSearch_TextChanged;

        // btnAll
        btnAll.Name = "btnAll";
        btnAll.Dock = DockStyle.Fill;
        btnAll.Margin = new Padding(0);
        btnAll.Text = "All agents";
        btnAll.Radius = 8;
        btnAll.Font = new Font("Segoe UI", 9F);
        btnAll.Cursor = Cursors.Hand;
        btnAll.DefaultBack = Color.FromArgb(255, 255, 255);
        btnAll.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnAll.BorderWidth = 1F;
        btnAll.ForeColor = Color.FromArgb(90, 108, 132);
        btnAll.TabIndex = 1;
        btnAll.Click += btnAll_Click;

        // btnOnline
        btnOnline.Name = "btnOnline";
        btnOnline.Dock = DockStyle.Fill;
        btnOnline.Margin = new Padding(0);
        btnOnline.Text = "Online";
        btnOnline.Radius = 8;
        btnOnline.Font = new Font("Segoe UI", 9F);
        btnOnline.Cursor = Cursors.Hand;
        btnOnline.DefaultBack = Color.FromArgb(255, 255, 255);
        btnOnline.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnOnline.BorderWidth = 1F;
        btnOnline.ForeColor = Color.FromArgb(90, 108, 132);
        btnOnline.TabIndex = 1;
        btnOnline.Click += btnOnline_Click;

        // btnOffline
        btnOffline.Name = "btnOffline";
        btnOffline.Dock = DockStyle.Fill;
        btnOffline.Margin = new Padding(0);
        btnOffline.Text = "Offline";
        btnOffline.Radius = 8;
        btnOffline.Font = new Font("Segoe UI", 9F);
        btnOffline.Cursor = Cursors.Hand;
        btnOffline.DefaultBack = Color.FromArgb(255, 255, 255);
        btnOffline.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnOffline.BorderWidth = 1F;
        btnOffline.ForeColor = Color.FromArgb(90, 108, 132);
        btnOffline.TabIndex = 1;
        btnOffline.Click += btnOffline_Click;

        // btnRefresh
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Dock = DockStyle.Fill;
        btnRefresh.Margin = new Padding(0);
        btnRefresh.Text = "Refresh";
        btnRefresh.Radius = 8;
        btnRefresh.Font = new Font("Segoe UI", 9F);
        btnRefresh.Cursor = Cursors.Hand;
        btnRefresh.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M19 3v3.1A9 9 0 0 0 3 12h2a7 7 0 0 1 12.4-4.5H14v2h7V3h-2zM5 21v-3.1A9 9 0 0 0 21 12h-2a7 7 0 0 1-12.4 4.5H10v-2H3V21h2z\"/></svg>";
        btnRefresh.IconSize = new Size(16, 16);
        btnRefresh.DefaultBack = Color.FromArgb(255, 255, 255);
        btnRefresh.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnRefresh.BorderWidth = 1F;
        btnRefresh.ForeColor = Color.FromArgb(51, 65, 85);
        btnRefresh.BackHover = Color.FromArgb(239, 244, 252);
        btnRefresh.Enabled = false;
        btnRefresh.TabIndex = 2;
        btnRefresh.Click += btnRefresh_Click;

        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18F));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // bodyLayout
        bodyLayout.Name = "bodyLayout";
        bodyLayout.Dock = DockStyle.Fill;
        bodyLayout.Margin = new Padding(0);
        bodyLayout.ColumnCount = 3;
        bodyLayout.RowCount = 1;
        bodyLayout.Controls.Add(listPanel, 0, 0);
        bodyLayout.Controls.Add(detailPanel, 2, 0);

        // listPanel
        listPanel.Name = "listPanel";
        listPanel.Dock = DockStyle.Fill;
        listPanel.Margin = new Padding(0);
        listPanel.Back = Color.FromArgb(255, 255, 255);
        listPanel.Radius = 12;
        listPanel.BorderWidth = 1F;
        listPanel.BorderColor = Color.FromArgb(220, 228, 238);
        listPanel.Padding = new Padding(16, 10, 16, 8);
        listPanel.Controls.Add(listLayout);

        listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        // listLayout
        listLayout.Name = "listLayout";
        listLayout.Dock = DockStyle.Fill;
        listLayout.Margin = new Padding(0);
        listLayout.ColumnCount = 1;
        listLayout.RowCount = 4;
        listLayout.BackColor = Color.FromArgb(255, 255, 255);
        listLayout.Controls.Add(listHeading, 0, 0);
        listLayout.Controls.Add(listDivider, 0, 1);
        listLayout.Controls.Add(tblAgents, 0, 2);
        listLayout.Controls.Add(lblUpdated, 0, 3);

        listHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        listHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        listHeading.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // listHeading
        listHeading.Name = "listHeading";
        listHeading.Dock = DockStyle.Fill;
        listHeading.Margin = new Padding(0);
        listHeading.ColumnCount = 2;
        listHeading.RowCount = 1;
        listHeading.BackColor = Color.FromArgb(255, 255, 255);
        listHeading.Controls.Add(lblListTitle, 0, 0);
        listHeading.Controls.Add(lblResults, 1, 0);

        // lblListTitle
        lblListTitle.Name = "lblListTitle";
        lblListTitle.Dock = DockStyle.Fill;
        lblListTitle.Margin = new Padding(0);
        lblListTitle.Text = "Registered machines";
        lblListTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblListTitle.ForeColor = Color.FromArgb(36, 54, 77);
        lblListTitle.TextMultiLine = false;

        // lblResults
        lblResults.Name = "lblResults";
        lblResults.Dock = DockStyle.Fill;
        lblResults.Margin = new Padding(0);
        lblResults.Text = "0 of 0 agents";
        lblResults.Font = new Font("Segoe UI", 9F);
        lblResults.ForeColor = Color.FromArgb(100, 116, 139);
        lblResults.TextMultiLine = false;
        lblResults.TextAlign = ContentAlignment.MiddleRight;

        // listDivider
        listDivider.Name = "listDivider";
        listDivider.Dock = DockStyle.Fill;
        listDivider.Margin = new Padding(0);
        listDivider.BackColor = Color.FromArgb(230, 235, 242);

        // tblAgents
        tblAgents.Name = "tblAgents";
        tblAgents.Dock = DockStyle.Fill;
        tblAgents.Margin = new Padding(0);
        tblAgents.Font = new Font("Segoe UI", 9.5F);
        tblAgents.Back = Color.FromArgb(255, 255, 255);
        tblAgents.ForeColor = Color.FromArgb(36, 54, 77);
        tblAgents.ColumnFont = new Font("Segoe UI", 8F, FontStyle.Bold);
        tblAgents.ColumnBack = Color.FromArgb(247, 249, 252);
        tblAgents.ColumnFore = Color.FromArgb(100, 116, 139);
        tblAgents.RowHoverBg = Color.FromArgb(242, 246, 253);
        tblAgents.RowSelectedBg = Color.FromArgb(232, 241, 255);
        tblAgents.RowSelectedFore = Color.FromArgb(21, 63, 119);
        tblAgents.BorderColor = Color.FromArgb(230, 235, 242);
        tblAgents.BorderWidth = 1F;
        tblAgents.Radius = 6;
        tblAgents.RowHeight = 44;
        tblAgents.RowHeightHeader = 38;
        tblAgents.Gap = 10;
        tblAgents.GapCell = 8;
        tblAgents.FixedHeader = true;
        tblAgents.EmptyHeader = true;
        tblAgents.EmptyText = "No agents registered yet. Connect an agent and refresh.";
        tblAgents.AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill;
        tblAgents.EnableHeaderResizing = true;
        tblAgents.MultipleRows = false;
        tblAgents.LostFocusClearSelection = false;
        tblAgents.TabIndex = 3;
        tblAgents.TabStop = true;
        tblAgents.AccessibleName = "Registered agents";
        tblAgents.CellClick += tblAgents_CellClick;
        tblAgents.CellDoubleClick += tblAgents_CellDoubleClick;
        tblAgents.CellFocused += tblAgents_CellFocused;

        // lblUpdated
        lblUpdated.Name = "lblUpdated";
        lblUpdated.Dock = DockStyle.Fill;
        lblUpdated.Margin = new Padding(0);
        lblUpdated.Text = "Refresh to fetch the latest agent status.";
        lblUpdated.Font = new Font("Segoe UI", 8F);
        lblUpdated.ForeColor = Color.FromArgb(116, 134, 158);
        lblUpdated.TextMultiLine = false;

        // detailPanel
        detailPanel.Name = "detailPanel";
        detailPanel.Dock = DockStyle.Fill;
        detailPanel.Margin = new Padding(0);
        detailPanel.Back = Color.FromArgb(255, 255, 255);
        detailPanel.Radius = 12;
        detailPanel.BorderWidth = 1F;
        detailPanel.BorderColor = Color.FromArgb(220, 228, 238);
        detailPanel.Padding = new Padding(20);
        detailPanel.AutoScroll = true;
        detailPanel.Controls.Add(detailLayout);

        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // detailLayout
        detailLayout.Name = "detailLayout";
        detailLayout.Dock = DockStyle.Top;
        detailLayout.AutoSize = true;
        detailLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        detailLayout.Margin = new Padding(0);
        detailLayout.ColumnCount = 1;
        detailLayout.RowCount = 17;
        detailLayout.BackColor = Color.FromArgb(255, 255, 255);
        detailLayout.Controls.Add(lblDetailsTitle, 0, 0);
        detailLayout.Controls.Add(lblSelectedName, 0, 2);
        detailLayout.Controls.Add(badgeSelected, 0, 3);
        detailLayout.Controls.Add(detailDivider, 0, 5);
        detailLayout.Controls.Add(lblIpCaption, 0, 7);
        detailLayout.Controls.Add(lblSelectedIp, 0, 8);
        detailLayout.Controls.Add(lblOsCaption, 0, 10);
        detailLayout.Controls.Add(lblSelectedOs, 0, 11);
        detailLayout.Controls.Add(lblIdCaption, 0, 13);
        detailLayout.Controls.Add(lblSelectedId, 0, 14);

        // lblDetailsTitle
        lblDetailsTitle.Name = "lblDetailsTitle";
        lblDetailsTitle.Dock = DockStyle.Fill;
        lblDetailsTitle.Margin = new Padding(0);
        lblDetailsTitle.Text = "SELECTION DETAILS";
        lblDetailsTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblDetailsTitle.ForeColor = Color.FromArgb(100, 116, 139);
        lblDetailsTitle.TextMultiLine = false;

        // lblSelectedName
        lblSelectedName.Name = "lblSelectedName";
        lblSelectedName.Dock = DockStyle.Fill;
        lblSelectedName.Margin = new Padding(0);
        lblSelectedName.Text = "No agent selected";
        lblSelectedName.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        lblSelectedName.ForeColor = Color.FromArgb(21, 35, 57);
        lblSelectedName.TextMultiLine = false;
        lblSelectedName.AutoEllipsis = true;
        lblSelectedName.ShowTooltip = true;

        // badgeSelected
        badgeSelected.Name = "badgeSelected";
        badgeSelected.Dock = DockStyle.Fill;
        badgeSelected.Margin = new Padding(0);
        badgeSelected.Font = new Font("Segoe UI", 9F);
        badgeSelected.ForeColor = Color.FromArgb(100, 116, 139);
        badgeSelected.Text = "Choose a row";
        badgeSelected.State = AntdUI.TState.Default;
        badgeSelected.Gap = 5;
        badgeSelected.DotRatio = 0.25F;

        // detailDivider
        detailDivider.Name = "detailDivider";
        detailDivider.Dock = DockStyle.Fill;
        detailDivider.Margin = new Padding(0);
        detailDivider.BackColor = Color.FromArgb(230, 235, 242);

        // lblIpCaption
        lblIpCaption.Name = "lblIpCaption";
        lblIpCaption.Dock = DockStyle.Fill;
        lblIpCaption.Margin = new Padding(0);
        lblIpCaption.Text = "IP ADDRESS";
        lblIpCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblIpCaption.ForeColor = Color.FromArgb(116, 134, 158);
        lblIpCaption.TextMultiLine = false;

        // lblSelectedIp
        lblSelectedIp.Name = "lblSelectedIp";
        lblSelectedIp.Dock = DockStyle.Fill;
        lblSelectedIp.Margin = new Padding(0);
        lblSelectedIp.Text = "—";
        lblSelectedIp.Font = new Font("Consolas", 10F);
        lblSelectedIp.ForeColor = Color.FromArgb(36, 54, 77);
        lblSelectedIp.TextMultiLine = false;
        lblSelectedIp.AutoEllipsis = true;

        // lblOsCaption
        lblOsCaption.Name = "lblOsCaption";
        lblOsCaption.Dock = DockStyle.Fill;
        lblOsCaption.Margin = new Padding(0);
        lblOsCaption.Text = "OPERATING SYSTEM";
        lblOsCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblOsCaption.ForeColor = Color.FromArgb(116, 134, 158);
        lblOsCaption.TextMultiLine = false;

        // lblSelectedOs
        lblSelectedOs.Name = "lblSelectedOs";
        lblSelectedOs.Dock = DockStyle.Fill;
        lblSelectedOs.Margin = new Padding(0);
        lblSelectedOs.Text = "—";
        lblSelectedOs.Font = new Font("Segoe UI", 10F);
        lblSelectedOs.ForeColor = Color.FromArgb(36, 54, 77);
        lblSelectedOs.TextMultiLine = true;
        lblSelectedOs.TextAlign = ContentAlignment.TopLeft;
        lblSelectedOs.ShowTooltip = true;

        // lblIdCaption
        lblIdCaption.Name = "lblIdCaption";
        lblIdCaption.Dock = DockStyle.Fill;
        lblIdCaption.Margin = new Padding(0);
        lblIdCaption.Text = "AGENT ID";
        lblIdCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblIdCaption.ForeColor = Color.FromArgb(116, 134, 158);
        lblIdCaption.TextMultiLine = false;

        // lblSelectedId
        lblSelectedId.Name = "lblSelectedId";
        lblSelectedId.Dock = DockStyle.Fill;
        lblSelectedId.Margin = new Padding(0);
        lblSelectedId.Text = "—";
        lblSelectedId.Font = new Font("Consolas", 8.5F);
        lblSelectedId.ForeColor = Color.FromArgb(97, 115, 139);
        lblSelectedId.TextMultiLine = true;
        lblSelectedId.TextAlign = ContentAlignment.TopLeft;
        lblSelectedId.ShowTooltip = true;

        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146F));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // footerLayout
        footerLayout.Name = "footerLayout";
        footerLayout.Dock = DockStyle.Fill;
        footerLayout.Margin = new Padding(0);
        footerLayout.ColumnCount = 4;
        footerLayout.RowCount = 1;
        footerLayout.Controls.Add(lblMessage, 0, 0);
        footerLayout.Controls.Add(btnCancel, 1, 0);
        footerLayout.Controls.Add(btnUseAgent, 3, 0);

        // lblMessage
        lblMessage.Name = "lblMessage";
        lblMessage.Dock = DockStyle.Fill;
        lblMessage.Margin = new Padding(0, 0, 16, 0);
        lblMessage.Text = "Select an online agent to continue.";
        lblMessage.Font = new Font("Segoe UI", 9F);
        lblMessage.ForeColor = Color.FromArgb(97, 115, 139);
        lblMessage.TextMultiLine = false;
        lblMessage.AutoEllipsis = true;
        lblMessage.ShowTooltip = true;

        // btnCancel
        btnCancel.Name = "btnCancel";
        btnCancel.Dock = DockStyle.Fill;
        btnCancel.Margin = new Padding(0, 5, 0, 5);
        btnCancel.Text = "Cancel";
        btnCancel.Radius = 8;
        btnCancel.Font = new Font("Segoe UI", 9F);
        btnCancel.Cursor = Cursors.Hand;
        btnCancel.DefaultBack = Color.FromArgb(255, 255, 255);
        btnCancel.DefaultBorderColor = Color.FromArgb(220, 228, 238);
        btnCancel.BorderWidth = 1F;
        btnCancel.ForeColor = Color.FromArgb(90, 108, 132);
        btnCancel.BackHover = Color.FromArgb(248, 250, 252);
        btnCancel.TabIndex = 4;
        btnCancel.Click += btnCancel_Click;

        // btnUseAgent
        btnUseAgent.Name = "btnUseAgent";
        btnUseAgent.Dock = DockStyle.Fill;
        btnUseAgent.Margin = new Padding(0, 5, 0, 5);
        btnUseAgent.Text = "Use agent";
        btnUseAgent.Radius = 8;
        btnUseAgent.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnUseAgent.Cursor = Cursors.Hand;
        btnUseAgent.Type = AntdUI.TTypeMini.Primary;
        btnUseAgent.BackColor = Color.FromArgb(37, 99, 235);
        btnUseAgent.BackHover = Color.FromArgb(29, 78, 216);
        btnUseAgent.BackActive = Color.FromArgb(30, 64, 175);
        btnUseAgent.IconSvg = "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M4 11h12.2l-4.6-4.6L13 5l7 7-7 7-1.4-1.4 4.6-4.6H4v-2z\"/></svg>";
        btnUseAgent.IconSize = new Size(18, 18);
        btnUseAgent.IconPosition = AntdUI.TAlignMini.Right;
        btnUseAgent.IconGap = 0.5F;
        btnUseAgent.Enabled = false;
        btnUseAgent.TabIndex = 5;
        btnUseAgent.Click += btnUseAgent_Click;

        // AgentsForm
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoHandDpi = false;
        DisableTheme = true;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(243, 246, 250);
        ForeColor = Color.FromArgb(21, 35, 57);
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(1140, 760);
        MinimumSize = new Size(940, 720);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Name = "AgentsForm";
        Text = "NetOps | Agents";
        Controls.Add(shellLayout);
        footerLayout.ResumeLayout(true);
        detailLayout.ResumeLayout(true);
        detailPanel.ResumeLayout(true);
        listHeading.ResumeLayout(true);
        listLayout.ResumeLayout(true);
        listPanel.ResumeLayout(true);
        bodyLayout.ResumeLayout(true);
        toolbarLayout.ResumeLayout(true);
        offlineStatLayout.ResumeLayout(true);
        offlinePanel.ResumeLayout(true);
        onlineStatLayout.ResumeLayout(true);
        onlinePanel.ResumeLayout(true);
        totalStatLayout.ResumeLayout(true);
        totalPanel.ResumeLayout(true);
        statsLayout.ResumeLayout(true);
        headingText.ResumeLayout(true);
        headingLayout.ResumeLayout(true);
        contentLayout.ResumeLayout(true);
        shellLayout.ResumeLayout(true);
        ResumeLayout(true);
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
    private TableLayoutPanel statsLayout;
    private AntdUI.Panel totalPanel;
    private TableLayoutPanel totalStatLayout;
    private AntdUI.Label lblTotalCaption;
    private AntdUI.Label lblTotalCount;
    private AntdUI.Panel onlinePanel;
    private TableLayoutPanel onlineStatLayout;
    private AntdUI.Label lblOnlineCaption;
    private AntdUI.Label lblOnlineCount;
    private AntdUI.Panel offlinePanel;
    private TableLayoutPanel offlineStatLayout;
    private AntdUI.Label lblOfflineCaption;
    private AntdUI.Label lblOfflineCount;
    private TableLayoutPanel toolbarLayout;
    private AntdUI.Input txtSearch;
    private AntdUI.Button btnAll;
    private AntdUI.Button btnOnline;
    private AntdUI.Button btnOffline;
    private AntdUI.Button btnRefresh;
    private TableLayoutPanel bodyLayout;
    private AntdUI.Panel listPanel;
    private TableLayoutPanel listLayout;
    private TableLayoutPanel listHeading;
    private AntdUI.Label lblListTitle;
    private AntdUI.Label lblResults;
    private System.Windows.Forms.Panel listDivider;
    private AntdUI.Table tblAgents;
    private AntdUI.Label lblUpdated;
    private AntdUI.Panel detailPanel;
    private TableLayoutPanel detailLayout;
    private AntdUI.Label lblDetailsTitle;
    private AntdUI.Label lblSelectedName;
    private AntdUI.Badge badgeSelected;
    private System.Windows.Forms.Panel detailDivider;
    private AntdUI.Label lblIpCaption;
    private AntdUI.Label lblSelectedIp;
    private AntdUI.Label lblOsCaption;
    private AntdUI.Label lblSelectedOs;
    private AntdUI.Label lblIdCaption;
    private AntdUI.Label lblSelectedId;
    private TableLayoutPanel footerLayout;
    private AntdUI.Label lblMessage;
    private AntdUI.Button btnCancel;
    private AntdUI.Button btnUseAgent;
}
