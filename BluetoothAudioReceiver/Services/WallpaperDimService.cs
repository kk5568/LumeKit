using Serilog;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// Switches the system wallpaper to a generated dimmed copy and keeps the
/// original wallpaper identity so a later restore cannot select an old image.
/// </summary>
public sealed partial class WallpaperDimService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(WallpaperDimService));
    private const string DimmedNameSuffix = "_lumekit_dim";
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly string _statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioReceiver",
        "wallpaper-dim-state.json");

    public async Task<string?> GetPreviewWallpaperPathAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            var currentPath = GetCurrentWallpaperPath();
            if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
            {
                return null;
            }

            var state = LoadState();
            if (state is not null && IsCurrentDimmedWallpaper(state, currentPath))
            {
                return GetRestoreSourcePath(state);
            }

            return currentPath;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private string? GetRestoreSourcePath(WallpaperDimState state)
    {
        var source = GetSourceIdentity(state);

        if (!string.IsNullOrWhiteSpace(source.Path) && File.Exists(source.Path))
        {
            return source.Path;
        }

        var resolvedBackupPath = GetBackupPath(source);
        if (File.Exists(resolvedBackupPath))
        {
            return resolvedBackupPath;
        }

        if (!string.IsNullOrWhiteSpace(state.OriginalBackupPath) && File.Exists(state.OriginalBackupPath))
        {
            return state.OriginalBackupPath;
        }

        return null;
    }

    public async Task<bool> ApplyDimmedAsync(double opacity)
    {
        await _operationLock.WaitAsync();
        try
        {
            var state = LoadState();
            var currentPath = GetCurrentWallpaperPath();
            var normalizedOpacity = Math.Clamp(opacity, 0.0, 0.85);

            if (IsDimmedWallpaperName(currentPath) &&
                !IsCurrentDimmedWallpaper(state, currentPath))
            {
                ClearStaleState(state, currentPath);
                _log.Warning(
                    "Dimmed wallpaper name detected but it does not match the saved processing copy. Skipped reprocessing. Current={CurrentPath}",
                    currentPath);
                return false;
            }

            if (IsCurrentDimmedWallpaper(state, currentPath) &&
                !HasOpacityChanged(state, normalizedOpacity))
            {
                _log.Warning(
                    "Dimmed wallpaper name detected, skipped duplicate processing. Current={CurrentPath}",
                    currentPath);
                return false;
            }

            WallpaperIdentity original;
            string originalSourcePath;
            string originalBackupPath;

            if (IsCurrentDimmedWallpaper(state, currentPath))
            {
                original = GetSourceIdentity(state!) with { ReadUtc = DateTimeOffset.UtcNow };
                originalBackupPath = ResolveBackupPath(original, state!.OriginalBackupPath);
                originalSourcePath = GetRestoreSourcePath(state)
                    ?? throw new FileNotFoundException("压暗壁纸备份不存在。", originalBackupPath);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                {
                    throw new FileNotFoundException("当前壁纸不存在。", currentPath);
                }

                original = ReadIdentity(currentPath);
                if (state is not null && !IsSameOriginalIdentity(original, GetSourceIdentity(state)))
                {
                    _log.Information(
                        "Current wallpaper changed. Replacing the saved wallpaper state. Current={CurrentPath}, Previous={PreviousPath}",
                        original.Path,
                        GetSourceIdentity(state).Path);
                    ClearStaleState(state, currentPath);
                    state = null;
                }

                originalBackupPath = GetBackupPath(original);
                originalSourcePath = currentPath;
            }

            EnsureWallpaperBackup(originalSourcePath, originalBackupPath);

            var dimmedPath = GetDimmedPath(original);

            if (!File.Exists(dimmedPath) ||
                state is null ||
                !PathsEqual(state.DimmedPath, dimmedPath) ||
                Math.Abs(state.Opacity - normalizedOpacity) > 0.001)
            {
                await CreateDimmedCopyAsync(originalBackupPath, dimmedPath, normalizedOpacity);
            }

            try
            {
                SetWallpaper(dimmedPath);
                var processedUtc = DateTimeOffset.UtcNow;
                SaveState(new WallpaperDimState
                {
                    Source = original,
                    Original = original,
                    OriginalBackupPath = originalBackupPath,
                    DimmedProcessedUtc = processedUtc,
                    Dimmed = ReadIdentity(dimmedPath, processedUtc),
                    DimmedIdentityKey = GetDimmedIdentityKey(original, processedUtc),
                    DimmedPath = dimmedPath,
                    Opacity = normalizedOpacity
                });

                DeleteUnusedDimmedCopies(dimmedPath);
                DeleteUnusedOriginalBackups(originalBackupPath);
                _log.Information(
                    "Dimmed wallpaper applied. Original={OriginalPath}, Backup={BackupPath}, Dimmed={DimmedPath}, Opacity={Opacity}",
                original.Path,
                originalBackupPath,
                dimmedPath,
                normalizedOpacity);
                return true;
            }
            catch (Exception ex)
            {
                LogWallpaperFailure(
                    ex,
                    "apply",
                    currentPath,
                    original.Path,
                    dimmedPath,
                    normalizedOpacity);
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task RestoreAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            var state = LoadState();
            if (state is null || string.IsNullOrWhiteSpace(GetSourceIdentity(state).Path))
            {
                return;
            }

            var currentPath = GetCurrentWallpaperPath();
            if (!IsCurrentDimmedWallpaper(state, currentPath))
            {
                _log.Information(
                    "Wallpaper restore skipped because the current wallpaper is not the saved processing copy. Current={CurrentPath}, Expected={DimmedPath}",
                    currentPath ?? string.Empty,
                    state.DimmedPath);
                ClearStaleState(state, currentPath);
                return;
            }

            var restoreSourcePath = GetRestoreSourcePath(state);
            var source = GetSourceIdentity(state);
            if (string.IsNullOrWhiteSpace(restoreSourcePath))
            {
                _log.Warning(
                    "Wallpaper restore skipped because both original wallpaper and backup are missing. Original={OriginalPath}, Backup={BackupPath}",
                    source.Path,
                    state.OriginalBackupPath);
                return;
            }

            _log.Information(
                "Restoring wallpaper. RestoreSource={RestoreSourcePath}, Original={OriginalPath}, Backup={BackupPath}, Dimmed={DimmedPath}",
                restoreSourcePath,
                source.Path,
                state.OriginalBackupPath,
                state.DimmedPath);
            try
            {
                var backupPath = ResolveBackupPath(source, state.OriginalBackupPath);
                if (PathsEqual(restoreSourcePath, source.Path) && File.Exists(restoreSourcePath))
                {
                    EnsureWallpaperBackup(restoreSourcePath, backupPath);
                    restoreSourcePath = backupPath;
                }

                SetWallpaper(restoreSourcePath);
                state.Source = source;
                state.OriginalBackupPath = backupPath;
                SaveState(state);
                _log.Information("Original wallpaper restored. RestoreSource={RestoreSourcePath}", restoreSourcePath);
            }
            catch (Exception ex)
            {
                LogWallpaperFailure(
                    ex,
                    "restore",
                    null,
                    source.Path,
                    state.DimmedPath,
                    state.Opacity);
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private string ResolveBackupPath(WallpaperIdentity original, string? storedBackupPath)
    {
        if (!string.IsNullOrWhiteSpace(storedBackupPath))
        {
            return storedBackupPath;
        }

        return GetBackupPath(original);
    }

    private void EnsureWallpaperBackup(string sourcePath, string backupPath)
    {
        if (PathsEqual(sourcePath, backupPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(backupPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("原壁纸备份目录无效。");
        }

        Directory.CreateDirectory(directory);
        if (!File.Exists(backupPath))
        {
            File.Copy(sourcePath, backupPath, overwrite: false);
        }
    }

    private WallpaperIdentity GetSourceIdentity(WallpaperDimState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Source.Path) &&
            !IsDimmedWallpaperName(state.Source.Path))
        {
            return state.Source;
        }

        if (!string.IsNullOrWhiteSpace(state.Original.Path) && !IsDimmedWallpaperName(state.Original.Path))
        {
            return state.Original;
        }

        if (!string.IsNullOrWhiteSpace(state.OriginalBackupPath) && File.Exists(state.OriginalBackupPath))
        {
            return ReadIdentity(state.OriginalBackupPath);
        }

        return state.Original;
    }

    private bool IsCurrentDimmedWallpaper(WallpaperDimState? state, string? currentPath)
    {
        if (state is null || !IsDimmedWallpaperName(currentPath))
        {
            return false;
        }

        if (PathsEqual(currentPath, state.DimmedPath))
        {
            return true;
        }

        var source = GetSourceIdentity(state);
        return !string.IsNullOrWhiteSpace(source.Path) &&
               PathsEqual(currentPath, GetDimmedPath(source));
    }

    private void ClearStaleState(WallpaperDimState? state, string? currentPath)
    {
        if (state is not null)
        {
            DeleteManagedFile(state.DimmedPath, currentPath);
            DeleteManagedFile(state.OriginalBackupPath, currentPath);
        }

        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to clear stale wallpaper dim state.");
        }
    }

    private static void DeleteManagedFile(string? path, string? protectedPath)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            PathsEqual(path, protectedPath) ||
            !IsManagedWallpaperPath(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Stale cleanup must not block wallpaper switching.
        }
    }

    private static bool IsManagedWallpaperPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var managedDirectory = GetWallpaperDimDirectory();
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(managedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimmedWallpaperName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(fileName) &&
               (fileName.Contains(DimmedNameSuffix, StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("wallpaper-dim-", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasOpacityChanged(WallpaperDimState? state, double normalizedOpacity)
    {
        return state is not null && Math.Abs(state.Opacity - normalizedOpacity) > 0.001;
    }

    private static bool IsSameOriginalIdentity(WallpaperIdentity left, WallpaperIdentity right)
    {
        return string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) &&
               left.Size == right.Size;
    }

    private void DeleteUnusedOriginalBackups(string activePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(activePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (!PathsEqual(path, activePath))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to clean old original wallpaper backups.");
        }
    }

    private string? GetCurrentWallpaperPath()
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperComObject();
        try
        {
            var result = wallpaper.GetWallpaper(null, out var pathPointer);
            try
            {
                ThrowIfFailed(result, "读取当前壁纸失败");
                var path = pathPointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pathPointer);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            finally
            {
                if (pathPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(wallpaper);
        }
    }

    private static void SetWallpaper(string path)
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperComObject();
        try
        {
            var result = wallpaper.SetWallpaper(null, path);
            ThrowIfFailed(result, "设置壁纸失败");
        }
        finally
        {
            Marshal.ReleaseComObject(wallpaper);
        }
    }

    private async Task CreateDimmedCopyAsync(string sourcePath, string destinationPath, double opacity)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("压暗壁纸目录无效。");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = destinationPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
        using (File.Create(temporaryPath))
        {
        }

        try
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var inputStream = await sourceFile.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(inputStream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var pixelBuffer = new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
            bitmap.CopyToBuffer(pixelBuffer);
            var bytes = new byte[pixelBuffer.Length];
            using (var reader = DataReader.FromBuffer(pixelBuffer))
            {
                reader.ReadBytes(bytes);
            }

            var keepFactor = 1.0 - opacity;
            for (var index = 0; index + 3 < bytes.Length; index += 4)
            {
                bytes[index] = (byte)Math.Clamp(bytes[index] * keepFactor, 0, 255);
                bytes[index + 1] = (byte)Math.Clamp(bytes[index + 1] * keepFactor, 0, 255);
                bytes[index + 2] = (byte)Math.Clamp(bytes[index + 2] * keepFactor, 0, 255);
            }

            var outputBuffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
            bitmap.CopyFromBuffer(outputBuffer);

            using var outputStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            outputStream.Seek(0);
            var encodedBytes = new byte[(int)outputStream.Size];
            using (var reader = new DataReader(outputStream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)encodedBytes.Length);
                reader.ReadBytes(encodedBytes);
            }

            await File.WriteAllBytesAsync(temporaryPath, encodedBytes);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private WallpaperIdentity ReadIdentity(string path, DateTimeOffset? readUtc = null)
    {
        var fileInfo = new FileInfo(path);
        return new WallpaperIdentity(
            System.IO.Path.GetFullPath(path),
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks,
            readUtc ?? DateTimeOffset.UtcNow);
    }

    private string GetDimmedPath(WallpaperIdentity original)
    {
        var originalName = Path.GetFileNameWithoutExtension(original.Path);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = "wallpaper";
        }

        var directory = Path.Combine(GetWallpaperDimDirectory(), "Dimmed", GetOriginalIdentityHash(original));
        return Path.Combine(directory, $"{originalName}{DimmedNameSuffix}.png");
    }

    private string GetBackupPath(WallpaperIdentity original)
    {
        var fileName = Path.GetFileName(original.Path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "wallpaper";
        }

        var directory = Path.Combine(GetWallpaperDimDirectory(), "Originals", GetOriginalIdentityHash(original));
        return Path.Combine(directory, fileName);
    }

    private static string GetOriginalIdentityHash(WallpaperIdentity original)
    {
        var stableIdentity = $"{original.Path}\n{original.Size}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity))).ToLowerInvariant();
    }

    private static string GetDimmedIdentityKey(WallpaperIdentity original, DateTimeOffset processedUtc)
    {
        var processedIdentity = $"{original.Path}\n{original.Size}\n{processedUtc.UtcTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(processedIdentity))).ToLowerInvariant();
    }

    private static string GetWallpaperDimDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothAudioReceiver",
            "WallpaperDim");
    }

    private WallpaperDimState? LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(_statePath), WallpaperDimStateContext.Default.WallpaperDimState);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load wallpaper dim state.");
            return null;
        }
    }

    private void SaveState(WallpaperDimState state)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, WallpaperDimStateContext.Default.WallpaperDimState), Encoding.UTF8);
        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    private void DeleteUnusedDimmedCopies(string activePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(activePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, $"*{DimmedNameSuffix}.png"))
            {
                if (!PathsEqual(path, activePath))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to clean old dimmed wallpaper copies.");
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(
                   Path.GetFullPath(left),
                   Path.GetFullPath(right),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfFailed(int hResult, string message)
    {
        if (hResult < 0)
        {
            throw new COMException($"{message} (hr=0x{hResult:X8})", hResult);
        }
    }

    private static void LogWallpaperFailure(
        Exception ex,
        string action,
        string? currentPath,
        string originalPath,
        string dimmedPath,
        double opacity)
    {
        var hResultText = ex.HResult != 0 ? $"0x{ex.HResult:X8}" : "n/a";
        _log.Warning(
            ex,
            "Wallpaper {Action} failed. HResult={HResult}, Current={CurrentPath}, Original={OriginalPath}, Dimmed={DimmedPath}, Opacity={Opacity}",
            action,
            hResultText,
            currentPath ?? string.Empty,
            originalPath,
            dimmedPath,
            opacity);
    }

    /// <summary>
    /// 编译期生成的 JSON 元数据，替代反射写法，满足 AOT/裁剪要求。
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(WallpaperDimState))]
    private sealed partial class WallpaperDimStateContext : JsonSerializerContext
    {
    }

    private sealed class WallpaperDimState
    {
        public WallpaperIdentity Source { get; set; } = new();
        public WallpaperIdentity Original { get; set; } = new();
        public string OriginalBackupPath { get; set; } = string.Empty;
        public WallpaperIdentity Dimmed { get; set; } = new();
        public string DimmedIdentityKey { get; set; } = string.Empty;
        public string DimmedPath { get; set; } = string.Empty;
        public DateTimeOffset DimmedProcessedUtc { get; set; } = DateTimeOffset.MinValue;
        public double Opacity { get; set; }
    }

    private sealed record WallpaperIdentity(
        string Path,
        long Size,
        long LastWriteUtcTicks,
        DateTimeOffset ReadUtc)
    {
        public WallpaperIdentity() : this(string.Empty, 0, 0, DateTimeOffset.MinValue)
        {
        }
    }

    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperComObject;

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig]
        int SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [PreserveSig]
        int GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            out IntPtr wallpaper);
    }
}
