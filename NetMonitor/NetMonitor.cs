using NetMonitor.Core;
using NetMonitor.Properties;
using NetMonitor.Rendering;
using NetMonitor.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Timers;
using System.Windows.Forms;

namespace NetMonitor
{
    public partial class NetMonitor : Form, INetworkMonitorView
    {
        // 系统定时器（1s 心跳，驱动测速）
        private System.Timers.Timer updateTimer;

        // 窗体状态
        private bool formInitialized = false;   // 幂等保护：OnShown 只初始化一次

        // 各职责服务（详见 Core / Rendering / Services 目录）
        private NetworkMonitorService monitorService;
        private GifAnimator gifAnimator;
        private LayeredWindowRenderer renderer;
        private TrayIconService trayIconService;

        public NetMonitor()
        {
            InitializeComponent();

            monitorService = new NetworkMonitorService(this);
            monitorService.NicListChanged += (s, e) => BuildMultiNicFilterMenu();

            gifAnimator = new GifAnimator();
            gifAnimator.FrameAdvanced += OnFrameAdvanced;

            renderer = new LayeredWindowRenderer();
        }

        /// <summary>分层窗口：启用 WS_EX_LAYERED 以支持 UpdateLayeredWindow 逐像素 alpha 透明。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeApi.WS_EX_LAYERED;
                return cp;
            }
        }

        // OnShown 说明：
        //   Load/构造阶段句柄未就绪，且 Application.Run 尚未启动，
        //   耗时操作（注册表/网卡枚举）放到 OnShown 避免阻塞首屏渲染。
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (formInitialized) return;

            monitorService.InitNetworkInterface();
            InitializeTimer();
            ReadUserSettings();
#if !DEBUG
            InitAutoRunMenuItem();
#endif
            formInitialized = true;

            // 屏蔽子控件渲染：分层窗口下不渲染子控件，它们仅作数据源（Label.Text/Font/Location）
            containerPanel.Visible = false;

            // 订阅网卡变动事件：插拔/启停网卡时自动刷新网卡列表
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // 托盘图标：components 托管生命周期；contextMenu 复用同一实例保持菜单状态同步
            trayIconService = new TrayIconService(this.components, this.contextMenu,
                Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                "NetMonitor - 网络速度监控");
            trayIconService.ToggleRequested += () => { this.Visible = !this.Visible; };

            // 首次渲染：呈现初始画面（GIF 首帧 + 胶囊 + 文字）
            RenderFrame();
        }

        private void InitializeTimer()
        {
            updateTimer = new System.Timers.Timer { Interval = 1000 };
            updateTimer.Elapsed += Timer_Elapsed;
            updateTimer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // 非阻塞地将更新请求排入 UI 消息队列
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.BeginInvoke((Action)UpdateNetworkInterface); }
                catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
            }
        }

        public void UpdateNetworkInterface()
        {
            // 确保在 UI 线程执行；BeginInvoke 避免阻塞计时器线程
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateNetworkInterface));
                return;
            }

            // 全屏判定 + 窗体可见性
            UpdateVisibility();

            // 测速（按单/多网卡模式分发）+ 同步档位给动画器
            monitorService.Tick();
            gifAnimator.Step = monitorService.GifStep;
        }

        private void UpdateVisibility()
        {
            bool fullScreen = FullScreenDetector.IsFullScreen();
            bool shouldShow = !fullScreen || this.showInFullScreenMenuItem.Checked;
            this.Visible = shouldShow;
        }

        /// <summary>
        /// 每帧合成：GIF 当前帧 + 玻璃胶囊 + 文字 → 32bpp ARGB 位图 → UpdateLayeredWindow 呈现。
        /// 由 GifAnimator 帧推进信号驱动。
        /// </summary>
        private void RenderFrame()
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;

            renderer.Render(new LayeredWindowRenderer.RenderContext
            {
                Handle       = this.Handle,
                FormLocation = this.Location,
                ClientSize   = this.ClientSize,
                GifArea      = new Rectangle(gifPictureBox.Location, gifPictureBox.Size),
                CurrentFrame = gifAnimator.CurrentFrame,
                Labels = new[]
                {
                    ToLabelInfo(uploadLabel),
                    ToLabelInfo(uploadSpeedLabel),
                    ToLabelInfo(downloadLabel),
                    ToLabelInfo(downloadSpeedLabel)
                }
            });
        }

        private static LayeredWindowRenderer.LabelInfo ToLabelInfo(Label lbl)
        {
            return new LayeredWindowRenderer.LabelInfo
            {
                Text      = lbl.Text,
                Font      = lbl.Font,
                ForeColor = lbl.ForeColor,
                Location  = lbl.Location
            };
        }

        private void SetGifBackground()
        {
            gifAnimator.Load(Properties.Resources.Enjoywork);
            gifAnimator.Start();
        }

        /// <summary>GIF 帧推进信号回调（线程池线程）：切回 UI 线程推进帧并渲染。</summary>
        private void OnFrameAdvanced()
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        gifAnimator.Advance();
                        RenderFrame();
                    }));
                }
                catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
            }
        }

        // Load 在消息循环启动前触发，不能 Invoke，直接调用 SetGifBackground 即可。
        // 定时器与开机自启初始化移至 OnShown。
        private void NetMonitor_Load(object sender, EventArgs e)
        {
            SetGifBackground();
        }

        private void NetMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // 右键弹出菜单（原由子控件 ContextMenuStrip 自动触发，现改手动）
                contextMenu.Show(this, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // 无边框窗体拖动：释放鼠标捕获后发送系统移动命令
                NativeApi.ReleaseCapture();
                NativeApi.SendMessage(this.Handle,
                    NativeApi.WM_SYSCOMMAND,
                    NativeApi.SC_MOVE + NativeApi.HTCAPTION, 0);
                WriteUserSettings();
            }
        }

        private void exitMenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                contextMenu.Hide();//隐藏一些东西
                if (MessageBox.Show("你确定关闭流量悬浮窗么？", "提示", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    this.Close();
                }

            }
        }

        private void interfaceComboBox_DropDownClosed(object sender, EventArgs e)
        {
            contextMenu.Hide();
        }

        private void autoRunMenuItem_Click(object sender, EventArgs e)
        {
            if (AutoStartManager.IsNameChanged())
            {
                MessageBox.Show("程序名称已被修改,请恢复原名称后再试！。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!AutoStartManager.SetAutoRun(this.autoRunMenuItem.Checked, out string error))
            {
                MessageBox.Show("设置开机自启失败: " + error);
            }
        }

        private void InitAutoRunMenuItem()
        {
            bool enabled = AutoStartManager.GetAutoRun() && !AutoStartManager.IsPathChanged();
            this.autoRunMenuItem.Checked = enabled;
            if (!enabled)
            {
                AutoStartManager.SetAutoRun(false, out _);
            }
        }

        /// <summary>网卡变动事件回调：切回 UI 线程后刷新网卡列表。</summary>
        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.BeginInvoke((Action)monitorService.RefreshNetworkInterfaceList); }
                catch (InvalidOperationException) { /* 句柄已销毁，忽略 */ }
            }
        }

        /// <summary>从持久化设置读取并应用到窗体；读取失败时回退到默认值。</summary>
        private void ReadUserSettings()
        {
            AppSettings settings = AppSettingsService.Load();

            if (settings.WindowLocation != default(Point))
                this.Location = settings.WindowLocation;

            this.showInFullScreenMenuItem.Checked = settings.ShowInFullScreen;
            this.multiNicModeMenuItem.Checked      = settings.MultiNicMode;

            monitorService.MultiNicSelectedIds = settings.MultiNicSelectedIds;
            monitorService.IsMultiMode         = settings.MultiNicMode;

            // 恢复多网卡模式开关状态，并据此调整网卡选择菜单可用性
            UpdateInterfaceMenuState();

            // 按持久化的勾选状态刷新多网卡筛选子菜单
            BuildMultiNicFilterMenu();
        }

        /// <summary>切换多网卡/单网卡模式。切换后立即重置采样并持久化设置。</summary>
        private void multiNicModeMenuItem_Click(object sender, EventArgs e)
        {
            monitorService.IsMultiMode = this.multiNicModeMenuItem.Checked;
            // 模式切换后采样基数不同，重置采样避免速率突跳
            monitorService.ResetSampling();
            UpdateInterfaceMenuState();
            // 开关变化 → 重建/清空筛选子菜单（未开启时不显示）
            BuildMultiNicFilterMenu();
            WriteUserSettings();
        }

        /// <summary>多网卡模式下禁用网卡选择菜单（累加筛选网卡，无需手动选择）。</summary>
        private void UpdateInterfaceMenuState()
        {
            this.interfaceMenuItem.Enabled = !monitorService.IsMultiMode;
        }

        /// <summary>重建「多网卡模式」的筛选子菜单：每块网卡一个勾选项，悬停展开。仅多网卡模式开启时显示。</summary>
        private void BuildMultiNicFilterMenu()
        {
            multiNicModeMenuItem.DropDownItems.Clear();

            // 多网卡模式未开启时不显示筛选子菜单
            if (!monitorService.IsMultiMode)
                return;

            NetworkInterface[] nicArr = monitorService.AvailableNics;
            if (nicArr == null || nicArr.Length == 0)
                return;

            foreach (NetworkInterface nic in nicArr)
            {
                // null（未自定义）视为全选
                bool isChecked = monitorService.MultiNicSelectedIds == null || monitorService.MultiNicSelectedIds.Contains(nic.Id);
                ToolStripMenuItem filterItem = new ToolStripMenuItem
                {
                    Text = nic.Name,
                    CheckOnClick = true,
                    Checked = isChecked,
                    Tag = nic.Id
                };
                filterItem.CheckedChanged += MultiNicFilterItem_CheckedChanged;
                multiNicModeMenuItem.DropDownItems.Add(filterItem);
            }
        }

        /// <summary>筛选子菜单勾选变化：更新选中集合并立即持久化。</summary>
        private void MultiNicFilterItem_CheckedChanged(object sender, EventArgs e)
        {
            ToolStripMenuItem filterItem = sender as ToolStripMenuItem;
            string nicId = filterItem?.Tag as string;
            if (nicId == null) return;

            HashSet<string> selectedIds = monitorService.MultiNicSelectedIds;
            if (selectedIds == null)
            {
                // 首次操作：从「全选」基线转为具体集合
                selectedIds = new HashSet<string>();
                NetworkInterface[] nicArr = monitorService.AvailableNics;
                if (nicArr != null)
                    foreach (NetworkInterface nic in nicArr) selectedIds.Add(nic.Id);
                monitorService.MultiNicSelectedIds = selectedIds;
            }

            if (filterItem.Checked)
                selectedIds.Add(nicId);
            else
                selectedIds.Remove(nicId);

            // 筛选集合变化 → 重置采样避免速率突跳
            monitorService.ResetSampling();
            WriteUserSettings();
        }

        private void WriteUserSettings()
        {
            AppSettingsService.Save(new AppSettings
            {
                WindowLocation      = this.Location,
                ShowInFullScreen    = this.showInFullScreenMenuItem.Checked,
                MultiNicMode        = this.multiNicModeMenuItem.Checked,
                MultiNicSelectedIds = monitorService.MultiNicSelectedIds
            });
        }

        private void NetMonitor_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteUserSettings();

            // 取消网卡变动订阅，防止退出后残留引用
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            // 停止 GIF 动画并还原系统定时器分辨率、释放帧
            gifAnimator.Dispose();

            // 退出前显式隐藏托盘图标，防止幽灵图标残留
            if (trayIconService != null)
                trayIconService.Hide();

            // Program.cs 使用空 ApplicationContext，关闭窗体不会自动停止消息循环，
            // 需手动调用 Application.Exit() 终止进程。
            Application.Exit();
        }

        #region INetworkMonitorView 实现

        int INetworkMonitorView.SelectedNicIndex
        {
            get { return interfaceComboBox.SelectedIndex; }
            set { interfaceComboBox.SelectedIndex = value; }
        }

        void INetworkMonitorView.SetNicItems(string[] names, string emptyText)
        {
            interfaceComboBox.Text = "";
            interfaceComboBox.Items.Clear();
            if (names != null)
                foreach (string name in names) interfaceComboBox.Items.Add(name);
            if (names == null || names.Length == 0)
                interfaceComboBox.Text = emptyText ?? "";
        }

        void INetworkMonitorView.ShowSpeed(string upText, string downText)
        {
            uploadSpeedLabel.Text   = upText;
            downloadSpeedLabel.Text = downText;
        }

        #endregion
    }
}
