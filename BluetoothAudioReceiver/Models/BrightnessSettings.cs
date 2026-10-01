using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Models
{
    public class BrightnessSettings : ObservableObject
    {
        private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(BrightnessSettings));
        private readonly ILocalSettingsService _localSettingsService;
        private readonly LocalSettingsKeys _localSettingsKeys;

        public BrightnessSettings(ILocalSettingsService localSettingsService, LocalSettingsKeys localSettingsKeys)
        {
            _localSettingsService = localSettingsService;
            _localSettingsKeys = localSettingsKeys;

            _autoEnabled = GetBool(_localSettingsKeys.AutoBrightnessEnabledKey, true);
            _log.Information("Auto brightness master switch loaded. Enabled={Enabled}, SettingsPath={SettingsPath}",
                _autoEnabled,
                LocalSettingsHelper.ApplicationDataPath);
            _minBrightness = GetByte(_localSettingsKeys.MinBrightnessKey, 30);
            _maxBrightness = GetByte(_localSettingsKeys.MaxBrightnessKey, 100);
            _minColorTemp = GetDouble(_localSettingsKeys.MinColorTempKey, 4500);
            _maxColorTemp = GetDouble(_localSettingsKeys.MaxColorTempKey, 8500);
        }

        private bool _autoEnabled;
        public bool AutoEnabled
        {
            get => _autoEnabled;
            set
            {
                if (SetProperty(ref _autoEnabled, value))
                {
                    _ = SaveAutoEnabledAsync(value, "property");
                }
            }
        }

        public async Task SetAutoEnabledAsync(bool value, string source)
        {
            if (!SetProperty(ref _autoEnabled, value))
            {
                _log.Information("Auto brightness master switch unchanged. Source={Source}, Enabled={Enabled}",
                    source, value);
                return;
            }

            _log.Information("Auto brightness master switch changing. Source={Source}, Enabled={Enabled}",
                source, value);
            await SaveAutoEnabledAsync(value, source);
        }

        private async Task SaveAutoEnabledAsync(bool value, string source)
        {
            try
            {
                await _localSettingsService.SaveSettingAsync(_localSettingsKeys.AutoBrightnessEnabledKey, value);
                var savedValue = _localSettingsService.ReadSetting<string>(_localSettingsKeys.AutoBrightnessEnabledKey);
                _log.Information(
                    "Auto brightness master switch saved. Source={Source}, Requested={Requested}, Stored={Stored}, SettingsPath={SettingsPath}",
                    source,
                    value,
                    savedValue,
                    LocalSettingsHelper.ApplicationDataPath);
            }
            catch (Exception ex)
            {
                _log.Error(ex,
                    "Auto brightness master switch save failed. Source={Source}, Requested={Requested}, SettingsPath={SettingsPath}",
                    source,
                    value,
                    LocalSettingsHelper.ApplicationDataPath);
            }
        }

        private byte _minBrightness;
        public byte MinBrightness
        {
            get => _minBrightness;
            set
            {
                if (SetProperty(ref _minBrightness, value))
                {
                    _ = _localSettingsService.SaveSettingAsync(_localSettingsKeys.MinBrightnessKey, value);
                }
            }
        }

        private byte _maxBrightness;
        public byte MaxBrightness
        {
            get => _maxBrightness;
            set
            {
                if (SetProperty(ref _maxBrightness, value))
                {
                    _ = _localSettingsService.SaveSettingAsync(_localSettingsKeys.MaxBrightnessKey, value);
                }
            }
        }

        private double _minColorTemp;
        public double MinColorTemp
        {
            get => _minColorTemp;
            set
            {
                if (SetProperty(ref _minColorTemp, value))
                {
                    _ = _localSettingsService.SaveSettingAsync(_localSettingsKeys.MinColorTempKey, value);
                }
            }
        }

        private double _maxColorTemp;
        public double MaxColorTemp
        {
            get => _maxColorTemp;
            set
            {
                if (SetProperty(ref _maxColorTemp, value))
                {
                    _ = _localSettingsService.SaveSettingAsync(_localSettingsKeys.MaxColorTempKey, value);
                }
            }
        }

        private bool GetBool(string key, bool fallback)
        {
            var raw = _localSettingsService.ReadSetting<string>(key);
            return bool.TryParse(raw, out var value) ? value : fallback;
        }

        private byte GetByte(string key, byte fallback)
        {
            var raw = _localSettingsService.ReadSetting<string>(key);
            return byte.TryParse(raw, out var value) ? value : fallback;
        }

        private double GetDouble(string key, double fallback)
        {
            var raw = _localSettingsService.ReadSetting<string>(key);
            return double.TryParse(raw, out var value) ? value : fallback;
        }
    }
}
