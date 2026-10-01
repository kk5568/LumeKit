using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BluetoothAudioReceiver.Core.Contracts.Services;

public interface ILocalSettingsService
{
    T? ReadSetting<T>(string key);

    T? ReadSetting<T>(string key, T defaultValue);

    Task<T?> ReadSettingAsync<T>(string key);

    Task<T?> ReadSettingAsync<T>(string key, T defaultValue);

    Task SaveSettingAsync<T>(string key, T value);

    IReadOnlyDictionary<string, object> GetSettingsSnapshot();

    T? ReadJsonFile<T>(string fileName, T defaultValue, JsonSerializerOptions? jsonSerializerSettings = null);

    T? ReadJsonFileOrThrow<T>(string fileName, JsonSerializerOptions? jsonSerializerSettings = null);

    Task<T?> ReadJsonFileAsync<T>(string fileName, T defaultValue, JsonSerializerOptions? jsonSerializerSettings = null);

    Task<bool> SaveJsonFileAsync<T>(string fileName, T value);

    bool DeleteJsonFile(string fileName);
}
