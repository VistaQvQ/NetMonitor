# NetMonitor 技术要点文档

> 记录开发过程中解决的关键技术问题，供后续维护参考。
> 最后更新：2026-04-08

---

## 1. 任务管理器"后台进程"方案

### 问题背景

Windows 任务管理器将进程分为"应用"和"后台进程"。判定条件：

- **应用**：进程拥有可见的、具有 `WS_EX_APPWINDOW` 扩展样式（或无 owner）的顶级窗口。
- **后台进程**：进程没有符合上述条件的窗口。

NetMonitor 是一个悬浮窗工具，应归为"后台进程"，同时窗体需要正常渲染显示。

### 失败方案：SetParent → HWND_MESSAGE（已验证无效）

```
// 直觉上的做法：把窗口挂到 HWND_MESSAGE 子窗口下，使其不是"顶级窗口"
IntPtr hiddenParent = CreateWindowEx(..., HWND_MESSAGE, ...);
SetParent(form.Handle, hiddenParent);
```

**为什么失败：**

`HWND_MESSAGE` 是纯消息窗口，**没有客户区（client area），不渲染任何子窗口**。
子窗口的绘制由父窗口客户区裁剪决定——父窗口没有客户区，子窗口永远不会被渲染到屏幕上。

结果：`this.Visible = true`、`shouldShow = true` 状态正常，但窗口肉眼不可见。

### 正确方案：WS_EX_TOOLWINDOW 扩展样式（Program.cs）

保持窗口为顶级窗口（不调用 SetParent），但修改扩展样式：

```csharp
// 1. 获取窗口句柄（强制触发 WinForms 的 CreateHandle）
var handle = form.Handle;

// 2. 读取当前扩展样式
int exStyle = GetWindowLong(handle, GWL_EXSTYLE);

// 3. 清除 WS_EX_APPWINDOW（此标志会强制窗口出现在任务栏）
//    添加 WS_EX_TOOLWINDOW（此标志使窗口不出现在任务栏和"应用"列表）
int newExStyle = (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW;
SetWindowLong(handle, GWL_EXSTYLE, newExStyle);

// 4. 发送 SWP_FRAMECHANGED 强制系统重新读取样式（立即生效）
SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

// 5. Show() 触发初始化链，ApplicationContext 不注册主窗口
form.Show();
Application.Run(new ApplicationContext());
```

**效果：**
- ✅ 窗口正常渲染，可以正常显示
- ✅ 任务栏没有按钮
- ✅ 任务管理器"应用"列表不出现
- ✅ 任务管理器"后台进程"列表中显示

---

## 2. ApplicationContext 空上下文启动

### 问题背景

`Application.Run(form)` 会把 `form` 注册为"主窗口"，主窗口关闭时整个应用退出，
且 WinForms 会在 `Run` 时立即显示窗口（调用内部的 `Show()`）。

### 解决方案

```csharp
// 传入空 ApplicationContext：消息循环照常运行，但不接管任何窗口的生命周期
Application.Run(new ApplicationContext());
```

窗口的显示/隐藏/关闭完全由程序自身逻辑控制（Timer + isFullScreen + Exit 菜单）。

---

## 3. OnShown 初始化链 vs Load/构造函数

### 选择 OnShown 的原因

| 阶段 | 时机 | 问题 |
|---|---|---|
| 构造函数 | 句柄未创建 | 不能操作 UI 和句柄相关资源 |
| Load 事件 | 消息循环未启动 | `this.Invoke` 会死锁 |
| **OnShown** | 窗口已显示，消息循环已运行 | ✅ 安全，句柄就绪，可以 Invoke |

### 关键陷阱：Load 中不能用 Invoke

```csharp
// ❌ 错误：Load 触发时消息循环未启动，Invoke 永久等待 → 死锁
private void NetMonitor_Load(object sender, EventArgs e)
{
    this.Invoke((EventHandler)delegate { SetGifBackground(); });
}

// ✅ 正确：Load 本身就在 UI 线程，直接调用即可
private void NetMonitor_Load(object sender, EventArgs e)
{
    SetGifBackground();
}
```

---

## 4. isFullScreen() 自身识别

### SetParent 方案下的问题（已过时，仅记录）

`GetForegroundWindow()` **只返回顶级窗口**，不会返回子窗口句柄。
SetParent 后窗口变成子窗口，`fg == this.Handle` 永远为 false，导致程序把自己判定为全屏。

修复（当时）：改用进程 ID 比较——前台窗口属于本进程则不是"其他全屏"。

### 当前方案下

窗口是顶级窗口，但有 `WS_EX_TOOLWINDOW` 样式。
`isFullScreen()` 中有专门一条规则：

```csharp
// 排除 toolwindow 等非典型应用窗口
int ex = GetWindowLong(fg, GWL_EXSTYLE);
if ((ex & WS_EX_TOOLWINDOW) != 0)
    return false;
```

自身有 `WS_EX_TOOLWINDOW`，即使成为前台窗口也会被正确过滤。进程 ID 判断作为额外保险。

---

## 5. 托盘图标（NotifyIcon）实现

### 需求

- 任务栏右下角显示与程序图标相同的小图标
- 右键唤起的菜单与窗体右键菜单为**同一个** `ContextMenuStrip`（`this.Menu`）

### 实现

在 `NetMonitor.cs` 的 `OnShown` 初始化时创建 `NotifyIcon`：

```csharp
private NotifyIcon trayIcon;

// 在 OnShown 初始化末尾：
trayIcon = new NotifyIcon(this.components)
{
    // 使用程序自身的 .ico 图标资源
    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
    Text = "NetMonitor - 网络速度监控",
    Visible = true,
    // 共用窗体右键菜单（同一个 ContextMenuStrip 实例）
    ContextMenuStrip = this.Menu
};
// 双击托盘图标：切换窗体显示/隐藏
trayIcon.DoubleClick += (s, e) => { this.Visible = !this.Visible; };
```

### 注意事项

- `NotifyIcon` 需要加入 `this.components` 以便 `Dispose` 时自动清理
- 图标使用 `Icon.ExtractAssociatedIcon(Application.ExecutablePath)` 提取可执行文件关联图标，与资源管理器显示一致
- `ContextMenuStrip` 实例共用同一个对象，菜单项状态（如 CheckBox）自动同步，无需额外逻辑
- 程序退出（`this.Close()`）时在 `FormClosing` 事件中将 `trayIcon.Visible = false` 避免"幽灵图标"

---

## 6. 全屏检测防抖机制

连续 `FullScreenConfirmThreshold`（默认 2）次检测为全屏才真正认为是全屏状态，
避免大型程序启动过渡期（splash/无标题窗口）导致悬浮窗闪烁消失。

---

## 7. 文件结构说明

```
NetMonitor/
├── Program.cs          # 入口：样式修改、启动流程
├── NetMonitor.cs       # 主窗体逻辑：网速计算、全屏检测、托盘图标
├── NetMonitor.Designer.cs  # 设计器生成：控件布局、ContextMenuStrip(Menu)定义
├── NetMonitor.resx     # 资源：字符串、图片等
├── NetMonitor.ico      # 程序图标
├── Properties/         # Settings、Resources
└── TECH-NOTES.md       # 本文件：技术要点记录
```
