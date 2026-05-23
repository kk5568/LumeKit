using System;
using System.Threading.Tasks;
using Windows.Graphics.Display;
using BluetoothAudioReceiver.Services.Monitor;

namespace BluetoothAudioReceiver.Services
{
    public class BrightnessController
    {
        private const int DeadZone = 3;
        private const double ColorTempDeadZone = 100;
        private byte _currentBrightness;
        private double _currentColorTemp;
        private readonly MonitorManager _monitorManager;

        public BrightnessController(MonitorManager monitorManager)
        {
            _monitorManager = monitorManager;
            _currentBrightness = GetCurrentBrightness();
            _currentColorTemp = GetcurrentColorTemp();
        }

        public async Task AdjustBrightnessAsync(byte target)
        {
            if (Math.Abs(target - _currentBrightness) < DeadZone) return;

            byte step = (byte)(target > _currentBrightness ? 1 : -1);
            while (_currentBrightness != target)
            {
                _currentBrightness = (byte)(_currentBrightness + step);
                SetScreenBrightness(_currentBrightness);
                await Task.Delay(20);
                
                if (step > 0 && _currentBrightness >= target)
                    break;
                else if (step < 0 && _currentBrightness <= target)
                    break;
            }
        }

        public async Task AdjustColorTempAsync(double target)
        {
            if (Math.Abs(target - _currentColorTemp) < ColorTempDeadZone) return;

            double step = target > _currentColorTemp ? 50 : -50;
            while (Math.Abs(target - _currentColorTemp) > ColorTempDeadZone)
            {
                _currentColorTemp += step;
                SetColorTemperature(_currentColorTemp);
                await Task.Delay(20);
            }
        }

        private byte GetCurrentBrightness()
        {
            try
            {
                var display = DisplayInformation.GetForCurrentView();
                return 100;
            }
            catch
            {
                return 100;
            }
        }

        private double GetcurrentColorTemp()
        {
            try
            {
                return 5000;
            }
            catch
            {
                return 5000;
            }
        }

        private void SetScreenBrightness(byte value)
        {
            try
            {
                var display = DisplayInformation.GetForCurrentView();
            }
            catch
            {
            }
        }

        private void SetColorTemperature(double value)
        {
            try
            {
            }
            catch
            {
            }
        }
    }
}
