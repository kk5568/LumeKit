using Microsoft.UI.Windowing;

namespace BluetoothAudioReceiver.DeskBoxFloating.Helpers;

/// <summary>
/// DeskBox 原项目 WindowShellState 的 LumeKit 独立迁移版。
/// 这里只负责窗口外壳状态，不处理通知业务。
/// </summary>
internal static class DeskBoxWindowShellState
{
    public static void TryHideFromSwitchers(AppWindow appWindow)
    {
        try
        {
            appWindow.IsShownInSwitchers = false;
        }
        catch
        {
        }
    }

    public static void TryApplyBorderlessOverlappedPresenter(AppWindow appWindow)
    {
        try
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }
        catch
        {
        }
    }
}
