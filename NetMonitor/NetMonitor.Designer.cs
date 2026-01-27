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
            this.Menu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Interface_Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.ComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.toolStripSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.AutoRun_ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Exit_Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.panel = new System.Windows.Forms.Panel();
            this.Lable_SpeedDown = new System.Windows.Forms.Label();
            this.Lable_SpeedUP = new System.Windows.Forms.Label();
            this.labeldwon = new System.Windows.Forms.Label();
            this.labelup = new System.Windows.Forms.Label();
            this.pictureBox = new System.Windows.Forms.PictureBox();
            this.Menu.SuspendLayout();
            this.panel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // Menu
            // 
            this.Menu.AllowMerge = false;
            this.Menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Interface_Menu,
            this.toolStripSeparator,
            this.AutoRun_ToolStripMenuItem,
            this.Exit_Menu});
            this.Menu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this.Menu.Name = "contextMenuStrip1";
            this.Menu.ShowCheckMargin = true;
            this.Menu.ShowImageMargin = false;
            resources.ApplyResources(this.Menu, "Menu");
            // 
            // Interface_Menu
            // 
            this.Interface_Menu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ComboBox});
            this.Interface_Menu.MergeIndex = 0;
            this.Interface_Menu.Name = "Interface_Menu";
            this.Interface_Menu.Padding = new System.Windows.Forms.Padding(0);
            resources.ApplyResources(this.Interface_Menu, "Interface_Menu");
            // 
            // ComboBox
            // 
            this.ComboBox.DropDownWidth = 70;
            resources.ApplyResources(this.ComboBox, "ComboBox");
            this.ComboBox.Name = "ComboBox";
            this.ComboBox.DropDownClosed += new System.EventHandler(this.ComboBox_DropDownClosed);
            // 
            // toolStripSeparator
            // 
            this.toolStripSeparator.ForeColor = System.Drawing.Color.Black;
            this.toolStripSeparator.Name = "toolStripSeparator";
            resources.ApplyResources(this.toolStripSeparator, "toolStripSeparator");
            // 
            // AutoRun_ToolStripMenuItem
            // 
            this.AutoRun_ToolStripMenuItem.CheckOnClick = true;
            this.AutoRun_ToolStripMenuItem.Name = "AutoRun_ToolStripMenuItem";
            resources.ApplyResources(this.AutoRun_ToolStripMenuItem, "AutoRun_ToolStripMenuItem");
            this.AutoRun_ToolStripMenuItem.Click += new System.EventHandler(this.AutoRun_ToolStripMenuItem_Click);
            // 
            // Exit_Menu
            // 
            this.Exit_Menu.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.Exit_Menu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Exit_Menu.MergeIndex = 0;
            this.Exit_Menu.Name = "Exit_Menu";
            resources.ApplyResources(this.Exit_Menu, "Exit_Menu");
            this.Exit_Menu.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Exit_Menu_MouseDown);
            // 
            // panel
            // 
            this.panel.BackColor = System.Drawing.SystemColors.Control;
            this.panel.BackgroundImage = global::NetMonitor.Properties.Resources.img_background;
            resources.ApplyResources(this.panel, "panel");
            this.panel.ContextMenuStrip = this.Menu;
            this.panel.Controls.Add(this.Lable_SpeedDown);
            this.panel.Controls.Add(this.Lable_SpeedUP);
            this.panel.Controls.Add(this.labeldwon);
            this.panel.Controls.Add(this.labelup);
            this.panel.Controls.Add(this.pictureBox);
            this.panel.Name = "panel";
            this.panel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // Lable_SpeedDown
            // 
            resources.ApplyResources(this.Lable_SpeedDown, "Lable_SpeedDown");
            this.Lable_SpeedDown.BackColor = System.Drawing.Color.Transparent;
            this.Lable_SpeedDown.ContextMenuStrip = this.Menu;
            this.Lable_SpeedDown.ForeColor = System.Drawing.Color.Black;
            this.Lable_SpeedDown.Name = "Lable_SpeedDown";
            this.Lable_SpeedDown.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // Lable_SpeedUP
            // 
            resources.ApplyResources(this.Lable_SpeedUP, "Lable_SpeedUP");
            this.Lable_SpeedUP.BackColor = System.Drawing.Color.Transparent;
            this.Lable_SpeedUP.ContextMenuStrip = this.Menu;
            this.Lable_SpeedUP.ForeColor = System.Drawing.Color.Black;
            this.Lable_SpeedUP.Name = "Lable_SpeedUP";
            this.Lable_SpeedUP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // labeldwon
            // 
            resources.ApplyResources(this.labeldwon, "labeldwon");
            this.labeldwon.BackColor = System.Drawing.Color.Transparent;
            this.labeldwon.ContextMenuStrip = this.Menu;
            this.labeldwon.ForeColor = System.Drawing.Color.Black;
            this.labeldwon.Name = "labeldwon";
            // 
            // labelup
            // 
            resources.ApplyResources(this.labelup, "labelup");
            this.labelup.BackColor = System.Drawing.Color.Transparent;
            this.labelup.ContextMenuStrip = this.Menu;
            this.labelup.ForeColor = System.Drawing.Color.Black;
            this.labelup.Name = "labelup";
            // 
            // pictureBox
            // 
            this.pictureBox.BackColor = System.Drawing.Color.Transparent;
            resources.ApplyResources(this.pictureBox, "pictureBox");
            this.pictureBox.ContextMenuStrip = this.Menu;
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.TabStop = false;
            this.pictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // NetMonitor
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.Controls.Add(this.panel);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "NetMonitor";
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.TransparencyKey = System.Drawing.SystemColors.Control;
            this.Load += new System.EventHandler(this.NetMonitor_Load);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            this.Menu.ResumeLayout(false);
            this.panel.ResumeLayout(false);
            this.panel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.PictureBox pictureBox;
        private System.Windows.Forms.Label Lable_SpeedDown;
        private System.Windows.Forms.Label Lable_SpeedUP;
        private System.Windows.Forms.Panel panel;
        new private System.Windows.Forms.ContextMenuStrip Menu;
        private System.Windows.Forms.ToolStripMenuItem Exit_Menu;
        private System.Windows.Forms.ToolStripMenuItem Interface_Menu;
        private System.Windows.Forms.ToolStripComboBox ComboBox;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator;
        private System.Windows.Forms.ToolStripMenuItem AutoRun_ToolStripMenuItem;
        private System.Windows.Forms.Label labeldwon;
        private System.Windows.Forms.Label labelup;
    }
}

