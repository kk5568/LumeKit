# DeskBox 与 LumeKit 浮层实现对比及修改计划

## 第 3 轮完成情况

第 3 轮已完成，范围仍然只限于新设计：

- 增加 `IDeskBoxFloatingService`，以后通知、托盘面板和右键菜单共用一个入口。
- 把 `Notification / Panel / Menu` 三种类型单独定义，调用方不再各自复制判断。
- 浮层入口统一限制显示时间和自定义宽高，避免不同调用方传入异常值。
- 设置页测试按钮已改为调用公共接口。
- 旧的 `NotificationWindow`、`NotificationService`、托盘面板和右键菜单没有切换。

当前状态：

```text
测试按钮
    ↓
IDeskBoxFloatingService
    ↓
DeskBoxFloatingHost
    ↓
DeskBoxFloatingWindow
```

这表示新浮层已经具备公共入口，但还没有接入真实通知、托盘面板和右键菜单业务。

## 新悬浮通知 1:1 迁移启动报告

目标已调整为：以 DeskBox 托盘右键浮层的原代码为底座，直接迁移窗口外壳和公共资源，再把菜单内容替换为通知内容。

第 1 轮已开始并完成：

- 搬入 DeskBox `WindowShellState` 的窗口状态处理。
- 新浮层使用 DeskBox 的无任务栏、无边框、固定大小窗口设置。
- 补充 DeskBox 的浮层资源名称、字体、圆角、菜单内边距和菜单行高。
- 新浮层外框改用 DeskBox 的 `WidgetOverlaySurfaceBrush` 和 `WidgetOverlayStrokeBrush`。

本轮没有修改：

- 现有右键托盘菜单。
- 现有 `NotificationWindow`。
- 现有 `NotificationService`。
- 通知业务和托盘业务。

## 可扩展浮窗配置改造

新浮层不再把尺寸写死为“通知、面板、菜单”三种。

现在的结构是：

```text
DeskBoxFloatingRequest
    ↓
ProfileId
    ↓
DeskBoxFloatingProfiles
    ↓
宽高、最小/最大尺寸、内边距、定位、自动关闭
```

已录入的配置：

| 配置名 | 尺寸 | 来源 |
| --- | ---: | --- |
| `Notification.Default` | `600 x 110` | 现有标准通知 |
| `Notification.Compact` | `420 x 110` | 现有紧凑通知 |
| `Notification.Automation` | `600 x 130` | 现有自动化通知 |
| `Notification.Merged` | `600 x 180` | 现有合并通知 |
| `Notification.Hotkey` | `400 x 110` | 现有快捷键通知 |
| `Tray.QuickPanel` | `260 x 400` | `TrayQuickPanelWindow.xaml` 实际值 |
| `Tray.ContextMenu` | `192 x 116` | DeskBox 菜单宽度和行高推算值 |

以后增加新浮窗，只增加一个新的配置档，例如：

```text
Device.Selector
Automation.Detail
Confirmation.Dialog
Search.Popup
```

窗口代码不需要再增加新的 `switch` 分支。

当前已经支持的可变参数：

- 宽度和高度
- 最小宽度和最小高度
- 最大宽度和最大高度
- 内边距
- 内容间距
- 显示位置类型
- 是否自动关闭
- 默认显示时间
- 单次请求的宽高覆盖值

本次改造仍然只影响新 `DeskBoxFloating` 模块，旧通知、旧托盘面板和旧右键菜单没有切换。

## 新设计阶段完成记录

本阶段完成的是“独立新浮窗设计”，不是业务交接。

已完成：

- DeskBox 风格无边框窗口底座。
- 可扩展浮窗配置档。
- 通知、紧凑通知、自动化通知、合并通知、快捷键通知、托盘面板、右键菜单预览。
- 内容布局类型：单行、标题说明、通知列表、设备列表、命令菜单等。
- 新浮窗独立材质控制，使用 DeskBox 通知材质适配路径。
- 支持底部居中、屏幕居中、鼠标/托盘锚点、工作区边界限制。
- 自动关闭和手动关闭仍由新浮窗自己管理。
- 设置页“测试”按钮只调用 `IDeskBoxFloatingService`。

明确未做：

- 未替换 `NotificationWindow`。
- 未替换 `NotificationService`。
- 未替换旧托盘右键菜单。
- 未替换旧托盘快速面板。
- 未清理旧动画、旧材质和旧资源。
- 未接入真实通知、托盘或快捷菜单业务。

代码审查结果：

| 轮次 | 审查结论 |
| --- | --- |
| 第 1 轮 窗口底座 | 只改新 `DeskBoxFloating` 模块，旧设计未切换 |
| 第 2 轮 可变配置 | 尺寸参数来自旧通知和旧托盘实际值，窗口核心不再按三种枚举写死 |
| 第 3 轮 定位边界 | 已支持锚点和工作区限制，真实托盘图标矩形读取后续业务接入时再绑定 |
| 第 4 轮 材质 | 新浮窗独立使用材质控制，不替换旧通知材质 |
| 第 5 轮 动画 | 保留新浮窗独立动画，不删除旧动画 |
| 第 6 轮 内容布局 | 只做预览内容，不复制旧通知业务 |
| 第 7 轮 测试入口 | 设置页测试入口保留，未引入快捷键测试 |

Debug 编译结果：

```text
0 个错误
98 个项目原有警告
```

还未迁移的部分：

- DeskBox 原始动画控制器。
- DeskBox 原始材质控制器。
- DeskBox 原始托盘定位和多显示器定位代码。
- DeskBox 原始内容控件。

## 1. 结论先说

当前不是把 DeskBox 整个浮层系统原样搬进 LumeKit，而是：

```text
DeskBox 的部分公共资源
        +
DeskBox 的一段 Composition 入场/退场动画
        +
LumeKit 自己的通知窗口、材质和业务逻辑
```

因此，当前测试浮层可以算“使用了 DeskBox 风格和部分原代码”，还不能算“完整按 DeskBox 实现”。

## 2. 总体对比

| 项目 | DeskBox 原项目 | LumeKit 当前实现 | 状态 |
| --- | --- | --- | --- |
| 托盘图标接收右键 | `H.NotifyIcon` 托盘入口 | `H.NotifyIcon` 托盘入口 | 已基本一致 |
| 菜单显示 | `MenuFlyout` | `MenuFlyout` | 已基本一致 |
| 菜单行项目 | `MenuFlyoutItem`、图标、文字、点击动作 | `MenuFlyoutItem`、图标、文字、点击动作 | 已基本一致 |
| 菜单公共资源 | DeskBox `App.xaml` 内的主题资源和菜单样式 | `DeskBoxMenuResources.xaml`，已搬入部分资源 | 部分实现 |
| 菜单弹出处理 | DeskBox 的 Popup 视觉树处理和边界设置 | LumeKit 有自己的菜单 Presenter 设置 | 部分实现 |
| 浮层窗口外壳 | DeskBox 自己的 Widget 窗口体系 | LumeKit 原有 `NotificationWindow` | 未一致 |
| 浮层材质 | `WidgetMaterialSystemBackdrop` | `NotificationDeskBoxBackdrop`，按 LumeKit 通知逻辑改写 | 部分实现 |
| 主题颜色 | DeskBox 的完整 Light/Dark 资源 | 已搬入主要背景、文字、边框资源 | 部分实现 |
| 圆角 | DeskBox 统一使用 4/6/8 三档 | 已搬入资源，但通知仍有本地圆角设置 | 部分实现 |
| 间距 | DeskBox 统一使用 4/8/12/16 | 已搬入资源，通知布局仍有自己的数值 | 部分实现 |
| 入场动画 | DeskBox Composition 动画 | 测试浮层已使用 DeskBox Composition 入场动画 | 已实现 |
| 退场动画 | DeskBox Composition 动画 | 测试浮层已使用 DeskBox Composition 退场动画 | 已实现 |
| 完整动画控制器 | `WidgetTrayAnimationController` 等控制器 | 仍使用 LumeKit 原有通知生命周期 | 未一致 |
| 动画时长设置 | DeskBox 统一动画设置 | 测试浮层使用固定的 DeskBox 动画时长 | 部分实现 |
| 位置计算 | DeskBox 按 Widget 窗口和托盘场景计算 | LumeKit 按通知窗口底部居中计算 | 未一致 |
| 内容控件结构 | DeskBox 的 Widget 内容和公共控件体系 | LumeKit 原有 `Grid` 和 `TextBlock` | 未一致 |
| 悬停、按下状态 | DeskBox 有完整状态资源和控件状态 | 测试浮层是通知，不支持这些交互状态 | 不适用/未实现 |
| 设置页测试入口 | DeskBox 没有 LumeKit 的测试按钮 | 已增加按钮直接显示测试浮层 | LumeKit 扩展 |
| 设置页测试按钮 | DeskBox 无此业务 | 目前还没有，待增加“测试”按钮 | 未实现 |
| 普通通知切换 | DeskBox 不负责 LumeKit 通知 | 普通通知仍使用旧样式和旧动画 | 暂未推广 |

## 3. 已经直接搬入的 DeskBox 内容

### 3.1 公共资源

来源：

```text
开发文档/引用项目/DeskBox-main/src/DeskBox/App.xaml
```

已搬入：

- 浅色主题和深色主题的主要背景色
- 主文字和次要文字颜色
- 浮层边框颜色
- 悬停、按下、打开状态颜色
- `4 / 6 / 8` 三档圆角
- `4 / 8 / 12 / 16` 间距
- 标题、正文、说明文字字号
- DeskBox 菜单字体
- 菜单行高和菜单内边距
- DeskBox 的动画节奏资源

LumeKit 文件：

```text
BluetoothAudioReceiver/Styles/DeskBoxMenuResources.xaml
```

### 3.2 测试浮层 Composition 动画

来源：

```text
开发文档/引用项目/DeskBox-main/src/DeskBox/Helpers/DetailPageTransitionHelper.cs
```

已搬入：

- Composition 动画
- 入场 `210ms`
- 退场 `150ms`
- 入场从下往上移动
- 退场向下移动
- DeskBox 的缓动曲线
- 动画期间同步透明度变化

LumeKit 文件：

```text
BluetoothAudioReceiver/Helpers/DeskBoxFloatingTransitionHelper.cs
```

当前只给测试浮层使用，普通通知仍走原来的动画。

## 4. 还没有按 DeskBox 原项目实现的部分

### 4.1 浮层窗口没有改成 DeskBox 的窗口体系

DeskBox 使用自己的 Widget 窗口、Widget Shell 和窗口状态管理。

LumeKit 现在仍然是：

```text
NotificationWindow
    ↓
NotificationSurface
    ↓
NotificationCard
    ↓
原通知内容 Grid
```

这意味着现在只是把 DeskBox 的资源和动画放进了 LumeKit 原窗口，不是直接使用 DeskBox 的窗口结构。

### 4.2 材质类不是 DeskBox 原类

DeskBox 原项目使用：

```text
WidgetMaterialSystemBackdrop
```

LumeKit 使用：

```text
NotificationDeskBoxBackdrop
```

LumeKit 这个类是根据 DeskBox 逻辑改写的通知专用版本，能够使用 Mica、Acrylic 和降级背景，但代码结构和 DeskBox 原类不完全相同。

### 4.3 动画控制器没有完整搬入

DeskBox 有：

```text
WidgetTrayAnimationController
WidgetTrayBatchAnimationDriver
WidgetAnimationFramePacingPolicy
WidgetAnimationDisplayTiming
TrayAnimationInterruptionCoordinator
```

LumeKit 当前只搬了单个元素的显示和隐藏动画，没有搬入：

- 多窗口协调
- 动画打断后恢复
- 帧率和掉帧处理
- 托盘批量动画
- 窗口状态恢复

测试浮层目前够用，但还不是 DeskBox 的完整动画系统。

### 4.4 内容布局不是 DeskBox 原控件

DeskBox 的菜单和 Widget 内容有自己的控件、模板和状态处理。

LumeKit 测试浮层仍使用原通知里的：

```text
DefaultContentGrid
IconGlyph
MessageText
StatusText
```

因此布局只是套用了 DeskBox 的公共参数，没有直接搬 DeskBox 的内容控件。

### 4.5 资源没有完全替换本地参数

虽然已经有公共资源，但通知中仍存在本地参数，例如原通知的默认宽度、默认高度、内边距和动画参数。

当前做法是：

```text
测试浮层使用一部分 DeskBox 资源
普通通知继续使用 LumeKit 本地参数
```

后续推广公共样式时，需要把重复的本地参数逐步收回公共资源。

## 5. 当前实现边界

### 测试浮层已经使用

- DeskBox 主题资源的一部分
- DeskBox 圆角和间距资源的一部分
- DeskBox 背景材质适配逻辑
- DeskBox Composition 入场动画
- DeskBox Composition 退场动画
- DeskBox 风格的窄型通知尺寸

### 测试浮层还没有使用

- DeskBox 完整 Widget 窗口结构
- DeskBox 完整 Widget Shell
- DeskBox 完整材质类
- DeskBox 完整动画控制器
- DeskBox 完整内容控件
- DeskBox 的多窗口和动画打断机制

## 6. 修改计划

修改顺序按“先易后难、先局部后公共、先不影响功能后改底层”安排。

### 第一阶段：先改公共资源

这部分最容易修改，不改变通知业务，也不影响普通通知功能。

1. 把 DeskBox 的颜色、圆角、间距、字体和动画参数继续集中到 `DeskBoxMenuResources.xaml`。
2. 删除通知窗口中重复的本地参数。
3. 让测试浮层优先从公共资源读取宽高、圆角、内边距和动画时长。
4. 保留普通通知原有参数，避免一次性影响全部通知。

结果：

```text
修改公共资源
    ↓
测试浮层外观跟着变化
    ↓
普通通知暂时不变
```

**代码审查点 1：公共资源完成后**

检查内容：

- 是否直接对应 DeskBox 原项目资源
- 是否只是搬运，没有加入无关设计
- 是否影响现有托盘菜单
- 是否影响普通通知
- 资源名称和来源是否写清楚

### 第二阶段：增加设置页“测试”按钮

这部分只增加一个测试入口，不使用快捷键，也不改变现有快捷键功能。

按钮名称：

```text
测试
```

点击后：

```text
通知设置页面
    ↓
点击“测试”
    ↓
弹出 DeskBox 测试浮层
```

测试内容：

- 背景材质
- 浅色和深色主题
- 浮层尺寸
- 圆角和边框
- 图标、文字和状态
- 从下往上出现
- 向下消失
- 2.5 秒、5 秒、10 秒自动隐藏

这一步不依赖真实快捷键，方便反复调整。

**代码审查点 2：测试按钮完成后**

检查内容：

- 是否已经完全去掉快捷键测试依赖
- 按钮是否只调用测试通知
- 是否没有改变现有快捷键功能
- 是否没有增加新的全局设置
- 点击按钮是否能稳定弹出 DeskBox 浮层

### 第三阶段：调整测试浮层视觉

只调整测试浮层，不改普通通知。

重点对照 DeskBox 图片检查：

- 浮层是否过大或过小
- 内容是否垂直居中
- 图标和文字间距是否一致
- 背景是否过亮或过暗
- 边框是否明显
- 圆角是否一致
- 动画方向是否从下往上
- 出现和消失速度是否自然

> 本阶段的名称需要同步改为“调整测试浮层视觉”，不再使用 Alt+4 作为测试入口。

**代码审查点 3：测试浮层视觉确认后**

检查内容：

- 外观是否以 DeskBox 原项目和引用图片为准
- 是否误改普通通知
- 是否误改托盘菜单
- 动画方向和时长是否与 DeskBox 原代码一致
- 公共参数是否仍然集中管理

### 第四阶段：补齐 DeskBox 材质实现

这部分改动较大，放在测试浮层外观确认后进行。

计划把当前通知专用的材质适配，逐步对齐 DeskBox 原项目的：

```text
WidgetMaterialSystemBackdrop
WidgetOverlaySurfaceBrush
WidgetOverlayStrokeBrush
Light / Dark ThemeDictionaries
```

这一步可能涉及窗口背景、系统版本兼容和降级处理，需要单独验证。

**代码审查点 4：材质实现完成后**

检查内容：

- 是否直接沿用 DeskBox 的材质处理思路
- Windows 不支持时是否能正常降级
- 浅色和深色主题是否正常
- 是否影响通知窗口关闭和重新显示
- 是否引入了不必要的 LumeKit 私有改写

### 第五阶段：补齐 DeskBox 动画控制

这部分风险最高，最后处理。

计划评估是否搬入：

- 动画设置统一管理
- 动画被打断后的恢复
- 帧率和掉帧处理
- 多窗口动画协调
- 窗口状态恢复

测试浮层稳定前，不改普通通知的动画控制。

这里应改为：

> 测试浮层稳定前，不改普通通知的动画控制。

**代码审查点 5：动画控制完成后**

检查内容：

- 是否真的使用 DeskBox 的动画控制方式
- 是否处理动画中断、重复触发和快速隐藏
- 是否存在定时器和 Composition 动画同时控制同一对象
- 是否影响窗口关闭和通知队列
- 是否仍保持从下往上出现、向下消失

### 第六阶段：推广到普通通知

只有测试浮层稳定后，才考虑把公共外壳推广给普通通知。

推广顺序：

1. 普通快捷键通知
2. 音频和设备通知
3. 自动化通知
4. 合并通知

每一类单独验证，避免一次改动影响所有通知。

**代码审查点 6：每推广一类通知后**

检查内容：

- 当前通知是否使用公共 DeskBox 外壳
- 普通通知内容是否保持原有功能
- 通知队列、自动隐藏和合并通知是否正常
- 托盘面板打开时的隐藏规则是否正常
- 是否可以只回退这一类通知，不影响其它通知

## 7. 审查总原则

每个阶段完成后，先审查再进入下一阶段，不把多个阶段一次性混在一起。

每次代码审查都回答四个问题：

1. 这次改动是否来自 DeskBox 原项目或已确认的 LumeKit 需求？
2. 是否改变了本阶段之外的功能？
3. 是否保留了现有功能和回退路径？
4. 下一阶段是否仍然符合“先测试、后推广”的顺序？

如果审查发现偏离目标：

```text
暂停下一阶段
    ↓
标出偏离的文件和代码
    ↓
先修正方案
    ↓
重新审查
```

## 8. 修改优先级

| 优先级 | 修改内容 | 难度 | 对整体功能影响 | 建议 |
| --- | --- | --- | --- | --- |
| 1 | 整理 DeskBox 公共资源 | 低 | 很小 | 最先做 |
| 2 | 设置页增加“测试”按钮 | 低 | 很小 | 第二步做 |
| 3 | 测试浮层尺寸、间距和文字调整 | 低 | 只影响测试浮层 | 第三步做 |
| 4 | 测试浮层背景材质对齐 DeskBox | 中 | 只影响测试浮层 | 测试后做 |
| 5 | 测试浮层窗口外壳对齐 DeskBox | 中 | 可能影响窗口行为 | 单独验证 |
| 6 | DeskBox 动画控制器搬入 | 高 | 可能影响动画生命周期 | 最后做 |
| 7 | 普通通知切换公共样式 | 高 | 影响所有通知 | 最后推广 |

## 9. 当前判断

当前完成度可以这样理解：

```text
DeskBox 资源：已搬一部分
DeskBox 动画：测试浮层已搬入主要单元素动画
DeskBox 材质：已做通知专用适配
DeskBox 窗口体系：尚未搬入
DeskBox 完整浮层系统：尚未完成
```

因此，当前更准确的名称是：

> DeskBox 资源和动画的测试浮层版本

而不是：

> 完全复刻 DeskBox 浮层系统
