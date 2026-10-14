using Serilog;

namespace BluetoothAudioReceiver;

/// <summary>
/// DeskBox App.Log / App.LogVerbose 的落点：DeskBox 材质源码里带这两个诊断调用
/// (WidgetMaterialSystemBackdrop、WindowsCompatibilityService)，LumeKit 这边接到已有的
/// Serilog 记录器 (App.xaml.cs 的 _log) 上，不引入第二套日志。
/// </summary>
public partial class App
{
    public static void Log(string msg) => _log.Information("{DeskBoxMaterial}", msg);

    public static void LogVerbose(string msg) => _log.Debug("{DeskBoxMaterial}", msg);
}
