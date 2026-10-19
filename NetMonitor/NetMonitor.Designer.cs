namespace NetMonitor
{
    partial class NetMonitor
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NetMonitor));
            this.contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.interfaceMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.interfaceComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.multiNicModeMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.showInFullScreenMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.separatorMenuItem = new System.Windows.Forms.ToolStripSeparator();
            this.autoRunMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.containerPanel = new System.Windows.Forms.Panel();
            this.downloadSpeedLabel = new System.Windows.Forms.Label();
            this.uploadSpeedLabel = new System.Windows.Forms.Label();
            this.downloadLabel = new System.Windows.Forms.Label();
            this.uploadLabel = new System.Windows.Forms.Label();
            this.gifPictureBox = new System.Windows.Forms.PictureBox();
            this.contextMenu.SuspendLayout();
            this.containerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gifPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // contextMenu
            // 
            this.contextMenu.AllowMerge = false;
            this.contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.interfaceMenuItem,
            this.multiNicModeMenuItem,
            this.showInFullScreenMenuItem,
            this.separatorMenuItem,
            this.autoRunMenuItem,
            this.exitMenuItem});
            this.contextMenu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this.contextMenu.Name = "contextMenu";
            this.contextMenu.ShowCheckMargin = true;
            this.contextMenu.ShowImageMargin = false;
            resources.ApplyResources(this.contextMenu, "contextMenu");
            // 
            // interfaceMenuItem
            // 
            this.interfaceMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.interfaceComboBox});
            this.interfaceMenuItem.MergeIndex = 0;
            this.interfaceMenuItem.Name = "interfaceMenuItem";
            this.interfaceMenuItem.Padding = new System.Windows.Forms.Padding(0);
            resources.ApplyResources(this.interfaceMenuItem, "interfaceMenuItem");
            // 
            // interfaceComboBox
            // 
            this.interfaceComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.interfaceComboBox.DropDownWidth = 70;
            resources.ApplyResources(this.interfaceComboBox, "interfaceComboBox");
            this.interfaceComboBox.Name = "interfaceComboBox";
            this.interfaceComboBox.DropDownClosed += new System.EventHandler(this.interfaceComboBox_DropDownClosed);
            // 
            // multiNicModeMenuItem
            // 
            this.multiNicModeMenuItem.CheckOnClick = true;
            this.multiNicModeMenuItem.Name = "multiNicModeMenuItem";
            resources.ApplyResources(this.multiNicModeMenuItem, "multiNicModeMenuItem");
            this.multiNicModeMenuItem.Click += new System.EventHandler(this.multiNicModeMenuItem_Click);
            // 
            // showInFullScreenMenuItem
            // 
            this.showInFullScreenMenuItem.CheckOnClick = true;
            this.showInFullScreenMenuItem.Name = "showInFullScreenMenuItem";
            resources.ApplyResources(this.showInFullScreenMenuItem, "showInFullScreenMenuItem");
            // 
            // separatorMenuItem
            // 
            this.separatorMenuItem.ForeColor = System.Drawing.Color.Black;
            this.separatorMenuItem.Name = "separatorMenuItem";
            resources.ApplyResources(this.separatorMenuItem, "separatorMenuItem");
            // 
            // autoRunMenuItem
            // 
            this.autoRunMenuItem.CheckOnClick = true;
            this.autoRunMenuItem.Name = "autoRunMenuItem";
            resources.ApplyResources(this.autoRunMenuItem, "autoRunMenuItem");
            this.autoRunMenuItem.Click += new System.EventHandler(this.autoRunMenuItem_Click);
            // 
            // exitMenuItem
            // 
            this.exitMenuItem.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.exitMenuItem.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.exitMenuItem.MergeIndex = 0;
            this.exitMenuItem.Name = "exitMenuItem";
            resources.ApplyResources(this.exitMenuItem, "exitMenuItem");
            this.exitMenuItem.MouseDown += new System.Windows.Forms.MouseEventHandler(this.exitMenuItem_MouseDown);
            // 
            // containerPanel
            // 
            this.containerPanel.BackColor = System.Drawing.SystemColors.Control;
            resources.ApplyResources(this.containerPanel, "containerPanel");
            this.containerPanel.ContextMenuStrip = this.contextMenu;
            this.containerPanel.Controls.Add(this.downloadSpeedLabel);
            this.containerPanel.Controls.Add(this.uploadSpeedLabel);
            this.containerPanel.Controls.Add(this.downloadLabel);
            this.containerPanel.Controls.Add(this.uploadLabel);
            this.containerPanel.Controls.Add(this.gifPictureBox);
            this.containerPanel.Name = "containerPanel";
            this.containerPanel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // downloadSpeedLabel
            // 
            resources.ApplyResources(this.downloadSpeedLabel, "downloadSpeedLabel");
            this.downloadSpeedLabel.BackColor = System.Drawing.Color.Transparent;
            this.downloadSpeedLabel.ContextMenuStrip = this.contextMenu;
            this.downloadSpeedLabel.ForeColor = System.Drawing.Color.Black;
            this.downloadSpeedLabel.Name = "downloadSpeedLabel";
            this.downloadSpeedLabel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // uploadSpeedLabel
            // 
            resources.ApplyResources(this.uploadSpeedLabel, "uploadSpeedLabel");
            this.uploadSpeedLabel.BackColor = System.Drawing.Color.Transparent;
            this.uploadSpeedLabel.ContextMenuStrip = this.contextMenu;
            this.uploadSpeedLabel.ForeColor = System.Drawing.Color.Black;
            this.uploadSpeedLabel.Name = "uploadSpeedLabel";
            this.uploadSpeedLabel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // downloadLabel
            // 
            resources.ApplyResources(this.downloadLabel, "downloadLabel");
            this.downloadLabel.BackColor = System.Drawing.Color.Transparent;
            this.downloadLabel.ContextMenuStrip = this.contextMenu;
            this.downloadLabel.ForeColor = System.Drawing.Color.Black;
            this.downloadLabel.Name = "downloadLabel";
            // 
            // uploadLabel
            // 
            resources.ApplyResources(this.uploadLabel, "uploadLabel");
            this.uploadLabel.BackColor = System.Drawing.Color.Transparent;
            this.uploadLabel.ContextMenuStrip = this.contextMenu;
            this.uploadLabel.ForeColor = System.Drawing.Color.Black;
            this.uploadLabel.Name = "uploadLabel";
            // 
            // gifPictureBox
            // 
            this.gifPictureBox.BackColor = System.Drawing.Color.Transparent;
            resources.ApplyResources(this.gifPictureBox, "gifPictureBox");
            this.gifPictureBox.ContextMenuStrip = this.contextMenu;
            this.gifPictureBox.Name = "gifPictureBox";
            this.gifPictureBox.TabStop = false;
            this.gifPictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // NetMonitor
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.Controls.Add(this.containerPanel);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "NetMonitor";
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.NetMonitor_FormClosing);
            this.Load += new System.EventHandler(this.NetMonitor_Load);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            this.contextMenu.ResumeLayout(false);
            this.containerPanel.ResumeLayout(false);
            this.containerPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gifPictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.PictureBox gifPictureBox;
        private System.Windows.Forms.Label downloadSpeedLabel;
        private System.Windows.Forms.Label uploadSpeedLabel;
        private System.Windows.Forms.Panel containerPanel;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem exitMenuItem;
        private System.Windows.Forms.ToolStripMenuItem interfaceMenuItem;
        private System.Windows.Forms.ToolStripComboBox interfaceComboBox;
        private System.Windows.Forms.ToolStripSeparator separatorMenuItem;
        private System.Windows.Forms.ToolStripMenuItem autoRunMenuItem;
        private System.Windows.Forms.Label downloadLabel;
        private System.Windows.Forms.Label uploadLabel;
        private System.Windows.Forms.ToolStripMenuItem showInFullScreenMenuItem;
        private System.Windows.Forms.ToolStripMenuItem multiNicModeMenuItem;
    }
}

