using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using Windows.Storage;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Infrastructure;
using BluetoothAudioReceiver.Infrastructure.Contracts.Services;
using BluetoothAudioReceiver.Infrastructure.Helpers;

namespace BluetoothAudioReceiver.Core.Services;

// For MSIX package:
// Settings saved in C:\Users\<UserName>\AppData\Local\Packages\<PackageFamilyName>\Settings\settings.dat
// File saved in C:\Users\<UserName>\AppData\Local\Packages\<PackageFamilyName>\LocalState\{FileName}
public class LocalSettingsService : ILocalSettingsService
{
    private readonly IFileService _fileService;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _settingsLock = new();

    private readonly string _localSettingsPath;

    private readonly string _localsettingsFile;

    private Dictionary<string, object>? _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService)
    {
        _fileService = fileService;

        _localsettingsFile = Constants.SettingsFile;

        _localSettingsPath = Path.Combine(LocalSettingsHelper.ApplicationDataPath, Constants.SettingsFolder);

        if (!Directory.Exists(_localSettingsPath))
        {
            Directory.CreateDirectory(_localSettingsPath);
        }
    }

    #region App Settings

    public T? ReadSetting<T>(string key)
    {
        return ReadSetting(key, default(T));
    }

    public T? ReadSetting<T>(string key, T defaultValue)
    {
        if (RuntimeHelper.IsMSIX)
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj))
            {
                return DeserializeSettingValue(obj, defaultValue);
            }
        }
        else
        {
            lock (_settingsLock)
            {
                InitializeSettings();

                if (_settings != null && _settings.TryGetValue(key, out var obj))
                {
                    return DeserializeSettingValue(obj, defaultValue);
                }
            }
        }

        return defaultValue;
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        return await ReadSettingAsync(key, default(T));
    }

    public async Task<T?> ReadSettingAsync<T>(string key, T defaultValue)
    {
        if (RuntimeHelper.IsMSIX)
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj))
            {
                return await Task.FromResult(DeserializeSettingValue(obj, defaultValue));
            }
        }
        else
        {
            await InitializeSettingsAsync();

            lock (_settingsLock)
            {
                if (_settings != null && _settings.TryGetValue(key, out var obj))
                {
                    return DeserializeSettingValue(obj, defaultValue);
                }
            }
        }

        return defaultValue;
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
        var stringValue = JsonHelper.Stringify(value!);

        if (RuntimeHelper.IsMSIX)
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var obj) &&
                JsonHelper.ConvertToString(obj) == stringValue)
            {
                return;
            }

            ApplicationData.Current.LocalSettings.Values[key] = stringValue;
        }
        else
        {
            await _saveGate.WaitAsync();
            try
            {
                await InitializeSettingsAsync();

                lock (_settingsLock)
                {
                    if (_settings != null && _settings.TryGetValue(key, out var obj) &&
                        JsonHelper.ConvertToString(obj) == stringValue)
                    {
                        return;
                    }

                    _settings![key] = stringValue;
                }

                var saved = await _fileService.SaveAsync(_localSettingsPath, _localsettingsFile, _settings!, true);
                if (!saved)
                {
                    throw new IOException($"保存设置失败: {_localsettingsFile}");
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
    }

    public IReadOnlyDictionary<string, object> GetSettingsSnapshot()
    {
        if (RuntimeHelper.IsMSIX)
        {
            return ApplicationData.Current.LocalSettings.Values
                .Cast<KeyValuePair<string, object>>()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        lock (_settingsLock)
        {
            InitializeSettings();
            return _settings != null
                ? new Dictionary<string, object>(_settings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void InitializeSettings()
    {
        if (!_isInitialized)
        {
            _settings = _fileService.Read<Dictionary<string, object>>(_localSettingsPath, _localsettingsFile) ?? [];

            _isInitialized = true;
        }
    }

    private async Task InitializeSettingsAsync()
    {
        if (!_isInitialized)
        {
            var loaded = await _fileService.ReadAsync<Dictionary<string, object>>(_localSettingsPath, _localsettingsFile) ?? [];
            lock (_settingsLock)
            {
                if (!_isInitialized)
                {
                    _settings = loaded;
                    _isInitialized = true;
                }
            }
        }
    }

    private static T? DeserializeSettingValue<T>(object obj, T defaultValue)
    {
        var raw = JsonHelper.ConvertToString(obj);

        try
        {
            var value = JsonHelper.ToObject<T>(raw);
            if (value is not null)
            {
                return value;
            }
        }
        catch
        {
            // Backward compatibility: some old settings were stored as raw values, not JSON literals.
        }

        var normalized = raw.Trim();
        if (normalized.Length >= 2 && normalized.StartsWith('"') && normalized.EndsWith('"'))
        {
            normalized = normalized[1..^1];
        }

        try
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)normalized;
            }

            if (typeof(T) == typeof(bool) && bool.TryParse(normalized, out var boolValue))
            {
                return (T)(object)boolValue;
            }

            if (typeof(T) == typeof(int) && int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return (T)(object)intValue;
            }

            if (typeof(T) == typeof(DateTime) && DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue))
            {
                return (T)(object)dateTimeValue;
            }

            if (typeof(T).IsEnum && Enum.TryParse(typeof(T), normalized, ignoreCase: true, out var enumValue))
            {
                return (T)enumValue;
            }
        }
        catch
        {
            // ignored, fallback to default below
        }

        return defaultValue;
    }

    #endregion

    #region Json Files

    public T? ReadJsonFile<T>(string fileName, T defaultValue, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        return _fileService.Read<T>(_localSettingsPath, fileName, jsonSerializerSettings) ?? defaultValue;
    }

    public T? ReadJsonFileOrThrow<T>(string fileName, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        return _fileService.ReadOrThrow<T>(_localSettingsPath, fileName, jsonSerializerSettings);
    }

    public async Task<T?> ReadJsonFileAsync<T>(string fileName, T defaultValue, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        return await _fileService.ReadAsync<T>(_localSettingsPath, fileName, jsonSerializerSettings) ?? defaultValue;
    }

    public async Task<bool> SaveJsonFileAsync<T>(string fileName, T value)
    {
        return await _fileService.SaveAsync(_localSettingsPath, fileName, value, true);
    }

    public bool DeleteJsonFile(string fileName)
    {
        return _fileService.Delete(_localSettingsPath, fileName);
    }

    #endregion
}
