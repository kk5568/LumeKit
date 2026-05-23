# Bluetooth Audio Receiver

**蓝牙音频接收器** - 基于 WinUI 3 的 Windows 桌面应用，可通过蓝牙将音频接收到电脑上播放。

## 功能特性

- **蓝牙音频设备管理** - 扫描、发现并连接附近的蓝牙音频播放设备
- **音频播放连接** - 使用 Windows `AudioPlaybackConnection` API 将音频路由到蓝牙设备
- **系统托盘图标** - 后台运行时可通过托盘图标快速操作
- **启动管理** - 支持开机自启
- **主题与背景** - 支持浅色/深色主题切换，以及 Mica、Acrylic 等多种背景效果

## 项目结构

```
BluetoothAudioReceiver/
├── BluetoothAudioReceiver/              # 主项目（WinUI 3 应用）
│   ├── Activation/                      # 应用激活处理器
│   │   ├── ActivationHandler.cs         # 基础激活处理
│   │   ├── DefaultActivationHandler.cs  # 默认激活处理
│   │   └── AppNotificationActivationHandler.cs  # 通知激活处理
│   ├── Assets/                          # 应用图标与资源文件
│   ├── Behaviors/                       # UI 行为
│   │   └── NavigationViewHeaderBehavior.cs
│   ├── Contracts/Services/              # 服务接口定义
│   ├── Extensions/                      # 扩展方法
│   ├── Helpers/
│   │   ├── Application/                 # 应用信息、语言、常量
│   │   └── Navigation/                  # 导航辅助
│   ├── Models/
│   │   ├── Application/                 # 应用模型（语言项）
│   │   └── Settings/                    # 设置相关（键值定义）
│   ├── Services/                        # 应用服务
│   │   ├── ActivationService.cs         # 激活协调
│   │   ├── AudioPlaybackConnectionService.cs  # 蓝牙音频连接
│   │   ├── AppNotificationService.cs    # 通知服务
│   │   ├── AppSettingsService.cs        # 设置服务
│   │   ├── NavigationService.cs         # 导航服务
│   │   ├── ThemeSelectorService.cs      # 主题选择
│   │   └── BackdropSelectorService.cs   # 背景效果选择
│   ├── Strings/en-us/                   # 本地化资源
│   ├── Styles/                          # XAML 样式定义
│   ├── UserControls/                    # 自定义控件（系统托盘菜单）
│   ├── ViewModels/Pages/                # 页面 ViewModel
│   │   ├── BluetoothPageViewModel.cs    # 蓝牙设备列表
│   │   ├── HomePageViewModel.cs         # 主页
│   │   ├── NavShellPageViewModel.cs     # 导航壳
│   │   └── SettingsPageViewModel.cs     # 设置页
│   ├── Views/Pages/                     # XAML 页面
│   │   ├── BluetoothPage.xaml           # 蓝牙设备页面
│   │   ├── HomePage.xaml                # 主页
│   │   ├── NavShellPage.xaml            # 导航壳
│   │   ├── SettingsPage.xaml            # 设置页面
│   │   └── SplashScreenPage.xaml        # 启动画面
│   ├── Views/Windows/                   # 主窗口
│   │   └── MainWindow.xaml
│   ├── App.xaml                         # 应用定义
│   ├── Program.cs                       # 程序入口
│   └── Package.appxmanifest             # MSIX 清单
│
├── BluetoothAudioReceiver.Core/         # 核心业务逻辑层
│   ├── Contracts/Services/              # 服务接口
│   ├── Extensions/                      # 扩展方法
│   ├── Helpers/                         # 辅助类（背景、主题、标题栏等）
│   ├── Models/                          # 领域模型
│   │   ├── BluetoothAudioDevice.cs      # 蓝牙音频设备模型
│   │   ├── BackdropType.cs              # 背景类型枚举
│   │   ├── DisplayMonitor.cs            # 显示器信息
│   │   └── MonitorInfo.cs               # 显示器详细数据
│   └── Services/                        # 核心服务
│       ├── DialogService.cs             # 对话框服务
│       └── LocalSettingsService.cs      # 本地设置存储
│
├── BluetoothAudioReceiver.Infrastructure/  # 基础设施层
│   ├── Contracts/Services/              # 服务接口
│   ├── Helpers/                         # 辅助工具
│   │   ├── ExceptionFormatter.cs        # 异常格式化
│   │   ├── JsonHelper.cs                # JSON 序列化
│   │   ├── RuntimeHelper.cs             # 运行时检测
│   │   └── StartupHelper.cs             # 开机启动管理
│   ├── Services/                        # 基础设施服务
│   │   └── FileService.cs               # 文件读写
│   └── Constants.cs                     # 常量定义
│
├── .github/workflows/                   # GitHub Actions CI/CD
└── LICENSE                              # MIT 许可证
```

## 技术栈

- **.NET 9** + **Windows App SDK 1.8**
- **WinUI 3** - 现代 Windows 原生 UI 框架
- **CommunityToolkit.Mvvm** - MVVM 架构
- **CommunityToolkit.WinUI** - UI 控件与扩展
- **WinUIEx** - 窗口管理与扩展
- **Serilog** - 结构化日志
- **H.NotifyIcon** - 系统托盘图标
- **CsWin32** - 原生 Win32 API 调用

## 系统要求

- Windows 10 版本 19041 (20H1) 或更高版本
- 支持蓝牙的 Windows 设备
- 目标蓝牙音频设备（如蓝牙音箱、耳机等）

## 构建

### 前置条件

- Visual Studio 2022（推荐安装 ".NET 桌面开发" 工作负载）
- Windows App SDK

### 编译运行

```bash
# 克隆仓库
git clone https://github.com/kk5568/BluetoothAudioReceiver.git

# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行（非打包模式）
dotnet run --project BluetoothAudioReceiver/BluetoothAudioReceiver.csproj
```

## 引用说明

本项目基于以下开源项目构建，在此表示感谢：

项目地址：[https://github.com/ysc3839/AudioPlaybackConnector](https://github.com/ysc3839/AudioPlaybackConnector)

本项目的蓝牙音频功能参考了 ysc3839 的 AudioPlaybackConnector（C++ 实现），使用 C# 重新实现了核心逻辑。来自该项目的代码/设计包括：

- **蓝牙音频服务接口** - [IAudioPlaybackConnectionService.cs](file:///c:/Users/rog/OneDrive/文档/1LLL_Files/BluetoothAudioReceiver/BluetoothAudioReceiver.Core/Contracts/Services/IAudioPlaybackConnectionService.cs)
- **蓝牙音频连接服务** - [AudioPlaybackConnectionService.cs](file:///c:/Users/rog/OneDrive/文档/1LLL_Files/BluetoothAudioReceiver/BluetoothAudioReceiver/Services/AudioPlaybackConnectionService.cs)（使用 `Windows.Media.Audio.AudioPlaybackConnection` API 实现）
- **设备模型** - [BluetoothAudioDevice.cs](file:///c:/Users/rog/OneDrive/文档/1LLL_Files/BluetoothAudioReceiver/BluetoothAudioReceiver.Core/Models/BluetoothAudioDevice.cs)
- **设备列表页面** - [BluetoothPage.xaml](file:///c:/Users/rog/OneDrive/文档/1LLL_Files/BluetoothAudioReceiver/BluetoothAudioReceiver/Views/Pages/BluetoothPage.xaml) 及 [BluetoothPageViewModel.cs](file:///c:/Users/rog/OneDrive/文档/1LLL_Files/BluetoothAudioReceiver/BluetoothAudioReceiver/ViewModels/Pages/BluetoothPageViewModel.cs)
- **自动重连** - 启动时自动重连上次连接的设备

## 参考

- [WinUI 3](https://docs.microsoft.com/windows/apps/winui/)
- [Windows App SDK](https://docs.microsoft.com/windows/apps/windows-app-sdk/)
- [CommunityToolkit.WinUI](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [WinUIEx](https://github.com/dotMorten/WinUIEx)
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

## 许可证

[MIT License](LICENSE)

Copyright © 2025 [kk5568](https://github.com/kk5568)
