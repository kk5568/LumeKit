using System;
using System.Linq;
using System.Threading.Tasks;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Services.Monitor;

namespace BluetoothAudioReceiver.Services
{
    public class BrightnessController : IBrightnessService
    {
        private const int BrightnessDeadZone = 3;
        private byte _currentBrightness = 50;
        private readonly MonitorManager _monitorManager;
        private readonly WmiBrightnessService _wmiBrightnessService;
        private readonly InternalPanelBrightnessService _internalPanelBrightnessService;
        private bool _isInitialized;

        public BrightnessController(
            MonitorManager monitorManager,
            WmiBrightnessService wmiBrightnessService,
            InternalPanelBrightnessService internalPanelBrightnessService)
        {
            _monitorManager = monitorManager;
            _wmiBrightnessService = wmiBrightnessService;
            _internalPanelBrightnessService = internalPanelBrightnessService;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            await _monitorManager.InitializeAsync();
            await RefreshCurrentStateAsync();
            _isInitialized = true;
        }

        public async Task AdjustBrightnessAsync(byte target)
        {
            await InitializeAsync();
            await RefreshCurrentStateAsync();

            if (Math.Abs(target - _currentBrightness) < BrightnessDeadZone)
            {
                return;
            }

            var wmiTask = _wmiBrightnessService.SetBrightnessAsync(target);
            var internalPanelTask = _internalPanelBrightnessService.TrySetBrightnessAsync(target);

            if (_monitorManager.Monitors.Count == 0)
            {
                _currentBrightness = target;
            }

            await _monitorManager.SetAllMonitorsBrightnessAsync(target);
            await wmiTask;
            await internalPanelTask;
            _currentBrightness = target;
        }

        public async Task<byte> GetCurrentBrightnessAsync()
        {
            await InitializeAsync();
            await RefreshCurrentStateAsync();
            return _currentBrightness;
        }

        private async Task RefreshCurrentStateAsync()
        {
            await _monitorManager.UpdateAllMonitorsAsync();

            var firstMonitor = _monitorManager.Monitors.FirstOrDefault();
            if (firstMonitor != null)
            {
                if (firstMonitor.Brightness >= 0)
                {
                    _currentBrightness = (byte)Math.Clamp(firstMonitor.Brightness, 0, 100);
                }
            }
            else
            {
                var currentBrightness = await _wmiBrightnessService.GetCurrentBrightnessAsync();
                if (currentBrightness.HasValue)
                {
                    _currentBrightness = currentBrightness.Value;
                    return;
                }

                var internalPanelBrightness = await _internalPanelBrightnessService.TryGetBrightnessAsync();
                if (internalPanelBrightness.HasValue)
                {
                    _currentBrightness = internalPanelBrightness.Value;
                }
            }
        }
    }
}
