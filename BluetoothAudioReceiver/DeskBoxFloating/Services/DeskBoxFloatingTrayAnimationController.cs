using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

public sealed record DeskBoxFloatingTrayAnimationProfile(
    double ShowOffsetX,
    double ShowOffsetY,
    double HideOffsetX,
    double HideOffsetY,
    float ShowStartOpacity,
    float HideEndOpacity,
    float ShowStartScale,
    float HideEndScale,
    int DurationMs,
    bool IsEnabled);

internal sealed class DeskBoxFloatingTrayAnimationController
{
    public const float RestingOpacity = 1.0f;
    public const float SoftOpacity = 0.0f;
    public const float RestingScale = 1.0f;
    public const float SoftScale = 0.985f;

    private readonly UIElement _rootElement;

    internal DeskBoxFloatingTrayAnimationController(UIElement rootElement)
    {
        _rootElement = rootElement;
        ElementCompositionPreview.SetIsTranslationEnabled(_rootElement, true);
    }

    public long Generation { get; private set; }

    public long NextGeneration() => ++Generation;

    public DeskBoxFloatingTrayAnimationProfile CreateSlideUpFadeProfile() =>
        new(
            ShowOffsetX: 0,
            ShowOffsetY: 10,
            HideOffsetX: 0,
            HideOffsetY: 7,
            ShowStartOpacity: SoftOpacity,
            HideEndOpacity: SoftOpacity,
            ShowStartScale: RestingScale,
            HideEndScale: RestingScale,
            DurationMs: 210,
            IsEnabled: true);

    public void PrepareHiddenState(DeskBoxFloatingTrayAnimationProfile profile)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_rootElement);
        StopVisualAnimations();
        visual.Offset = new Vector3((float)profile.ShowOffsetX, (float)profile.ShowOffsetY, 0);
        visual.Opacity = profile.ShowStartOpacity;
        visual.Scale = new Vector3(profile.ShowStartScale, profile.ShowStartScale, 1);
        visual.CenterPoint = GetVisualCenterPoint();
    }

    public void Animate(
        DeskBoxFloatingTrayAnimationProfile profile,
        bool isShowing,
        long generation,
        Action completed)
    {
        StopVisualAnimations();

        var visual = ElementCompositionPreview.GetElementVisual(_rootElement);
        var compositor = visual.Compositor;
        visual.CenterPoint = GetVisualCenterPoint();

        var easing = compositor.CreateCubicBezierEasingFunction(
            isShowing
                ? new Vector2(0.16f, 1f)
                : new Vector2(0.4f, 0f),
            isShowing
                ? new Vector2(0.3f, 1f)
                : new Vector2(0.6f, 1f));

        var duration = TimeSpan.FromMilliseconds(
            isShowing ? profile.DurationMs : Math.Min(profile.DurationMs, 150));

        var fromOffset = isShowing
            ? new Vector3((float)profile.ShowOffsetX, (float)profile.ShowOffsetY, 0)
            : Vector3.Zero;
        var toOffset = isShowing
            ? Vector3.Zero
            : new Vector3((float)profile.HideOffsetX, (float)profile.HideOffsetY, 0);
        var fromOpacity = isShowing ? profile.ShowStartOpacity : RestingOpacity;
        var toOpacity = isShowing ? RestingOpacity : profile.HideEndOpacity;
        var fromScale = isShowing ? profile.ShowStartScale : RestingScale;
        var toScale = isShowing ? RestingScale : profile.HideEndScale;

        visual.Offset = fromOffset;
        visual.Opacity = fromOpacity;
        visual.Scale = new Vector3(fromScale, fromScale, 1);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = duration;
        offsetAnimation.InsertKeyFrame(0, fromOffset);
        offsetAnimation.InsertKeyFrame(1, toOffset, easing);

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = duration;
        opacityAnimation.InsertKeyFrame(0, fromOpacity);
        opacityAnimation.InsertKeyFrame(1, toOpacity, easing);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = duration;
        scaleAnimation.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1));
        scaleAnimation.InsertKeyFrame(1, new Vector3(toScale, toScale, 1), easing);

        visual.StartAnimation("Offset", offsetAnimation);
        visual.StartAnimation("Opacity", opacityAnimation);
        visual.StartAnimation("Scale", scaleAnimation);

        _ = CompleteAfterAsync(duration, generation, completed);
    }

    public void Reset()
    {
        StopVisualAnimations();
        var visual = ElementCompositionPreview.GetElementVisual(_rootElement);
        visual.Offset = Vector3.Zero;
        visual.Opacity = RestingOpacity;
        visual.Scale = new Vector3(RestingScale, RestingScale, 1);
    }

    private async Task CompleteAfterAsync(
        TimeSpan duration,
        long generation,
        Action completed)
    {
        await Task.Delay(duration);
        if (generation == Generation)
        {
            completed();
        }
    }

    private void StopVisualAnimations()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_rootElement);
        visual.StopAnimation("Offset");
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
    }

    private Vector3 GetVisualCenterPoint()
    {
        if (_rootElement is FrameworkElement element)
        {
            return new Vector3(
                (float)(element.ActualWidth / 2),
                (float)(element.ActualHeight / 2),
                0);
        }

        return Vector3.Zero;
    }
}
