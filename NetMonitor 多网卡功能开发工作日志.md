# NetMonitor 多网卡功能开发工作日志

> 日期：2026-09-30
> 主题：参照 neobox-plugins-main（speedbox 插件）为 NetMonitor 实现可选的多网卡模式，含个性化筛选、网卡变动监听、IPv6 流量统计修复。
> 涉及文件：`NetMonitor.cs`、`NetMonitor.Designer.cs`、`NetMonitor.resx`、`Properties/Settings.settings`、`Properties/Settings.Designer.cs`

---

## 0. 改动总览

| 阶段 | 主题 | 涉及文件 | 核心产出 |
|---|---|---|---|
| 一 | 多网卡模式核心实现 | `NetMonitor.cs`、`NetMonitor.Designer.cs` | `UpdateMultiMode`、采样键、菜单开关+持久化 |
| 二 | 菜单可见性 + 网卡变动监听 | `NetMonitor.resx`、`NetMonitor.cs` | `NetworkChange` 订阅、`RefreshNetworkInterfaceList` |
| 三 | 个性化筛选网卡 | `Settings.settings`、`Settings.Designer.cs`、`NetMonitor.cs` | 悬浮子菜单、`multiNicSelectedIds` 持久化 |
| 四 | 子菜单显隐联动 | `NetMonitor.cs` | `BuildMultiNicFilterMenu` 加开关判断 |
| 五 | IPv6 统计修复 + 方法重构 | `NetMonitor.cs` | `GetIPStatistics`、`GetAllNic*` 复用单网卡入口 |

---

## 1. 多网卡模式核心实现

### 1.1 修改内容

1. 字段重命名：`temp＿isMultiMode`（含全角下划线 `＿`）→ `isMultiMode`，并改为持久化状态。
2. 防突跳采样键：`interfaceSelect`（int 下标）→ `speedSampleKey`（string），新增 `BuildSpeedSampleKey()`。
3. 实现 `UpdateMultiMode()`（原本是空 TODO）。
4. 新增 `MultNicMode_ToolStripMenuItem_Click`、`UpdateInterfaceMenuState()`。
5. `ReadUserSettings()` 同步 `isMultiMode`。
6. `NetMonitor.Designer.cs` 给 `MultNicMode_ToolStripMenuItem` 绑定 `Click` 事件。

### 1.2 设计原理

- **参照 neobox speedbox 的汇总逻辑**：多网卡模式 = 累加所有参与网卡的上下行字节，再计算差值得到速率。NetMonitor 已有 `GetAllNicBytesSent/Received` 正是这个思路，本阶段补齐 `UpdateMultiMode` 与模式开关。
- **网卡筛选沿用 NetMonitor 现有实现**：`InitNetworkInterface()` 中的 `isVirtualNetworkInterface()` + `OperationalStatus.Up` 过滤比 neobox「只过滤回环/OTHER」更完善，故不采用 neobox 的筛选。
- **防突跳采样键**：速率 = 两次采样的字节差值 ÷ 时间差。当「采样源」变化（切网卡 / 切模式 / 网卡集合变化）时，前后累计字节的基数不同，直接相减会产生巨大的假速率。因此用采样键标识当前采样源，键变化时把差值清零。

### 1.3 核心逻辑

```csharp
// 采样键：单网卡 = "S:"+网卡Id；多网卡 = "M:"+参与网卡Id排序拼接
private string BuildSpeedSampleKey()
{
    if (isMultiMode)
    {
        var selected = GetSelectedNics();
        if (selected.Length == 0) return "M:empty";
        var ids = selected.Select(n => n.Id).OrderBy(x => x).ToArray();
        return "M:" + string.Join(",", ids);
    }
    if (nicArr == null || ComboBox.SelectedIndex < 0 || ComboBox.SelectedIndex >= nicArr.Length)
        return "S:empty";
    return "S:" + nicArr[ComboBox.SelectedIndex].Id;
}

// 防突跳：采样源变化或首次采样时 delta 置零
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
```

---

## 2. 菜单可见性 + 网卡变动监听

### 2.1 修改内容

1. `NetMonitor.resx`：`MultNicMode_ToolStripMenuItem.Visible` 由 `False` → `True`（功能未实现时被隐藏，导致菜单看不到多网卡选项）。
2. `NetMonitor.cs`：`OnShown` 订阅 `NetworkChange.NetworkAddressChanged`，`FormClosing` 取消订阅。
3. 新增 `OnNetworkAddressChanged`（切回 UI 线程）与 `RefreshNetworkInterfaceList`。

### 2.2 设计原理

- **多网卡禁用单网卡选择**：`UpdateInterfaceMenuState()` 里 `Interface_Menu.Enabled = !isMultiMode`，勾选多网卡后「网卡选择」整项灰掉。
- **网卡变动监听**：`NetworkAddressChanged` 事件在网卡地址变化（含插拔/启停）时触发，但也可能在仅 IP 变化（DHCP 续租）时触发。为区分，用 `isNetworkInterfaceListChanged()` 判断网卡「集合」是否真正变化，变了才重新初始化——避免频繁重置采样导致速率归零。

### 2.3 核心逻辑

```csharp
private void OnNetworkAddressChanged(object sender, EventArgs e)
{
    if (this.IsHandleCreated && !this.IsDisposed)
        try { this.BeginInvoke((Action)RefreshNetworkInterfaceList); }
        catch (InvalidOperationException) { }
}

private void RefreshNetworkInterfaceList()
{
    if (!isNetworkInterfaceListChanged()) return;   // 仅 IP 变化则跳过

    string selectedId = nicArr[ComboBox.SelectedIndex].Id;  // 记录选中
    InitNetworkInterface();                                  // 内部 speedCalcReady=false
    for (int i = 0; i < nicArr.Length; i++)                  // 按 Id 恢复选中
        if (nicArr[i].Id == selectedId) { ComboBox.SelectedIndex = i; break; }
}
```

---

## 3. 个性化筛选网卡（多网卡候选列表）

### 3.1 修改内容

1. `Settings.settings` + `Settings.Designer.cs`：新增 `MultiNicSelectedIds`（string，逗号分隔勾选网卡 Id；空串 = 未自定义 = 全选）。
2. `NetMonitor.cs`：新增字段 `multiNicSelectedIds`（`HashSet<string>`，null = 全选）。
3. 新增 `BuildMultiNicFilterMenu()`、`MultiNicFilterItem_CheckedChanged()`、`GetSelectedNics()`。
4. `UpdateMultiMode()` 改用 `GetSelectedNics()`；`BuildSpeedSampleKey()` 多网卡分支改用筛选集合。
5. `ReadUserSettings()`/`WriteUserSettings()` 读写 `MultiNicSelectedIds`。
6. `InitNetworkInterface()` 末尾调用 `BuildMultiNicFilterMenu()`，网卡变动时子菜单自动重建。

### 3.2 设计原理

- **交互**：给「多网卡模式」菜单项动态填充 `DropDownItems`（每个网卡一个 `CheckOnClick` 勾选项，`Tag` 存网卡 Id）。WinForms 中带子菜单的项，鼠标**悬停自动展开**子菜单，符合「悬浮显示子菜单」需求。
- **勾选即持久化**：`CheckOnClick` 项点击后菜单保持打开，可连续勾选多个网卡；每次勾选/取消立即 `WriteUserSettings()` 落盘，重启后恢复。
- **默认全选语义**：`multiNicSelectedIds == null` 表示从未自定义，等价于全选（沿用旧「累加全部」行为）；用户一旦手动勾选/取消，才转为具体 `HashSet`（从全选基线增删）。
- **筛选变化联动采样**：`BuildSpeedSampleKey()` 多网卡分支基于筛选集合，勾选变化 → 采样键变化 → 自动重置防突跳。

### 3.3 核心逻辑

```csharp
private void BuildMultiNicFilterMenu()
{
    MultNicMode_ToolStripMenuItem.DropDownItems.Clear();
    if (!isMultiMode) return;                       // 未开启不显示子菜单
    if (nicArr == null || nicArr.Length == 0) return;

    foreach (var nic in nicArr)
    {
        bool isChecked = multiNicSelectedIds == null || multiNicSelectedIds.Contains(nic.Id);
        var item = new ToolStripMenuItem {
            Text = nic.Name, CheckOnClick = true, Checked = isChecked, Tag = nic.Id
        };
        item.CheckedChanged += MultiNicFilterItem_CheckedChanged;
        MultNicMode_ToolStripMenuItem.DropDownItems.Add(item);
    }
}

private NetworkInterface[] GetSelectedNics()
{
    if (nicArr == null || nicArr.Length == 0) return new NetworkInterface[0];
    if (multiNicSelectedIds == null) return nicArr;  // 未自定义 → 全选
    return nicArr.Where(n => multiNicSelectedIds.Contains(n.Id)).ToArray();
}
```

---

## 4. 子菜单显隐联动

### 4.1 修改内容

- `BuildMultiNicFilterMenu()` 开头加 `if (!isMultiMode) return;`（清空子菜单后判断）。
- `MultNicMode_ToolStripMenuItem_Click()` 切换开关后调用 `BuildMultiNicFilterMenu()`。

### 4.2 设计原理

多网卡模式未勾选时，悬停「多网卡模式」不应显示筛选子菜单。通过清空 `DropDownItems` 实现——无子菜单项则悬停无下拉。启动流程 `InitNetworkInterface`（isMultiMode 默认 false → 清空）→ `ReadUserSettings`（按持久化勾选状态重建）依然正确。

---

## 5. IPv6 流量统计修复 + 汇总方法重构

### 5.1 修改内容

1. `GetSingleNicBytesSent/Received`：`GetIPv4Statistics()` → `GetIPStatistics()`。
2. `GetAllNicBytesSent/Received`：改为复用 `GetSingleNic*`，删除重复的 try/catch 与直接读取。

### 5.2 设计原理

- **问题**：`GetIPv4Statistics().BytesSent/Received` 仅统计 IPv4 流量，IPv6 连接产生的数据不计入，导致速率被系统性低估（Windows 默认启用 IPv6）。
- **方案**：`GetIPStatistics()` 返回 `IPInterfaceStatistics`，其 `BytesSent/BytesReceived` 为 **IPv4+IPv6 总量**，.NET Framework 4.5+ 均支持，无需 P/Invoke。
- **重构意义**：统计来源收敛到 `GetSingleNic*` 单网卡入口，后续若换统计口径（如 P/Invoke `GetIfEntry2`）只需改一处，`GetAllNic*` 自动跟随，避免单网卡/多网卡口径不一致。

### 5.3 核心逻辑

```csharp
private long GetAllNicBytesSent(NetworkInterface[] nics)
{
    long total = 0;
    foreach (var nic in nics)
        total += GetSingleNicBytesSent(nic);
    return total;
}

private long GetSingleNicBytesSent(NetworkInterface nic)
{
    try   { return nic.GetIPStatistics().BytesSent; }   // IPv4+IPv6 总量
    catch { return 0; }
}
```

> 补充：neobox 参考项目用 P/Invoke `GetIfTable`（`MIB_IFROW.dwInOctets/dwOutOctets`）拿接口总流量，也是为覆盖 IPv6，但它是 32 位计数器有溢出风险；`GetIPStatistics()` 是 64 位 `long`，托管实现更优。

---


## 6. 遗留 / 后续可优化项

- `autoSwitchNicEnabled` 字段仍未绑定菜单项（目前硬编码 `true`）。
- `multiNicSelectedIds` 中拔除网卡的陈旧 Id 未清理（无害，仅设置文件里残留几个 GUID）。
- 完整构建需在装有 VS/MSBuild 的环境验证资源打包与运行时行为。
