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
            this.pictureBox = new System.Windows.Forms.PictureBox();
            this.panel = new System.Windows.Forms.Panel();
            this.Lable_SpeedUP = new System.Windows.Forms.Label();
            this.Lable_SpeedDown = new System.Windows.Forms.Label();
            this.Lable_TotalUP = new System.Windows.Forms.Label();
            this.Lable_TotalDown = new System.Windows.Forms.Label();
            this.Menu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.panel.SuspendLayout();
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
            this.Menu.ShowImageMargin = false;
            this.Menu.Size = new System.Drawing.Size(100, 74);
            // 
            // Interface_Menu
            // 
            this.Interface_Menu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ComboBox});
            this.Interface_Menu.MergeIndex = 0;
            this.Interface_Menu.Name = "Interface_Menu";
            this.Interface_Menu.Padding = new System.Windows.Forms.Padding(0);
            this.Interface_Menu.Size = new System.Drawing.Size(99, 20);
            this.Interface_Menu.Text = "网卡选择";
            // 
            // ComboBox
            // 
            this.ComboBox.DropDownWidth = 70;
            this.ComboBox.FlatStyle = System.Windows.Forms.FlatStyle.Standard;
            this.ComboBox.Name = "ComboBox";
            this.ComboBox.Size = new System.Drawing.Size(75, 25);
            this.ComboBox.DropDownClosed += new System.EventHandler(this.ComboBox_DropDownClosed);
            // 
            // toolStripSeparator
            // 
            this.toolStripSeparator.ForeColor = System.Drawing.Color.Black;
            this.toolStripSeparator.Name = "toolStripSeparator";
            this.toolStripSeparator.Size = new System.Drawing.Size(96, 6);
            // 
            // AutoRun_ToolStripMenuItem
            // 
            this.AutoRun_ToolStripMenuItem.Checked = true;
            this.AutoRun_ToolStripMenuItem.CheckOnClick = true;
            this.AutoRun_ToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.AutoRun_ToolStripMenuItem.Name = "AutoRun_ToolStripMenuItem";
            this.AutoRun_ToolStripMenuItem.Size = new System.Drawing.Size(99, 22);
            this.AutoRun_ToolStripMenuItem.Text = "开机启动";
            // 
            // Exit_Menu
            // 
            this.Exit_Menu.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.Exit_Menu.Checked = true;
            this.Exit_Menu.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Exit_Menu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.Exit_Menu.MergeIndex = 0;
            this.Exit_Menu.Name = "Exit_Menu";
            this.Exit_Menu.Size = new System.Drawing.Size(99, 22);
            this.Exit_Menu.Text = "退出";
            this.Exit_Menu.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Exit_Menu_MouseDown);
            // 
            // pictureBox
            // 
            this.pictureBox.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox.ContextMenuStrip = this.Menu;
            this.pictureBox.Location = new System.Drawing.Point(0, 0);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(59, 56);
            this.pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox.TabIndex = 2;
            this.pictureBox.TabStop = false;
            this.pictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // panel
            // 
            this.panel.BackColor = System.Drawing.SystemColors.Control;
            this.panel.BackgroundImage = global::NetMonitor.Properties.Resources.img_background;
            this.panel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.panel.ContextMenuStrip = this.Menu;
            this.panel.Controls.Add(this.Lable_SpeedUP);
            this.panel.Controls.Add(this.Lable_SpeedDown);
            this.panel.Controls.Add(this.Lable_TotalUP);
            this.panel.Controls.Add(this.Lable_TotalDown);
            this.panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel.Location = new System.Drawing.Point(0, 0);
            this.panel.Name = "panel";
            this.panel.Size = new System.Drawing.Size(193, 56);
            this.panel.TabIndex = 3;
            this.panel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // Lable_SpeedUP
            // 
            this.Lable_SpeedUP.AutoSize = true;
            this.Lable_SpeedUP.BackColor = System.Drawing.Color.White;
            this.Lable_SpeedUP.ContextMenuStrip = this.Menu;
            this.Lable_SpeedUP.Font = new System.Drawing.Font("楷体", 10.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Lable_SpeedUP.ForeColor = System.Drawing.Color.Black;
            this.Lable_SpeedUP.Location = new System.Drawing.Point(66, 9);
            this.Lable_SpeedUP.Name = "Lable_SpeedUP";
            this.Lable_SpeedUP.Size = new System.Drawing.Size(84, 14);
            this.Lable_SpeedUP.TabIndex = 0;
            this.Lable_SpeedUP.Text = "上传：0B/S";
            this.Lable_SpeedUP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // Lable_SpeedDown
            // 
            this.Lable_SpeedDown.AutoSize = true;
            this.Lable_SpeedDown.BackColor = System.Drawing.Color.White;
            this.Lable_SpeedDown.ContextMenuStrip = this.Menu;
            this.Lable_SpeedDown.Font = new System.Drawing.Font("楷体", 10.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Lable_SpeedDown.ForeColor = System.Drawing.Color.Black;
            this.Lable_SpeedDown.Location = new System.Drawing.Point(66, 33);
            this.Lable_SpeedDown.Name = "Lable_SpeedDown";
            this.Lable_SpeedDown.Size = new System.Drawing.Size(84, 14);
            this.Lable_SpeedDown.TabIndex = 1;
            this.Lable_SpeedDown.Text = "下载：0B/S";
            this.Lable_SpeedDown.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            // 
            // Lable_TotalUP
            // 
            this.Lable_TotalUP.AutoSize = true;
            this.Lable_TotalUP.BackColor = System.Drawing.Color.White;
            this.Lable_TotalUP.Location = new System.Drawing.Point(163, 11);
            this.Lable_TotalUP.Name = "Lable_TotalUP";
            this.Lable_TotalUP.Size = new System.Drawing.Size(11, 12);
            this.Lable_TotalUP.TabIndex = 4;
            this.Lable_TotalUP.Text = "0";
            this.Lable_TotalUP.Visible = false;
            // 
            // Lable_TotalDown
            // 
            this.Lable_TotalDown.AutoSize = true;
            this.Lable_TotalDown.BackColor = System.Drawing.Color.White;
            this.Lable_TotalDown.Location = new System.Drawing.Point(163, 33);
            this.Lable_TotalDown.Name = "Lable_TotalDown";
            this.Lable_TotalDown.Size = new System.Drawing.Size(11, 12);
            this.Lable_TotalDown.TabIndex = 5;
            this.Lable_TotalDown.Text = "0";
            this.Lable_TotalDown.Visible = false;
            // 
            // NetMonitor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.ClientSize = new System.Drawing.Size(193, 56);
            this.Controls.Add(this.pictureBox);
            this.Controls.Add(this.panel);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Location = new System.Drawing.Point(700, 10);
            this.MaximumSize = new System.Drawing.Size(193, 56);
            this.MinimumSize = new System.Drawing.Size(193, 56);
            this.Name = "NetMonitor";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "NetMonitor";
            this.TopMost = true;
            this.TransparencyKey = System.Drawing.SystemColors.Control;
            this.Load += new System.EventHandler(this.NetMonitor_Load);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NetMonitor_MouseDown);
            this.Menu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.panel.ResumeLayout(false);
            this.panel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.PictureBox pictureBox;
        private System.Windows.Forms.Label Lable_SpeedDown;
        private System.Windows.Forms.Label Lable_SpeedUP;
        private System.Windows.Forms.Panel panel;
        private System.Windows.Forms.ContextMenuStrip Menu;
        private System.Windows.Forms.ToolStripMenuItem Exit_Menu;
        private System.Windows.Forms.ToolStripMenuItem Interface_Menu;
        private System.Windows.Forms.ToolStripComboBox ComboBox;
        private System.Windows.Forms.Label Lable_TotalUP;
        private System.Windows.Forms.Label Lable_TotalDown;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator;
        private System.Windows.Forms.ToolStripMenuItem AutoRun_ToolStripMenuItem;
    }
}

