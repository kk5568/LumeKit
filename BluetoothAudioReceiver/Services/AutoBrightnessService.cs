using System;
using System.Threading.Tasks;
using BluetoothAudioReceiver.Models;

namespace BluetoothAudioReceiver.Services
{
    public class AutoBrightnessService
    {
        private readonly CameraReaderService _cameraReader;
        private readonly BrightnessController _controller;
        private readonly BrightnessSettings _settings;
        private bool _userOverride = false;
        private DateTime _lastUserAction = DateTime.MinValue;
        private readonly TimeSpan UserOverrideTimeout = TimeSpan.FromMinutes(30);

        public AutoBrightnessService(CameraReaderService cameraReader, BrightnessController controller, BrightnessSettings settings)
        {
            _cameraReader = cameraReader;
            _controller = controller;
            _settings = settings;
        }

        public void OnUserAction()
        {
            _userOverride = true;
            _lastUserAction = DateTime.Now;
        }

        public void OnScreenUnlocked()
        {
            _userOverride = false;
        }

        public async Task UpdateBrightnessIfNeededAsync()
        {
            if (!_settings.AutoEnabled) return;

            if (_userOverride)
            {
                if (DateTime.Now - _lastUserAction > UserOverrideTimeout)
                {
                    _userOverride = false;
                }
                else
                {
                    return;
                }
            }

            var (envBrightness, envTemp) = await _cameraReader.GetAverageBrightnessAndColorTempAsync();

            double brightnessRatio = Math.Clamp(envBrightness / 255.0, 0.0, 1.0);
            int targetBrightnessInt = (int)(_settings.MinBrightness + brightnessRatio * (_settings.MaxBrightness - _settings.MinBrightness));
            byte targetBrightness = (byte)Math.Clamp(targetBrightnessInt, 0, 100);

            double targetColorTemp = envTemp + _settings.ColorTempBias * 500;
            targetColorTemp = Math.Clamp(targetColorTemp, _settings.MinColorTemp, _settings.MaxColorTemp);

            await _controller.AdjustBrightnessAsync(targetBrightness);
            await _controller.AdjustColorTempAsync(targetColorTemp);
        }

        public void ResetUserOverride() => _userOverride = false;

        public bool IsUserOverride => _userOverride;
    }
}
