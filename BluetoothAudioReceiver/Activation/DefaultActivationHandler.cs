using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Activation;

internal class DefaultActivationHandler(INavigationService navigationService) : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService = navigationService;

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        // 带参数的启动（协议/设备激活）仍落到音频管理页，普通启动默认打开主页。
        if (string.IsNullOrEmpty(args.Arguments))
        {
            await _navigationService.NavigateToAsync(typeof(HomePageViewModel).FullName!);
            return;
        }

        await _navigationService.NavigateToAsync(typeof(BluetoothPageViewModel).FullName!, args.Arguments);
    }
}
