# LumeKit 架构与自动化边界

更新时间：2026-09-24

> **本文已过期，仅作历史参考。** 文中提到的 `AutomationMainlineBus`、`AutomationTraceStore`、
> `AutomationTraceLogger`、`AutomationSessionStateStore`、`WallpaperDimService`，以及状态保存/恢复动作，
> 都已在 17.0 优化和后续清理中删除。当前口径以 `开发文档\开发计划.md` 为准。

## 1. 总体结构

LumeKit 采用“一个调度器、一个自动化中心、多个功能模块”的结构。

```text
LumeKit
│
├── 1级：调度器 Scheduler
│   │
│   ├── Observation
│   │   ├── 系统事件
│   │   ├── 程序 / 前台
│   │   ├── 电源 / 电量
│   │   ├── 蓝牙
│   │   ├── 音频设备
│   │   ├── 网络
│   │   ├── 主题
│   │   ├── 时间
│   │   └── 日出 / 日落
│   │
│   ├── Context
│   │   └── 统一系统状态
│   │
│   ├── Event Bus
│   │   ├── TriggerBus
│   │   └── MainlineBus
│   │
│   └── Scheduler
│       ├── 定时任务
│       ├── 循环任务
│       └── 监听生命周期
│
├── 2级：自动化 Automation
│   │
│   ├── Trigger
│   ├── Condition
│   ├── Rule
│   ├── RuleMatcher
│   ├── Decision
│   ├── Priority
│   ├── Cooldown / Once / Repeat
│   ├── ResourceConflict
│   ├── PipelineCoordinator
│   │
│   ├── ActionExecutor
│   │   │
│   │   ├── Automation Native Actions
│   │   │   ├── Delay
│   │   │   ├── Wait
│   │   │   ├── StartProcess
│   │   │   ├── StopProcess
│   │   │   ├── OpenUrl
│   │   │   ├── SaveState
│   │   │   └── RestoreState
│   │   │
│   │   └── Module Action Adapter
│   │       ├── Audio
│   │       ├── Brightness
│   │       ├── Power
│   │       ├── Theme
│   │       ├── Shortcut
│   │       └── Notification
│   │
│   ├── StateRestore
│   └── Trace / Log
│
└── 2级：功能模块 Modules
    │
    ├── Audio
    │   ├── Observation
    │   ├── State
    │   ├── Service
    │   ├── Controller
    │   └── Module Actions
    │       ├── SetInputDevice
    │       ├── SetOutputDevice
    │       ├── SetVolume
    │       └── AudioRoute
    │
    ├── Brightness
    │   ├── Observation
    │   ├── State
    │   ├── Service
    │   ├── Controller
    │   ├── AutoBrightness
    │   └── Module Actions
    │       └── SetBrightness
    │
    ├── Theme
    │   ├── Observation
    │   ├── State
    │   ├── Service
    │   ├── Controller
    │   ├── AutoDayNight
    │   └── Module Actions
    │       ├── SetTheme
    │       └── SetWallpaper
    │
    ├── Power
    │   ├── Observation
    │   ├── State
    │   ├── Service
    │   ├── Controller
    │   ├── PerformanceMode
    │   ├── BackgroundEfficiency
    │   └── Module Actions
    │       ├── SetPowerMode
    │       ├── SetPerformanceMode
    │       └── PreventSleep
    │
    ├── Shortcut
    │   ├── Registry
    │   ├── WindowsShortcut
    │   ├── GHelperShortcut
    │   └── CustomShortcut
    │
    ├── Notification
    │   ├── SystemNotification
    │   ├── AppNotification
    │   └── Module Actions
    │       └── ShowNotification
    │
    └── Program
        ├── Observation
        ├── Process
        ├── Foreground
        └── State
```

## 2. 三个核心定位

### 2.1 Scheduler

调度器负责：

- 发现系统和用户发生了什么变化。
- 采集需要的系统信息。
- 整理成统一状态。
- 发布统一事件。
- 管理定时任务、循环任务和监听生命周期。
- 控制监听的启动、停止、重连和释放。

调度器不负责：

- 判断某条自动化规则是否命中。
- 决定要执行哪一个动作。
- 直接修改音频、亮度、电源或主题。
- 保存某个功能模块自己的业务策略。

### 2.2 Automation

自动化负责：

- 接收调度器发布的事件和状态。
- 查找和匹配规则。
- 判断条件是否满足。
- 处理优先级、冷却、只执行一次和重复执行。
- 处理资源冲突。
- 按顺序编排动作。
- 调用自动化原生动作或功能模块动作。
- 记录执行过程和结果。
- 管理自动化状态保存和恢复。

自动化不负责：

- 重复监听系统事件。
- 自己实现音频、亮度、电源、主题等模块能力。
- 代替功能模块维护持续运行策略。

### 2.3 Modules

功能模块负责：

- 提供本模块的能力。
- 保存和维护本模块的状态。
- 执行本模块的动作。
- 维护本模块长期运行的策略。
- 向调度器提供需要观察的信息。
- 向自动化提供可调用的动作能力。

功能模块不负责：

- 读取自动化规则。
- 判断什么时候应该执行自动化。
- 直接控制其他模块。
- 为自动化重复实现一套规则判断。

## 3. 信息流

```text
系统 / 用户
    ↓
Scheduler Observation
    ↓
Event + Context
    ↓
Automation
    ↓
Rule → Condition → Decision → Pipeline
    ↓
ActionExecutor
    ├── Native Action
    └── Module Action Adapter
              ↓
        功能模块实际执行
              ↓
        系统状态发生变化
              ↓
        Observation
              ↓
       Event + Context 更新
```

## 4. 各层之间传递的内容

运行过程使用内存中的对象和消息传递，不通过临时文件传递任务。

### 4.1 调度器输出

调度器向自动化和其他模块输出：

- 系统事件。
- 统一系统状态。
- 事件发生时间。
- 事件来源。
- 关联 ID。
- 事件参数。

例如：

```text
事件：性能模式进入
参数：程序名、进程 ID、窗口状态、GPU 条件
状态：当前电源、音频设备、亮度和前台程序
```

### 4.2 自动化输出

自动化输出：

- 是否命中规则。
- 需要执行的动作列表。
- 动作顺序。
- 动作参数。
- 失败处理方式。
- 资源范围。
- 关联 ID。

自动化输出的是“要做什么”，不是具体的系统操作。

### 4.3 功能模块输出

功能模块输出：

- 当前模块状态。
- 是否可以执行。
- 动作执行结果。
- 失败原因。
- 受到影响的资源。
- 关联 ID。

模块输出的是“能不能做”和“做完没有”。

### 4.4 执行结果

所有动作统一返回：

- 成功或失败。
- 失败原因。
- 执行时间。
- 影响的资源。
- 关联 ID。

## 5. 自动化动作分类

### 5.1 自动化原生动作

这类动作只属于自动化，不依赖其他功能模块：

- Delay
- Wait
- StartProcess
- StopProcess
- OpenUrl
- SaveState
- RestoreState
- StopExecution

它们由自动化自己的执行器直接处理。

### 5.2 功能模块动作

这类动作属于功能模块，自动化只负责调用：

- Audio
- Brightness
- Power
- Theme
- Shortcut
- Notification

调用过程是：

```text
自动化动作执行器
    ↓
模块动作适配器
    ↓
功能模块
    ↓
模块控制器或系统接口
```

自动化不能复制模块的具体执行逻辑。

## 6. 关键边界

### 6.1 事件不等于状态

事件表示“发生了一次变化”：

```text
性能模式进入
输出设备发生变化
程序启动
```

状态表示“现在是什么情况”：

```text
当前是否处于性能模式
当前输出设备是什么
当前前台程序是什么
```

事件用于触发处理，状态用于判断条件和执行动作。

### 6.2 自动化动作不等于模块动作

自动化动作表示：

```text
规则希望做什么
```

模块动作表示：

```text
某个模块具体怎么做
```

例如：

```text
自动化动作：切换输出设备
模块动作：通过系统音频接口切换设备并确认结果
```

### 6.3 一次性动作不等于持续运行策略

一次性动作由自动化触发：

```text
进入性能模式时切换一次音频设备
```

持续运行策略由功能模块自己维护：

```text
持续监听环境光并自动调整亮度
持续监听设备变化并维护音频状态
```

自动化可以调用模块动作，但不接管模块的长期策略。

### 6.4 执行不等于执行结果

发出动作不代表动作已经完成。

```text
执行请求
→ 模块处理
→ 系统操作
→ 返回成功或失败
```

自动化必须等待并记录最终结果，不能只记录“已经发起”。

## 7. 模块之间的调用规则

允许：

```text
Scheduler → Automation
Scheduler → Modules
Automation → Module Action Adapter
Module Action Adapter → 对应功能模块
功能模块 → 自己的 Controller / System API
```

不允许：

```text
Automation → 直接调用 Windows API
Audio → 直接读取 Automation Rule
Brightness → 直接修改 Power 内部状态
Module → 代替 Automation 判断规则
多个模块 → 重复监听同一个系统事件
```

## 8. 当前项目的代码归属

以下是现有代码向新结构的归类，不要求一次性改名或搬目录。

### 8.1 Scheduler

- `AutomationObservationService`
- `AutomationObservationSubscriptionManager`
- `AutomationObservationRequestPlanner`
- `AutomationObservationGetters`
- `AutomationSystemContextHub`
- `AutomationTriggerBus`
- `AutomationMainlineBus`

当前这些代码承担了观察、状态整理、事件发布和部分循环调度。后续逐步收敛到 Scheduler。

### 8.2 Automation

- `AutomationRuleEngine`
- `AutomationPipelineCoordinator`
- `AutomationTriggerMatcher`
- `AutomationRuleDecisionService`
- `AutomationTriggerRegistry`
- `AutomationConditionRegistry`
- `AutomationPipelineRepository`
- `AutomationPipelineExecutor`
- `AutomationActionRegistry`
- `AutomationTraceStore`
- `AutomationTraceLogger`

### 8.3 Audio

- `AudioRouteHotkeyService`
- `AudioPlaybackConnectionService`
- `InputAudioVolumeService`
- `AutomationInputDeviceActionProvider`
- `AutomationOutputDeviceActionProvider`
- `AutomationInputVolumeActionProvider`
- `AudioOutputDeviceWatcherService`

### 8.4 Brightness

- `AutoBrightnessService`
- `AutomationBrightnessActionProvider`
- `BrightnessController`
- `CameraReaderService`
- `InternalPanelBrightnessService`
- `WmiBrightnessService`
- `ScreenGammaColorTemperatureController`

### 8.5 Theme

- `AutoDayNightService`
- `ThemeSelectorService`
- `AutomationThemeActionProvider`
- `SolarCalculator`
- `WallpaperDimService`

### 8.6 Power

- `BackgroundEfficiencyService`
- `AutomationPowerModeActionProvider`
- `TaskSchedulerManager`
- 性能模式相关的观察和判断代码

### 8.7 Shortcut

- `AutomationGHelperShortcutActionProvider`
- `AutomationWindowsShortcutActionProvider`
- `AutomationCustomShortcutActionProvider`
- `GHelperShortcutCatalog`
- `WindowsShortcutCatalog`

### 8.8 Notification

- `NotificationService`
- `AppNotificationService`
- `AutomationNotificationActionProvider`

### 8.9 Program

- 程序启动和退出监听
- 前台程序获取
- 进程控制动作
- `AutomationProcessActionProvider`
- `AutomationInstalledProgramScanner`
- `AutomationProgramListCatalog`

## 9. 配置、运行数据和日志

层级之间不通过临时文件传递运行任务。

### 保存配置

- 自动化规则：由自动化规则仓库保存。
- 功能模块配置：由各自模块保存。
- UI 设置：由现有设置服务保存。

### 保存运行数据

- 自动化执行记录：由 `AutomationTraceStore` 保存。
- 模块当前状态：优先保存在内存中。
- 需要恢复的状态：由自动化状态保存和恢复功能管理。

### 运行时传递

运行时使用：

- 事件。
- 状态快照。
- 自动化动作请求。
- 模块执行结果。
- 关联 ID。

## 10. 开发判断标准

新增功能时，先判断它属于哪一类：

### 如果是“发现变化”

放到 Scheduler Observation。

### 如果是“判断是否执行”

放到 Automation。

### 如果是“某个模块能做的事情”

放到对应功能模块。

### 如果是“真正调用系统完成操作”

放到对应模块的 Controller 或执行部分。

### 如果是“自动化专属的简单动作”

放到 Automation Native Actions。

## 11. 最终原则

```text
Scheduler
= 发现、采集、整理、传递

Automation
= 判断、编排、调用、记录

Modules
= 提供能力、执行能力、维护自身策略
```

最简判断方式：

```text
发生了什么？       → Scheduler
要不要执行？       → Automation
谁有能力执行？     → 对应功能模块
怎么真正执行？     → 模块自己的执行部分
执行结果如何？     → 返回 Automation 和 Trace
```
