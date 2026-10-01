using System.Text.Json.Serialization;
using BluetoothAudioReceiver.Automation;

namespace BluetoothAudioReceiver.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SolarCalculator.SunriseSunsetResponse))]
[JsonSerializable(typeof(SolarCalculator.SunriseSunsetResults))]
[JsonSerializable(typeof(SolarCalculator.AutoDayNightCache))]
[JsonSerializable(typeof(SolarCalculator.SunTimeHistoryEntry))]
[JsonSerializable(typeof(List<SolarCalculator.SunTimeHistoryEntry>))]
[JsonSerializable(typeof(TaskInfoDto))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(List<AutomationProgramListEntry>))]
[JsonSerializable(typeof(List<AutomationProgramListDefinition>))]
[JsonSerializable(typeof(AutomationProgramListCatalog.AutomationProgramListContainer))]
internal partial class AppJsonContext : JsonSerializerContext;
