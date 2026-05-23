# LumeKit

LumeKit 是一个音频管理工具，支持通过蓝牙连接其他设备音频、托盘浮窗快速操作和通知控制。

## 界面截图
<img width="1783" height="1591" alt="屏幕截图 2026-05-23 234044" src="https://github.com/user-attachments/assets/db288aad-ad2f-4b0c-a187-9dbf2e2abcaa" />

## 主要功能

- 蓝牙音频设备管理：扫描、连接、断开输入/输出音频设备。
- 快捷键切换：支持为输入路由与输出路由设置快捷键，并在候选设备中循环切换。
- 设备组合与卡片：可为快捷键绑定多个设备，统一管理已选设备列表。
- 托盘浮窗：鼠标操作即可快速连接/断开设备，并显示当前激活状态。
- 通知与静默策略：支持按场景抑制通知（如全屏、游戏等）。
- 开机自启与主题：支持系统托盘常驻、主题/背景效果配置。

## 系统要求

- 操作系统：Windows 10 2004（Build 19041）或更高版本（推荐 Windows 11）。
- 平台架构：x64（当前发布包为 x64）。
- 硬件要求：支持蓝牙的电脑，且存在可用音频输入/输出设备。
- 运行环境：Windows App SDK 运行所需系统组件（打包安装时会按项目配置处理）。

## 引用与致谢

本项目在实现思路和工程能力上参考/使用了以下开源项目与技术生态，特此致谢：

- [AudioPlaybackConnector](https://github.com/ysc3839/AudioPlaybackConnector)
- [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [CommunityToolkit.WinUI](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [WinUIEx](https://github.com/dotMorten/WinUIEx)
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)
- [Serilog](https://github.com/serilog/serilog)
- [CsWin32](https://github.com/microsoft/CsWin32)

## 许可证

本项目采用 [MIT License](LICENSE)。
