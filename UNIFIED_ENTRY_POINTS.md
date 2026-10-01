# 统一入口清单

这份文档记录当前项目里已经收敛出来的“统一入口”。后续调整 UI 尺寸、间距、字号、悬浮窗表现时，优先从这里列出的入口改，避免把数值散落到多个页面里。

## 使用原则

- 优先改统一入口，不要直接改使用处的单个控件属性。
- 页面级入口只影响当前页面；全局资源会影响多个页面。
- 如果一个参数已经有更高层入口，优先改高层入口。
- 修改功能后，记得保存代码到本地仓库。

## 全局 UI 资源

文件：`BluetoothAudioReceiver/Styles/Values.xaml`

用途：全局字号、边距、导航页内容区间距、设置页卡片间距。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `LargeFontSize` | `24` | `PageTitleStyle` 等大标题字号 |
| `MediumFontSize` | `16` | `BodyTextStyle` 等正文基础字号 |
| `LargeTopMargin` / `LargeTopBottomMargin` | `0,36,0,0` / `0,36,0,36` | 大间距 |
| `MediumTopMargin` / `MediumTopBottomMargin` / `MediumLeftRightMargin` / `MediumBottomMargin` | `24` 系列 | 中等间距 |
| `SmallLeftMargin` / `SmallRightMargin` / `SmallTopMargin` / `SmallBottomMargin` / `SmallLeftRightMargin` / `SmallTopBottomMargin` / `SmallMargin` / `SmallLeftBottomMargin` | `12` 系列 | 小间距 |
| `XSmallLeftMargin` / `XSmallTopMargin` / `XSmallLeftTopRightBottomMargin` | `8` 系列 | 更小间距 |
| `XXSmallTopMargin` / `XXSmallLeftTopRightBottomMargin` | `4` 系列 | 最小间距 |
| `NavigationViewContentGridBorderThickness` | `0,0,0,0` | 导航内容区域边框厚度 |
| `NavigationViewContentGridCornerRadius` | `0,0,0,0` | 导航内容区域圆角 |
| `NavigationViewContentMargin` | `0,48,0,0` | 导航内容区外边距 |
| `NavigationViewHeaderMargin` | `56,34,0,0` | 导航页头部边距 |
| `NavigationViewPageContentMargin` | `56,24,56,0` | 页面内容区统一边距 |
| `MenuBarContentMargin` | `36,24,36,0` | 菜单栏内容边距 |
| `ScrollViewerContentPadding` | `0,0,0,16` | 滚动内容底部留白 |
| `SettingsCardSpacing` | `3` | 设置页卡片纵向间距 |

文件：`BluetoothAudioReceiver/Styles/Controls.xaml`

用途：全局控件样式。多数页面通过这些样式复用字号、背景、标题样式。

主要入口：

| 入口 | 影响范围 |
| --- | --- |
| `CardPanelStyle` | 通用卡片容器背景、边框、圆角 |
| `PageTitleStyle` | 页面标题文字 |
| `BodyTextStyle` | 正文文字 |
| `ListTitleStyle` | 列表主标题 |
| `ListSubTitleStyle` | 列表副标题 |
| `DetailSubTitleStyle` | 详情副标题 |
| `DetailBodyBaseMediumStyle` | 详情正文弱强调 |
| `DetailBodyStyle` | 详情正文 |
| `AppendTextBlockStyle` | 附加说明文字 |
| `SettingsSectionHeaderTextBlockStyle` | 设置页分组标题 |

文件：`BluetoothAudioReceiver/App.xaml`

用途：应用级资源合并入口。

主要入口：

| 入口 | 影响范围 |
| --- | --- |
| `Styles/Controls.xaml` | 全局控件样式 |
| `Styles/Values.xaml` | 全局尺寸、间距、字号资源 |
| `NavigationViewContentBackground` | 导航内容区域背景，当前为透明 |

## 悬浮通知

文件：`BluetoothAudioReceiver/Contracts/Services/NotificationLayoutProfiles.cs`

用途：悬浮通知尺寸统一入口。后续改通知宽高优先改这里。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `NotificationLayoutProfiles.Default` | `600 x 110` | 标准通知，例如音频管理常规提示 |
| `NotificationLayoutProfiles.Compact` | `420 x 110` | 紧凑通知，适合短内容 |
| `NotificationLayoutProfiles.Hotkey` | `400 x 110` | 快捷键通知 |

文件：`BluetoothAudioReceiver/Views/Windows/NotificationWindow.xaml.cs`

用途：悬浮通知动画节奏、透明度、图标位移入口。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `NotificationAnimationSettings.ShowDurationMs` | `300` | 显示动画时长 |
| `NotificationAnimationSettings.HideDurationMs` | `200` | 隐藏动画时长 |
| `NotificationAnimationSettings.AutoHideDelayMs` | `2500` | 自动隐藏延迟 |
| `NotificationAnimationSettings.StartOffsetY` | `30` | 入场/退场纵向位移 |
| `NotificationAnimationSettings.AnimationFrameMs` | `16` | 动画刷新间隔 |
| `NotificationAnimationSettings.WindowOpacityMin` | `0` | 最小窗口透明度 |
| `NotificationAnimationSettings.WindowOpacityMax` | `255` | 最大窗口透明度 |
| `NotificationAnimationSettings.CubicEasePower` | `3` | 缓动曲线力度 |
| `NotificationIconLayoutSettings.IconScaleMultiplier` | `1.3` | 图标缩放倍率 |
| `NotificationIconLayoutSettings.DefaultIconMargin` | `-8,0,0,0` | 默认通知图标偏移 |
| `NotificationIconLayoutSettings.CompactIconMargin` | `0` | 紧凑通知图标偏移 |

注意：`NotificationWindow.xaml.cs` 里还有 `DefaultWindowWidth`、`DefaultWindowHeight`、`CompactWindowWidth`、`CompactWindowHeight` 作为兜底默认值。常规尺寸调整优先改 `NotificationLayoutProfiles.cs`，不要优先改这些兜底常量。

## 托盘悬浮面板

文件：`BluetoothAudioReceiver/Views/Windows/TrayQuickPanelWindow.xaml.cs`

用途：托盘悬浮弹窗的标题、分割线、列表行、窗口尺寸、列宽、热键徽章等视觉参数统一入口。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `TitleFontSize` | `14` | 标题“音频管理”字号 |
| `HeaderHintFontSize` | `11` | 标题后说明文字字号 |
| `HeaderSectionPadding` | `40,10,10,10` | 标题区内边距 |
| `HeaderDividerHorizontalInset` | `0` | 标题下分割线左右缩进；调小可加长，调大可缩短，允许负值 |
| `HeaderDividerTopSpacing` | `4` | 分割线上边距 |
| `HeaderDividerBottomSpacing` | `6` | 分割线下边距 |
| `HeaderDividerHeight` | `1` | 分割线高度 |
| `RowFontSize` | `12` | 设备名称字号 |
| `HotkeyFontSize` | `12` | 快捷键字号 |
| `DotRadius` | `4` | 左侧状态圆点视觉半径 |
| `RowPadding` | `6,5,6,5` | 每行内容内边距 |
| `SectionSpacing` | `10` | 区块间距预留参数 |
| `WindowWidth` | `650` | 浮窗宽度 |
| `WindowHeight` | `400` | 浮窗高度 |
| `DotColumnWidth` | `20` | 圆点列宽 |
| `IconColumnWidth` | `30` | 左侧图标列宽 |
| `NameColumnWidth` | `170` | 设备名称列宽 |
| `HotkeyColumnWidth` | `220` | 快捷键列宽 |
| `DeviceColumnsSpacing` | `0` | 设备行四列之间的间距 |
| `DeviceRowsSpacing` | `0` | 设备行之间的间距 |
| `HotkeyColumnMinWidth` | `120` | 快捷键列最小宽度 |
| `RowBackgroundAlpha` | `20` | 行悬浮背景透明度 |
| `RowBackgroundColor` | `180,180,180` with alpha | 行悬浮背景色 |
| `RowIdleBackgroundColor` | alpha `0` | 行默认背景色 |
| `RowCornerRadius` | `6` | 行圆角 |
| `RowOuterMargin` | `4,2,4,2` | 行外边距 |
| `HotkeyBadgeBackgroundAlpha` | `20` | 快捷键徽章背景透明度 |
| `HotkeyBadgeBackgroundColor` | `150,150,150` with alpha | 快捷键徽章背景色 |
| `HotkeyBadgeCornerRadius` | `6` | 快捷键徽章圆角 |
| `HotkeyBadgePadding` | `12,4,12,4` | 快捷键徽章内边距 |
| `HotkeyPlaceholder` | `--` | 快捷键为空时占位文本 |
| `DotActiveColor` | `108,203,95` | 激活圆点颜色 |
| `DotInactiveColor` | alpha `0` | 未激活圆点颜色 |

## 控制中心页面

文件：`BluetoothAudioReceiver/Views/Pages/ControlCenterPage.xaml`

用途：控制中心页面字号、卡片标题、模式按钮宽度和间距。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `ControlCenterPageTitleFontSize` | `24` | 控制中心页面主标题字号，当前预留 |
| `ControlCenterSectionTitleFontSize` | `18` | 卡片标题字号 |
| `ControlCenterModeTitleFontSize` | `16` | “性能模式”“显卡模式”等模块标题字号 |
| `ControlCenterBodyFontSize` | `14` | 普通说明文字字号 |
| `ControlCenterValueFontSize` | `14` | 右侧状态值字号 |
| `ControlCenterSectionSpacing` | `12` | 功能块之间的纵向间距 |
| `ControlCenterModeButtonMinWidth` | `72` | 模式按钮最小宽度 |
| `ControlCenterModeButtonSpacing` | `8` | 模式按钮横向间距 |
| `CardStyle` | 背景、边框、圆角、内边距 | 控制中心卡片外观 |
| `PageTitleStyle` | 页面标题样式 | 控制中心页面标题 |
| `SectionTitleStyle` | 卡片标题样式 | 控制中心卡片标题 |
| `BodyLabelStyle` | 普通标签样式 | 控制中心正文标签 |
| `StatusValueStyle` | 状态值样式 | 控制中心状态值 |

## 自动亮度页面

文件：`BluetoothAudioReceiver/Views/Pages/AutoBrightnessPage.xaml`

用途：自动亮度页面卡片、标题、正文、行距、输入框宽度等页面级入口。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `AutoBrightnessCardCornerRadius` | `4` | 自动亮度卡片圆角 |
| `AutoBrightnessPageMargin` | `24` | 页面外边距 |
| `AutoBrightnessCardPadding` | `12` | 卡片内边距 |
| `AutoBrightnessPageTitleFontSize` | `24` | 页面标题字号 |
| `AutoBrightnessSectionTitleFontSize` | `16` | 卡片/分组标题字号 |
| `AutoBrightnessBodyFontSize` | `14` | 正文字号 |
| `AutoBrightnessRowSpacing` | `8` | 行间距 |
| `AutoBrightnessHeaderSpacing` | `8` | 标题区列间距 |
| `AutoBrightnessHeaderToggleSpacing` | `10` | 标题和开关之间的间距 |
| `AutoBrightnessSelectionBoxWidth` | `200` | 下拉/选择框宽度 |
| `AutoBrightnessShortcutInputWidth` | `260` | 快捷键输入框宽度 |

## 快捷键通知页面

文件：`BluetoothAudioReceiver/Views/Pages/HotkeyNotificationPage.xaml`

用途：快捷键通知配置页卡片、徽章、空状态区域、按钮间距等页面级入口。

主要入口：

| 入口 | 当前值 | 影响范围 |
| --- | --- | --- |
| `HotkeyNotificationPageSectionSpacing` | `16` | 页面区块间距 |
| `HotkeyNotificationPageActionButtonSpacing` | `8` | 操作按钮间距 |
| `HotkeyNotificationPageCardWidth` | `246` | 快捷键卡片宽度 |
| `HotkeyNotificationPageCardHeight` | `150` | 快捷键卡片高度 |
| `HotkeyNotificationPageCardPadding` | `12` | 快捷键卡片内边距 |
| `HotkeyNotificationPageCardMargin` | `0,0,20,16` | 快捷键卡片外边距 |
| `HotkeyNotificationPageBadgePadding` | `10,5` | 徽章内边距 |
| `HotkeyNotificationPageBadgeCornerRadius` | `8` | 徽章圆角 |
| `HotkeyNotificationPageEmptyStatePadding` | `24` | 空状态区域内边距 |
| `HotkeyNotificationPageEmptyStateCornerRadius` | `16` | 空状态区域圆角 |
| `HotkeyNotificationPageEmptyStateOpacity` | `0.72` | 空状态文字透明度 |
| `HotkeyCardShellStyle` | 宽、高、内边距、外边距 | 快捷键卡片外壳 |
| `HotkeyCardBadgeStyle` | 徽章布局和外观 | 快捷键徽章 |
| `HotkeyCardBadgeTextStyle` | 徽章文字 | 快捷键徽章文字 |
| `HotkeyCardStatusTextStyle` | 状态文字 | 快捷键卡片状态行 |
| `HotkeyEmptyStateBorderStyle` | 空状态容器 | 无卡片时的提示区 |
| `HotkeyEmptyStateTextStyle` | 空状态文字 | 无卡片时的提示文字 |

## 快速定位建议

- 改全局页面间距：先看 `Styles/Values.xaml`。
- 改全局文字/卡片样式：先看 `Styles/Controls.xaml`。
- 改通知宽高：先看 `NotificationLayoutProfiles.cs`。
- 改通知动画/图标偏移：看 `NotificationWindow.xaml.cs`。
- 改托盘悬浮面板：看 `TrayQuickPanelWindow.xaml.cs` 顶部“可直接修改的设计参数”。
- 改控制中心：看 `ControlCenterPage.xaml` 的 `Page.Resources`。
- 改自动亮度页：看 `AutoBrightnessPage.xaml` 的 `Page.Resources`。
- 改快捷键通知页：看 `HotkeyNotificationPage.xaml` 的 `Page.Resources`。
