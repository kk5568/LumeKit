# FlyoutModule 使用说明

4 个文件，零第三方依赖，放进你的 WPF 项目即可（.NET 6+，因为用了 `VisualTreeHelper.GetDpi`）。

## 文件清单

| 文件 | 作用 |
|---|---|
| `NativeMethods.cs` | 精简版 Win32 P/Invoke（置顶、不抢焦点、显示器、键盘钩子） |
| `FlyoutWindow.cs` | **核心**：可复用浮窗基类（滑入/淡出 + 超时自动隐藏 + 单窗口复用） |
| `LowLevelKeyboardHook.cs` | 全局键盘钩子（可选，浮窗本身不需要它） |
| `AcrylicBlur.cs` | 亚克力模糊背景（可选） |

## 用法（以大写键弹窗为例）

### 1. 做一个浮窗，继承 FlyoutWindow

XAML（`CapsFlyoutWindow.xaml`）：

```xml
<flyout:FlyoutWindow x:Class="YourApp.CapsFlyoutWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:flyout="clr-namespace:FlyoutModule;assembly=FlyoutModule"
    Height="50" Width="160"
    Background="#CC202020"      <!-- 半透明底色，配合亚克力用 -->
    WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False">
    <TextBlock x:Name="StatusText" Text="Caps Lock 开启"
               Foreground="White" FontSize="14"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>
</flyout:FlyoutWindow>
```

代码（`CapsFlyoutWindow.xaml.cs`）：

```csharp
public partial class CapsFlyoutWindow : FlyoutWindow
{
    public CapsFlyoutWindow()
    {
        DisplayDuration = TimeSpan.FromSeconds(3);  // 停留 3 秒
        AnimationDurationMs = 300;                  // 动画 300ms
        BottomMargin = 16;                          // 距底边 16
        InitializeComponent();
    }

    public void ShowCapsState(bool isOn)
    {
        StatusText.Text = isOn ? "Caps Lock 开启" : "Caps Lock 关闭";
        ShowFlyout();   // 复用窗口 + 重置倒计时，核心就这一行
    }
}
```

### 2. 用键盘钩子触发（App 启动时）

```csharp
public partial class App : Application
{
    private readonly LowLevelKeyboardHook _hook = new();
    private CapsFlyoutWindow? _flyout;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _hook.KeyAction += (vkCode, isKeyUp) =>
        {
            if (isKeyUp && vkCode == Vk.CapsLock)
            {
                Dispatcher.Invoke(() =>
                {
                    _flyout ??= new CapsFlyoutWindow();
                    _flyout.ShowCapsState(Keyboard.IsKeyToggled(Key.CapsLock));
                });
            }
        };
        _hook.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook.Stop();
        base.OnExit(e);
    }
}
```

### 3. 亚克力背景（可选）

在 `ShowCapsState` 或构造函数里加一行：

```csharp
AcrylicBlur.Enable(this, opacity: 175);
```

注意：要窗口背景半透明（如 `#CC202020`）才能看到效果。

## 替代方案提示

如果嫌自己维护麻烦，可以考虑直接用现成库：
- **[WPF UI (lepo.co)](https://github.com/lepoco/wpfui)** — 有现成的 `Flyout`、`Snackbar`、Mica/亚克力支持，FluentFlyout 自己也在用它做图标和通知。
- **[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)** — 托盘 + 弹窗通知。
- 只想要任务栏角落提示的话，Win11 的 `ToastNotification` / `AppSDK` 也能做，但样式不可控。

FluentFlyout 的浮窗之所以全自己写，就是为了完全控制动画、位置和"不抢焦点"这几个系统通知做不到的
行为。如果你也需要这种完全控制，本模块就是最小可复用的那一套。
