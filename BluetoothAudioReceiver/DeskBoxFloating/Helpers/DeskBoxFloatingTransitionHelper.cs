using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.DeskBoxFloating.Services;

namespace BluetoothAudioReceiver.DeskBoxFloating.Helpers;

internal sealed class DeskBoxFloatingTransitionHelper
{
    private readonly DeskBoxFloatingTrayAnimationController _controller;
    private readonly DeskBoxFloatingTrayAnimationProfile _profile;

    internal DeskBoxFloatingTransitionHelper(UIElement element)
    {
        _controller = new DeskBoxFloatingTrayAnimationController(element);
        _profile = _controller.CreateSlideUpFadeProfile();
    }

    internal void PlayEnter()
    {
        long generation = _controller.NextGeneration();
        _controller.PrepareHiddenState(_profile);
        _controller.Animate(_profile, isShowing: true, generation, completed: () => { });
    }

    internal Task PlayExitAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long generation = _controller.NextGeneration();
        _controller.Animate(_profile, isShowing: false, generation, completed: () => completion.TrySetResult());
        return completion.Task;
    }

    internal void Reset()
    {
        _controller.Reset();
    }
}
