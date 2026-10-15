// 占位等价类：DeskBox 里这两个服务分别在
//   引用项目\DeskBox-main\src\DeskBox\Services\PerformanceLogger.cs（604 行）
//   引用项目\DeskBox-main\src\DeskBox\Services\WidgetCompactAnimationCoordinator.cs（756 行）
// 照搬来的动画文件调用它们的地方只有下面这几个成员。浮窗只有一个，不共享帧时钟、
// 不需要合成器时钟提速 / 帧健康策略 / 性能日志面板，所以这里只保留同签名的等价实现，
// 让照搬来的文件一字不改。
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

internal static class PerformanceLogger
{
    // DeskBox 的 Mark 受设置项开关控制；LumeKit 浮窗不做性能日志，直接丢弃。
    public static void Mark(string operation, string? details = null)
    {
    }

    public static void RecordOwnedCompositionResourceCreated()
    {
    }

    public static void RecordOwnedCompositionResourceReleased()
    {
    }
}

internal static class WidgetCompactAnimationCoordinator
{
    private static readonly object Gate = new();
    private static readonly List<Action> FrameCallbacks = new();
    private static bool _isSubscribed;

    public static IDisposable Register(Action frameCallback, Func<double> frameBudgetMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(frameCallback);
        ArgumentNullException.ThrowIfNull(frameBudgetMilliseconds);

        lock (Gate)
        {
            FrameCallbacks.Add(frameCallback);
            if (!_isSubscribed)
            {
                _isSubscribed = true;
                CompositionTarget.Rendering += OnRenderingFrame;
            }
        }

        return new FrameRegistration(frameCallback);
    }

    public static double GetFrameBudgetMillisecondsForPoint(int screenX, int screenY) =>
        WidgetAnimationDisplayTiming.GetFrameBudgetMillisecondsForPoint(screenX, screenY);

    private static void OnRenderingFrame(object? sender, object e)
    {
        Action[] snapshot;
        lock (Gate)
        {
            snapshot = FrameCallbacks.ToArray();
        }

        foreach (Action callback in snapshot)
        {
            callback();
        }
    }

    private sealed class FrameRegistration : IDisposable
    {
        private Action? _callback;

        public FrameRegistration(Action callback)
        {
            _callback = callback;
        }

        public void Dispose()
        {
            Action? callback = _callback;
            if (callback is null)
            {
                return;
            }

            _callback = null;
            lock (Gate)
            {
                FrameCallbacks.Remove(callback);
                if (FrameCallbacks.Count == 0 && _isSubscribed)
                {
                    _isSubscribed = false;
                    CompositionTarget.Rendering -= OnRenderingFrame;
                }
            }
        }
    }
}
