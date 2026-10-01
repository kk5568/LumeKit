using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Services;

public sealed class ScreenGammaColorTemperatureController : IDisposable
{
    private const double DeadZoneKelvin = 300;
    private const double NeutralKelvin = 6500;
    private const int RampSize = 256;
    private const int TransitionDurationMs = 120;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ushort[] _identityRamp = BuildIdentityRamp();
    private ushort[]? _originalRamp;
    private ushort[]? _lastAppliedRamp;
    private double _currentKelvin = NeutralKelvin;
    private bool _originalRampCaptured;
    private bool _disposed;

    public ScreenGammaColorTemperatureController()
    {
        EnsureOriginalRampCaptured();
    }

    public async Task AdjustColorTemperatureAsync(double targetKelvin)
    {
        ThrowIfDisposed();

        targetKelvin = NormalizeKelvin(targetKelvin);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var externallyChanged = HasAppliedRampChangedExternally();
            if (!externallyChanged && Math.Abs(targetKelvin - _currentKelvin) < DeadZoneKelvin)
            {
                return;
            }

            var startKelvin = _currentKelvin;
            var delta = Math.Abs(targetKelvin - startKelvin);
            var steps = Math.Clamp((int)Math.Ceiling(delta / 180.0), 4, 10);
            var delayMs = Math.Max(6, TransitionDurationMs / steps);

            for (var index = 1; index <= steps; index++)
            {
                var progress = index / (double)steps;
                var eased = EaseOutCubic(progress);
                var intermediateKelvin = startKelvin + (targetKelvin - startKelvin) * eased;

                if (!ApplyGammaRamp(intermediateKelvin))
                {
                    break;
                }

                _currentKelvin = intermediateKelvin;

                if (index < steps)
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }

            ApplyGammaRamp(targetKelvin);
            _currentKelvin = targetKelvin;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Restore()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Wait();
        try
        {
            if (_originalRampCaptured && _originalRamp is not null)
            {
                if (SetGammaRamp(_originalRamp))
                {
                    _lastAppliedRamp = null;
                    _currentKelvin = NeutralKelvin;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Restore();
        _gate.Dispose();
        _disposed = true;
    }

    private bool ApplyGammaRamp(double kelvin)
    {
        var ramp = BuildGammaRamp(kelvin);
        var applied = SetGammaRamp(ramp);
        if (applied)
        {
            _lastAppliedRamp = (ushort[])ramp.Clone();
        }

        return applied;
    }

    private bool HasAppliedRampChangedExternally()
    {
        if (_lastAppliedRamp is null)
        {
            return false;
        }

        var currentRamp = GetCurrentGammaRamp();
        return currentRamp is not null && !AreRampsEquivalent(currentRamp, _lastAppliedRamp);
    }

    private bool SetGammaRamp(ushort[] ramp)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
            try
            {
                return SetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private ushort[] BuildGammaRamp(double kelvin)
    {
        var (redScale, greenScale, blueScale) = GetColorTemperatureScale(kelvin);
        var ramp = new ushort[RampSize * 3];

        for (var i = 0; i < RampSize; i++)
        {
            var value = i / 255.0;

            ramp[i] = ToRampWord(value * redScale);
            ramp[i + RampSize] = ToRampWord(value * greenScale);
            ramp[i + RampSize * 2] = ToRampWord(value * blueScale);
        }

        return ramp;
    }

    private static ushort ToRampWord(double value)
    {
        value = Math.Clamp(value, 0.0, 1.0);
        return (ushort)Math.Round(value * ushort.MaxValue);
    }

    private static double NormalizeKelvin(double kelvin)
    {
        if (double.IsNaN(kelvin) || double.IsInfinity(kelvin))
        {
            return NeutralKelvin;
        }

        return Math.Max(kelvin, 1);
    }

    private static double EaseOutCubic(double value)
    {
        var t = 1 - Math.Clamp(value, 0.0, 1.0);
        return 1 - t * t * t;
    }

    private static ushort[] BuildIdentityRamp()
    {
        var ramp = new ushort[RampSize * 3];
        for (var i = 0; i < RampSize; i++)
        {
            var value = (ushort)(i * 257);
            ramp[i] = value;
            ramp[i + RampSize] = value;
            ramp[i + RampSize * 2] = value;
        }

        return ramp;
    }

    private static bool AreRampsEquivalent(ushort[] left, ushort[] right)
    {
        const int tolerance = 64;

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (Math.Abs(left[i] - right[i]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private (double red, double green, double blue) GetColorTemperatureScale(double kelvin)
    {
        var rgb = KelvinToRgb(kelvin);
        var neutral = KelvinToRgb(NeutralKelvin);

        var red = neutral.red <= 0 ? 1.0 : rgb.red / neutral.red;
        var green = neutral.green <= 0 ? 1.0 : rgb.green / neutral.green;
        var blue = neutral.blue <= 0 ? 1.0 : rgb.blue / neutral.blue;

        return (
            red: Math.Clamp(red, 0.0, 1.0),
            green: Math.Clamp(green, 0.0, 1.0),
            blue: Math.Clamp(blue, 0.0, 1.0));
    }

    private static (double red, double green, double blue) KelvinToRgb(double kelvin)
    {
        var temperature = Math.Max(kelvin, 1) / 100.0;
        double red;
        double green;
        double blue;

        if (temperature <= 66)
        {
            red = 255;
            green = 99.4708025861 * Math.Log(temperature) - 161.1195681661;

            blue = temperature <= 19
                ? 0
                : 138.5177312231 * Math.Log(temperature - 10) - 305.0447927307;
        }
        else
        {
            red = 329.698727446 * Math.Pow(temperature - 60, -0.1332047592);
            green = 288.1221695283 * Math.Pow(temperature - 60, -0.0755148492);
            blue = 255;
        }

        return (
            red: Math.Clamp(red, 0, 255),
            green: Math.Clamp(green, 0, 255),
            blue: Math.Clamp(blue, 0, 255));
    }

    private void EnsureOriginalRampCaptured()
    {
        if (_originalRampCaptured)
        {
            return;
        }

        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            _originalRamp = _identityRamp;
            _originalRampCaptured = true;
            return;
        }

        try
        {
            var ramp = new ushort[RampSize * 3];
            var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
            try
            {
                if (GetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject()))
                {
                    _originalRamp = ramp;
                }
                else
                {
                    _originalRamp = _identityRamp;
                }
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
            _originalRampCaptured = true;
        }
    }

    private ushort[]? GetCurrentGammaRamp()
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var ramp = new ushort[RampSize * 3];
            var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
            try
            {
                return GetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject()) ? ramp : null;
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ScreenGammaColorTemperatureController));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, IntPtr lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, IntPtr lpRamp);
}
