# LCU 英雄选择伴随窗口吸附实现

本文说明 Prometheus 在英雄选择阶段将伴随窗口吸附到英雄联盟客户端窗口边缘的实现，重点包括 LCU 窗口跟踪、位置计算、DPI 处理、Z 序、显隐生命周期和吸附边框样式。

本文是对当前代码的实现说明。功能行为和验收标准以 [`specs/lcu-champion-select-companion.md`](../specs/lcu-champion-select-companion.md) 为准。

## 实现概览

伴随窗口是独立的 WPF 窗口。它不向英雄联盟客户端注入代码，也不修改 LCU 窗口内容。当前实现通过每 250 毫秒轮询一次 `LeagueClientUx` 主窗口状态，在检测到位置、尺寸、显示器、DPI、可见性、最小化或前台状态变化后重新定位伴随窗口。

```text
LcuWindowTracker 每 250ms 读取 LeagueClientUx 窗口状态
                         │
                         ▼
                 LcuWindowState 发生变化
                         │
                         ▼
          LcuCompanionWindowController.UpdateWindow
                         │
              ┌──────────┴──────────┐
              │                     │
              ▼                     ▼
    PlacementCalculator      ZOrderCalculator
      计算坐标和尺寸          计算普通窗口 Z 序
              │                     │
              └──────────┬──────────┘
                         ▼
        SetWindowPos 同步位置、尺寸和 Z 序
                         │
                         ▼
       ApplyPlacementSide 更新接缝、圆角和阴影
```

核心实现文件：

- [`src/Prometheus/Services/LcuWindowTracker.cs`](../src/Prometheus/Services/LcuWindowTracker.cs)：发现并跟踪 LCU 主窗口。
- [`src/Prometheus/Services/LcuCompanionWindowController.cs`](../src/Prometheus/Services/LcuCompanionWindowController.cs)：协调快照、设置、窗口状态和伴随窗口生命周期。
- [`src/Prometheus/Services/LcuCompanionPlacementCalculator.cs`](../src/Prometheus/Services/LcuCompanionPlacementCalculator.cs)：计算右侧、左侧或内部右侧位置。
- [`src/Prometheus/Services/LcuCompanionZOrderCalculator.cs`](../src/Prometheus/Services/LcuCompanionZOrderCalculator.cs)：保证伴随窗口在普通非置顶 Z 层中位于 LCU 正上方。
- [`src/Prometheus/Services/LcuCompanionChrome.cs`](../src/Prometheus/Services/LcuCompanionChrome.cs)：计算不同吸附方向对应的边框样式。
- [`src/Prometheus/Views/LcuCompanionWindow.xaml`](../src/Prometheus/Views/LcuCompanionWindow.xaml) 和 [`LcuCompanionWindow.xaml.cs`](../src/Prometheus/Views/LcuCompanionWindow.xaml.cs)：伴随窗口外观及原生扩展样式。

## LCU 窗口跟踪

`LcuWindowTracker` 使用 `DispatcherTimer` 在 WPF 后台优先级下每 250 毫秒执行一次 `Poll()`。当前实现没有使用 `SetWinEventHook`，因此窗口变化是通过比较相邻两次轮询结果检测的。

### 定位主窗口

每次轮询按以下顺序定位 LCU 主窗口：

1. 从 `ILeagueClient.ProcessId` 取得当前 `LeagueClientUx` 进程 ID。
2. 使用 `EnumWindows` 枚举桌面顶层窗口。
3. 仅保留属于该进程、可见、能够取得边界，并且宽度至少为 400 像素、高度至少为 300 像素的窗口。
4. 在候选窗口中选择面积最大的窗口作为 LCU 主窗口。

进程 ID 不可用、没有合格窗口或无法取得窗口边界时，跟踪器发布 `LcuWindowState.Unavailable`。

### 采集的窗口状态

每次成功定位后会采集：

- LCU 窗口句柄；
- 窗口物理像素边界；
- 所在显示器的工作区；
- 窗口 DPI；
- 窗口是否可见；
- 窗口是否最小化；
- LCU 进程是否拥有当前前台窗口。

窗口边界优先通过 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 获取，以避开传统 `GetWindowRect` 可能包含的不可见调整边框；DWM 调用失败时回退到 `GetWindowRect`。

可见状态同时检查 `IsWindowVisible` 和 `DWMWA_CLOAKED`，以识别被 DWM 隐藏但仍具有可见样式的窗口。最小化状态通过 `IsIconic` 判断。

`LcuWindowState` 是值可比较的 record。只有完整状态与上次结果不同时，跟踪器才更新 `Current` 并发布 `StateChanged`。因此下列变化都会驱动伴随窗口重新计算：

- 移动或调整 LCU 窗口尺寸；
- 跨显示器；
- DPI 变化；
- 最小化、恢复、隐藏或重新显示；
- LCU 窗口关闭、重建或进程断开；
- 前台状态变化。

250 毫秒的轮询周期为规格要求的 500 毫秒内同步预留了一次额外轮询和 UI 调度时间。

## 吸附位置计算

`LcuCompanionPlacementCalculator.Calculate` 接收 `LcuWindowState`，返回物理像素坐标、尺寸以及 `LcuCompanionSide`。

可用方向按固定优先级选择：

1. `Right`：当前显示器工作区在 LCU 右侧的剩余宽度足够时，贴在 LCU 右侧外缘。
2. `Left`：右侧不足但左侧剩余宽度足够时，贴在 LCU 左侧外缘。
3. `InsideRight`：两侧都不足时，覆盖在 LCU 窗口内部右缘。

默认逻辑宽度为 344 DIP。计算器根据 LCU 窗口 DPI 转换为物理像素：

```text
物理像素宽度 = round(344 × DPI ÷ 96)
```

物理宽度不会超过当前显示器工作区宽度。伴随窗口高度取 LCU 窗口高度和工作区高度中的较小值，顶部坐标也会被钳制在工作区内。

三种位置的横坐标分别为：

```text
Right:      LCU.Right
Left:       LCU.Left - Companion.Width
InsideRight: clamp(LCU.Right - Companion.Width, WorkArea.Left, WorkArea.Right - Companion.Width)
```

控制器将计算得到的物理像素尺寸换算回 WPF DIP 后设置 `Window.Width` 和 `Window.Height`，再把原始物理像素位置和尺寸传给 `SetWindowPos`。这样 WPF 布局尺寸与原生窗口实际尺寸在不同 DPI 下保持一致。

## 状态变化后的窗口更新

`LcuCompanionWindowController` 订阅三个变化源：

- `IMatchService.SnapshotChanged`；
- `ILcuWindowTracker.StateChanged`；
- `ILcuCompanionSettings.PropertyChanged`。

所有变化最终都会调度到 WPF UI 线程执行 `UpdateWindow()`。

### 显示条件

只有同时满足下列条件时才显示并定位伴随窗口：

- 控制器已经启动；
- 当前阶段为 `GameflowPhase.ChampSelect`；
- “选人阶段吸附窗”设置已开启；
- LCU 主窗口可定位且边界有效；
- LCU 窗口可见；
- LCU 窗口没有最小化。

任一条件不满足时隐藏伴随窗口。因此：

- 离开 `ChampSelect` 时隐藏；
- 用户关闭功能开关时立即隐藏；
- LCU 断开、关闭或窗口不可用时隐藏；
- LCU 最小化时隐藏；
- LCU 恢复且仍处于 `ChampSelect` 时，在后续轮询中重新显示和定位。

显示时先计算位置和吸附方向，再调用 `Window.Show()` 创建或复用窗口句柄，最后使用 `SetWindowPos` 同步位置、尺寸和 Z 序。`SetWindowPos` 失败或发生其他原生窗口异常时，伴随窗口会被隐藏，错误会写入日志，但不会中断 LCU 数据协调器或退出应用。

### Prometheus 主窗口

伴随窗口成功显示且 `SetWindowPos` 成功后，控制器才隐藏 Prometheus 主窗口。`_mainWindowHiddenForPhase` 保证同一个英雄选择阶段只自动隐藏一次：用户在该阶段从托盘手动重新打开主窗口后，后续 LCU 位置变化不会再次强制隐藏它。

离开 `ChampSelect` 后该标记重置，为下一次英雄选择阶段做准备。

## Z 序和焦点

伴随窗口不使用全局置顶。`LcuCompanionZOrderCalculator` 读取 LCU 窗口在普通 Z 序中的前一个窗口：

- 如果前一个窗口已经是伴随窗口，则在 `SetWindowPos` 中使用 `SWP_NOZORDER` 保留当前 Z 序；
- 否则把伴随窗口插入到该前一个窗口之后，使伴随窗口紧邻并位于 LCU 窗口正上方；
- 如果 LCU 已经是普通 Z 序中的顶部窗口，则使用 `HWND_TOP` 对应的零句柄位置。

这个处理保证 `InsideRight` 模式下伴随窗口不会被 LCU 自身遮住，同时又不会覆盖所有其他应用的置顶窗口。

定位使用的 `SetWindowPos` 标志包括：

- `SWP_NOACTIVATE`：移动和调整窗口时不激活伴随窗口；
- `SWP_SHOWWINDOW`：确保目标窗口显示；
- `SWP_NOOWNERZORDER`：不改变所有者窗口的 Z 序；
- 必要时增加 `SWP_NOZORDER`：伴随窗口已处在正确位置时避免重复调整。

## 窗口样式和吸附边框

伴随窗口的 WPF 配置包括：

- `WindowStyle="None"`，无系统标题栏和边框；
- `ResizeMode="NoResize"`，不允许用户调整大小；
- `ShowInTaskbar="False"`，不显示任务栏按钮；
- `ShowActivated="False"` 和 `Focusable="False"`，显示时不主动取得焦点；
- `AllowsTransparency="True"`，支持自定义圆角和阴影。

窗口句柄创建后还会增加以下原生扩展样式：

- `WS_EX_TOOLWINDOW`：作为工具窗口，不进入普通任务切换和任务栏展示；
- `WS_EX_NOACTIVATE`：点击和显示窗口时不激活窗口。

窗口消息钩子对 `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`，进一步避免鼠标交互抢走 LCU 的键盘焦点。

`LcuCompanionChromeCalculator` 根据吸附方向生成视觉参数：

| 吸附方向 | 接缝 | 圆角 | 内缩 | 阴影 |
| --- | --- | --- | --- | --- |
| `Right` | 左侧 3px | 靠近 LCU 的左侧圆角为 0，外侧右圆角为 14 | 0 | 无 |
| `Left` | 右侧 3px | 靠近 LCU 的右侧圆角为 0，外侧左圆角为 14 | 0 | 无 |
| `InsideRight` | 无 | 四角均为 14 | 8px | 有 |

外部贴靠时，3px 接缝使用主题的 `InfoBrush`，用于强调伴随窗口与 LCU 窗口的连接边。内部覆盖时使用完整圆角和阴影，使伴随窗口与 LCU 内容形成视觉层级。

## 启动和退出

控制器在 Prometheus 主窗口完成加载后启动：

1. 读取 `IMatchService.Current`；
2. 订阅比赛快照、设置和窗口跟踪事件；
3. 启动伴随窗口 ViewModel；
4. 启动 LCU 窗口跟踪器；
5. 立即执行一次窗口状态更新。

应用关闭时，控制器会：

1. 解除所有事件订阅；
2. 停止 250 毫秒轮询；
3. 停止伴随窗口 ViewModel；
4. 在 UI 线程关闭伴随窗口。

## 自动化测试

窗口位置和视觉计算使用无 Win32 依赖的纯计算类，便于单元测试。现有相关测试包括：

- [`LcuCompanionPlacementCalculatorTests.cs`](../src/Tests/Prometheus.Modules.ModuleName.Tests/Services/LcuCompanionPlacementCalculatorTests.cs)：右侧、左侧、内部覆盖和 DPI 宽度换算。
- [`LcuCompanionZOrderCalculatorTests.cs`](../src/Tests/Prometheus.Modules.ModuleName.Tests/Services/LcuCompanionZOrderCalculatorTests.cs)：插入 LCU 上方、保持已有顺序和 LCU 位于顶部的情况。
- [`LcuCompanionChromeCalculatorTests.cs`](../src/Tests/Prometheus.Modules.ModuleName.Tests/Services/LcuCompanionChromeCalculatorTests.cs)：三种吸附方向的接缝、圆角、内缩和阴影。
- [`MainWindowCompanionLifecycleTests.cs`](../src/Tests/Prometheus.Modules.ModuleName.Tests/ViewModels/MainWindowCompanionLifecycleTests.cs)：主窗口关闭时等待伴随窗口协调器完成停止流程。

## 当前实现边界

- 窗口变化采用 250 毫秒轮询，不是 Windows 原生位置变化事件，因此最多存在一个轮询周期加 UI 调度时间的延迟。
- 主窗口识别依赖 `ILeagueClient.ProcessId`，并以该进程中面积最大的合格可见顶层窗口作为 LCU 主窗口。
- `LcuWindowState.IsForeground` 会参与状态比较并触发更新；当前控制器始终维护伴随窗口紧邻 LCU 上方的普通 Z 序，而不是仅在 LCU 成为前台窗口时调整。
- 原生窗口调用失败时采用隐藏伴随窗口的保守策略，避免留下与 LCU 脱离的悬空窗口。
