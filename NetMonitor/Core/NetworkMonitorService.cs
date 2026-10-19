using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace NetMonitor.Core
{
    /// <summary>
    /// 测速核心所需的 UI 视图抽象。由窗体实现，服务通过它读写网卡选择与速率文本，
    /// 从而与 WinForms 控件解耦。
    /// </summary>
    internal interface INetworkMonitorView
    {
        /// <summary>当前选中网卡下标（ComboBox.SelectedIndex）。</summary>
        int SelectedNicIndex { get; set; }

        /// <summary>清空并填充网卡下拉列表；<paramref name="names"/> 为空时显示 <paramref name="emptyText"/>。</summary>
        void SetNicItems(string[] names, string emptyText);

        /// <summary>写入上传/下载速率文本。</summary>
        void ShowSpeed(string upText, string downText);
    }

    /// <summary>
    /// 网卡枚举 + 速率计算 + 自动切卡 + 档位判定。
    /// 纯逻辑服务：不依赖 WinForms 控件，通过 <see cref="INetworkMonitorView"/> 与窗体交互。
    /// </summary>
    internal sealed class NetworkMonitorService
    {
        private readonly INetworkMonitorView view;

        // 当前可用网卡列表
        private NetworkInterface[] nicArr;

        // 采样状态
        private string speedSampleKey = null;       // 当前采样键（单网卡=选中网卡Id，多网卡=网卡集合Id），防突跳用
        private int    zeroSpeedCount   = 0;        // 连续零速次数（自动切卡阈值）
        private bool   speedCalcReady   = false;    // 首次采样初始化标志
        private long   prevBytesSent    = 0;
        private long   prevBytesRecv    = 0;
        private DateTime prevSampleTime = DateTime.MinValue;

        // 平滑后的「上传+下载」总速率，用于档位判定（EMA 削弱每秒瞬时波动）
        private double smoothedTotalRate = 0;

        // GIF 速度档位：1/2/3（帧率由 GifAnimator 据档位换算）
        private int gifStep = 1;

        // 无网自动切卡开关。下阶段绑定到菜单项，目前默认启用。
        private bool autoSwitchNicEnabled = true;

        /// <summary>网卡列表变化（插拔/启停）通知，供窗体重建筛选菜单。</summary>
        public event EventHandler NicListChanged;

        public NetworkMonitorService(INetworkMonitorView view)
        {
            this.view = view;
        }

        /// <summary>当前可用网卡列表（窗体读取以重建多网卡筛选菜单）。</summary>
        public NetworkInterface[] AvailableNics { get { return nicArr; } }

        /// <summary>多网卡模式开关：true = 累加所有网卡，false = 单网卡模式。</summary>
        public bool IsMultiMode { get; set; }

        /// <summary>多网卡模式下参与累加的网卡 Id 集合；null 表示未自定义（默认全选）。</summary>
        public HashSet<string> MultiNicSelectedIds { get; set; }

        /// <summary>当前 GIF 速度档位（由速率判定，供窗体同步给 GifAnimator）。</summary>
        public int GifStep { get { return gifStep; } }

        /// <summary>采样源变化时调用：重置采样基数，避免速率突跳。</summary>
        public void ResetSampling() { speedCalcReady = false; }

        public void InitNetworkInterface()
        {
            speedCalcReady = false;
            try
            {
                NetworkInterface[] allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                nicArr = allInterfaces.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up && //这里的OperationalStatus.Up 可能在多网卡模式下导致某些网卡无法显示，后续可以考虑放宽条件或提供更多选项
                    !IsVirtualNetworkInterface(nic)).ToArray();

                if (nicArr.Length > 0)
                {
                    view.SetNicItems(nicArr.Select(nic => nic.Name).ToArray(), null);
                    view.SelectedNicIndex = 0;
                }
                else
                {
                    view.SetNicItems(new string[0], "无可用网络接口");
                }

                Debug.WriteLine("可用网卡数量: " + nicArr.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("InitNetworkInterface 错误: " + ex.Message);
                nicArr = new NetworkInterface[0];
                view.SetNicItems(new string[0], "获取网卡失败");
            }

            OnNicListChanged();
        }

        /// <summary>网卡列表变化时重新初始化，并尽量恢复原选中网卡（按 Id 匹配）。</summary>
        public void RefreshNetworkInterfaceList()
        {
            // 列表未实际变化（如仅 IP 地址变化）则不处理，避免频繁重置采样导致速率归零
            if (!IsNetworkInterfaceListChanged())
                return;

            // 保存当前选中网卡 Id，供重新初始化后恢复
            string selectedId = null;
            if (nicArr != null && view.SelectedNicIndex >= 0 && view.SelectedNicIndex < nicArr.Length)
                selectedId = nicArr[view.SelectedNicIndex].Id;

            InitNetworkInterface();  // 内部会 speedCalcReady = false，重置采样避免速率突跳

            // 恢复原选中网卡；若已不存在则保持默认（第 0 项）
            if (selectedId != null && nicArr != null)
            {
                for (int i = 0; i < nicArr.Length; i++)
                {
                    if (nicArr[i].Id == selectedId)
                    {
                        view.SelectedNicIndex = i;
                        break;
                    }
                }
            }
        }

        /// <summary>按当前模式分发测速。须在 UI 线程调用。</summary>
        public void Tick()
        {
            if (IsMultiMode)
                UpdateMultiMode();
            else
                UpdateSingleMode();
        }

        private void UpdateSingleMode()
        {
            // 网卡为空数据判定 + 网卡刷新（SingleMode）
            // nicArr 为空说明网络接口尚未加载或全部失效，重置速度并重新初始化
            if (nicArr == null || nicArr.Length == 0)
            {
                ResetSpeedData();
                InitNetworkInterface();
                return;
            }

            // 防越界判定（SingleMode）
            // 下标越界时跳过本次更新，避免数组越界异常
            if (view.SelectedNicIndex < 0 || view.SelectedNicIndex >= nicArr.Length)
                return;

            // 取当前选中网卡的原始字节数
            NetworkInterface nic = nicArr[view.SelectedNicIndex];
            long bytesSent = GetSingleNicBytesSent(nic);
            long bytesRecv = GetSingleNicBytesReceived(nic);

            // 数据初始化/时间计算 + 防数据突跳 + 差值
            CalcSpeedAndGifStep(
                bytesSent, bytesRecv,
                out long netSendPerSec, out long netRecvPerSec);

            // UI 更新（公用逻辑）
            UpdateSpeedToUI(netSendPerSec, netRecvPerSec);

            // 无网时网卡自动切换
            AutoSwitchNicOnZeroSpeed_SingleMode(netSendPerSec, netRecvPerSec);
        }

        private void UpdateMultiMode()
        {
            // 多网卡模式：累加用户筛选出的网卡流量（参照 neobox speedbox 的汇总逻辑）
            if (nicArr == null || nicArr.Length == 0)
            {
                ResetSpeedData();
                InitNetworkInterface();
                return;
            }

            NetworkInterface[] selectedNics = GetSelectedNics();
            long bytesSent = GetAllNicBytesSent(selectedNics);
            long bytesRecv = GetAllNicBytesReceived(selectedNics);

            CalcSpeedAndGifStep(bytesSent, bytesRecv, out long netSendPerSec, out long netRecvPerSec);
            UpdateSpeedToUI(netSendPerSec, netRecvPerSec);
        }

        /// <summary>多网卡模式下参与累加的网卡集合；未自定义（null）时返回全部网卡。</summary>
        private NetworkInterface[] GetSelectedNics()
        {
            if (nicArr == null || nicArr.Length == 0)
                return new NetworkInterface[0];

            if (MultiNicSelectedIds == null)
                return nicArr;  // 未自定义 → 全选

            return nicArr.Where(nic => MultiNicSelectedIds.Contains(nic.Id)).ToArray();
        }

        /// <summary>
        /// 时间间隔计算 → 防突跳 → 差值转速率 → gifStep 调整。
        /// 输出 <paramref name="netSendPerSec"/> / <paramref name="netRecvPerSec"/>（bytes/s）。
        /// </summary>
        private void CalcSpeedAndGifStep(long bytesSent, long bytesRecv, out long netSendPerSec, out long netRecvPerSec)
        {
            // 时间间隔（首次或回退时取 1s）
            DateTime now = DateTime.UtcNow;
            double deltaSeconds = 1.0;
            if (prevSampleTime != DateTime.MinValue)
            {
                deltaSeconds = (now - prevSampleTime).TotalSeconds;
                if (deltaSeconds <= 0) deltaSeconds = 1.0;
            }
            prevSampleTime = now;

            // 防突跳：采样源（单网卡/多网卡集合）变化或首次采样时，将 delta 置零
            long deltaSent, deltaRecv;
            string sampleKey = BuildSpeedSampleKey();
            if (speedCalcReady && sampleKey == speedSampleKey)
            {
                deltaSent = bytesSent - prevBytesSent;
                deltaRecv = bytesRecv - prevBytesRecv;
            }
            else
            {
                speedCalcReady = true;
                speedSampleKey = sampleKey;
                deltaSent = 0;
                deltaRecv = 0;
            }
            prevBytesSent = bytesSent;
            prevBytesRecv = bytesRecv;

            // bytes/s
            netSendPerSec = (long)(deltaSent / deltaSeconds);
            netRecvPerSec = (long)(deltaRecv / deltaSeconds);

            // gifStep：按「上传+下载」总速率分三档（决定动画帧率）
            // 用 EMA 平滑 + 滞回判定，避免瞬时速率在阈值附近波动导致档位频繁跳变
            const long OneMB = 1024 * 1024;
            try
            {
                long totalRate = netSendPerSec + netRecvPerSec;   // 上传 + 下载总速率

                // EMA 平滑（α=0.4）：削弱每秒瞬时波动，让档位判定更稳定
                if (smoothedTotalRate <= 0)
                    smoothedTotalRate = totalRate;
                else
                    smoothedTotalRate = smoothedTotalRate * 0.6 + totalRate * 0.4;

                double rate = smoothedTotalRate;

                // 滞回判定：升档/降档用不同阈值，中间留滞回带，避免临界点抖动
                int newStep = gifStep;   // 默认保持当前档位
                switch (gifStep)
                {
                    case 1:   // 档1 → 档2：需 > 1.2 MB/s
                        if (rate >= OneMB * 1.2) newStep = 2;
                        break;
                    case 2:   // 档2 → 档3：需 > 12 MB/s；档2 → 档1：需 < 0.8 MB/s
                        if      (rate >= OneMB * 12)  newStep = 3;
                        else if (rate <  OneMB * 0.8) newStep = 1;
                        break;
                    case 3:   // 档3 → 档2：需 < 8 MB/s
                        if (rate < OneMB * 8) newStep = 2;
                        break;
                }

                // 档位变化时才更新（帧间隔由 GifAnimator 据 Step 换算）
                if (newStep != gifStep)
                {
                    gifStep = newStep;
                }
            }
            catch { /* 保留当前 gifStep */ }
        }

        /// <summary>
        /// 将计算好的速率写入速度标签 UI。
        /// Single/MultiMode 均可调用。
        /// </summary>
        private void UpdateSpeedToUI(long netSendPerSec, long netRecvPerSec)
        {
            view.ShowSpeed(FormatSpeed(netSendPerSec), FormatSpeed(netRecvPerSec));
        }

        /// <summary>将速度数据归零（网卡无效或刷新时调用）。</summary>
        private void ResetSpeedData()
        {
            prevBytesSent = 0;
            prevBytesRecv = 0;
            view.ShowSpeed("  0B/S", "  0B/S");
        }

        /// <summary>【SingleMode】连续零速时轮询切换网卡；受 autoSwitchNicEnabled 开关控制。</summary>
        private void AutoSwitchNicOnZeroSpeed_SingleMode(long netSendPerSec, long netRecvPerSec)
        {
            if (!autoSwitchNicEnabled)
            {
                zeroSpeedCount = 0;
                return;
            }

            if (netRecvPerSec == 0 && netSendPerSec == 0)
            {
                zeroSpeedCount++;
                if (zeroSpeedCount >= 3)
                {
                    if (zeroSpeedCount < nicArr.Length * 3)
                    {
                        // 未轮询完一圈，切下一块
                        view.SelectedNicIndex = (view.SelectedNicIndex + 1) % nicArr.Length;
                        zeroSpeedCount++;
                    }
                    else if (IsNetworkInterfaceListChanged())
                    {
                        // 网卡列表变化（插拔等），重新初始化
                        zeroSpeedCount = 0;
                        InitNetworkInterface();
                        view.SelectedNicIndex = 0;// 重置到第一块，等待下一轮速度更新后再判断是否继续切卡
                    }
                    else
                    {
                        // 轮询一圈仍为零，钳位防溢出
                        view.SelectedNicIndex = (view.SelectedNicIndex + 1) % nicArr.Length;
                        zeroSpeedCount = nicArr.Length * 3;
                    }
                }
            }
            else
            {
                zeroSpeedCount = 0;
            }
        }

        private string FormatSpeed(long bytes)
        {
            if (bytes <= 0)
                return "  0B/S";
            else if (bytes < 1000)
            {
                return $"{bytes,3}B/S";
            }
            else if (bytes < 1024)
            {
                return "0.9K/S";
            }
            else if (bytes < 1024 * 1000)
            {
                return $"{(bytes / 1024.0),3:F0}K/S";
            }
            else if (bytes < 1024 * 1024)
            {
                return "0.9M/S";
            }
            else
            {
                return $"{(bytes / (1024.0 * 1024.0)),3:F0}M/S";
            }
        }

        private bool IsVirtualNetworkInterface(NetworkInterface nic)
        {
            if (nic == null) return true;

            // 先排除明显的类型
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Unknown)
            {
                return true;
            }

            string desc = (nic.Description ?? string.Empty).ToLowerInvariant();
            string name = (nic.Name ?? string.Empty).ToLowerInvariant();
            string id = (nic.Id ?? string.Empty).ToLowerInvariant();

            // 常见虚拟网卡关键字列表（可按需扩展）
            string[] virtualKeywords = new[]
            {
                "virtual", "vmware", "hyper-v", "hyperv", "virtualbox", "vbox",
                "tap", "tun", "hamachi", "teredo", "miniport", "pseudo", "loopback",
                "vpn", "wireguard", "gpd", "vmnet", "ndiswan", "azure", "tunnel", "openvpn"
            };

            foreach (string keyword in virtualKeywords)
            {
                if (desc.Contains(keyword) || name.Contains(keyword) || id.Contains(keyword))
                {
                    return true;
                }
            }

            try
            {
                byte[] macBytes = nic.GetPhysicalAddress()?.GetAddressBytes();
                if (macBytes == null || macBytes.Length == 0) return true;
                bool allZero = true;
                foreach (byte macByte in macBytes)
                {
                    if (macByte != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (allZero) return true;
            }
            catch
            {
                // 读取物理地址时出错，保守地认为可能是虚拟网卡
                return true;
            }

            return false;
        }

        private bool CompareNetworkLists(NetworkInterface[] list1, NetworkInterface[] list2)
        {
            if (list1.Length != list2.Length)
            {
                return false;
            }
            for (int i = 0; i < list1.Length; i++)
            {
                if (list1[i].Id != list2[i].Id)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsNetworkInterfaceListChanged()
        {
            try
            {
                NetworkInterface[] currentNics = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface[] filteredCurrentNics = currentNics.Where(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    !IsVirtualNetworkInterface(nic)).ToArray();
                if (!CompareNetworkLists(filteredCurrentNics, nicArr))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略异常，假设没有变化
            }
            return false;
        }

        /// <summary>构建当前采样键：单网卡 = 选中网卡 Id，多网卡 = 筛选网卡集合 Id。</summary>
        private string BuildSpeedSampleKey()
        {
            if (IsMultiMode)
            {
                NetworkInterface[] selectedNics = GetSelectedNics();
                if (selectedNics.Length == 0)
                    return "M:empty";
                string[] ids = selectedNics.Select(nic => nic.Id).OrderBy(id => id).ToArray();
                return "M:" + string.Join(",", ids);
            }

            if (nicArr == null || view.SelectedNicIndex < 0 || view.SelectedNicIndex >= nicArr.Length)
                return "S:empty";
            return "S:" + nicArr[view.SelectedNicIndex].Id;
        }

        private long GetAllNicBytesSent(NetworkInterface[] nics)
        {
            long total = 0;
            foreach (NetworkInterface nic in nics)
                total += GetSingleNicBytesSent(nic);
            return total;
        }

        private long GetAllNicBytesReceived(NetworkInterface[] nics)
        {
            long total = 0;
            foreach (NetworkInterface nic in nics)
                total += GetSingleNicBytesReceived(nic);
            return total;
        }

        private long GetSingleNicBytesSent(NetworkInterface nic)
        {
            try   { return nic.GetIPStatistics().BytesSent; }
            catch { return 0; }
        }

        private long GetSingleNicBytesReceived(NetworkInterface nic)
        {
            try   { return nic.GetIPStatistics().BytesReceived; }
            catch { return 0; }
        }

        private void OnNicListChanged()
        {
            NicListChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
