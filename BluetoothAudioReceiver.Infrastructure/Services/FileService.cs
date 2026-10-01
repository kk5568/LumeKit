using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using BluetoothAudioReceiver.Infrastructure.Contracts.Services;
using BluetoothAudioReceiver.Infrastructure.Helpers;

namespace BluetoothAudioReceiver.Infrastructure.Services;

public class FileService : IFileService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(FileService));

    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    private readonly JsonSerializerOptions IndentedOption = new() { WriteIndented = true };
    private readonly JsonSerializerOptions UnindentedOption = new() { WriteIndented = false };

    public T? Read<T>(string folderPath, string fileName, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        var path = GetPath(folderPath, fileName);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, jsonSerializerSettings)!;
            }
            catch (Exception e)
            {
                _log.Error(e, $"Reading file {path} failed : {ExceptionFormatter.FormatExcpetion(e)}");
            }
        }

        return default!;
    }

    public T? ReadOrThrow<T>(string folderPath, string fileName, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        var path = GetPath(folderPath, fileName);
        if (!File.Exists(path))
        {
            return default;
        }

        var json = File.ReadAllText(path);
        var value = JsonSerializer.Deserialize<T>(json, jsonSerializerSettings);
        return value is null
            ? throw new JsonException($"文件内容为空或不是有效的 JSON：{path}")
            : value;
    }

    public async Task<T?> ReadAsync<T>(string folderPath, string fileName, JsonSerializerOptions? jsonSerializerSettings = null)
    {
        return await Task.Run(() => Read<T>(folderPath, fileName, jsonSerializerSettings));
    }

    public async Task<bool> SaveAsync<T>(string folderPath, string fileName, T content, bool indent)
    {
        var path = GetPath(folderPath, fileName, true);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var backupPath = $"{path}.bak";

        await semaphoreSlim.WaitAsync();

        try
        {
            var fileContent = JsonSerializer.Serialize(content, indent ? IndentedOption : UnindentedOption);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(fileContent);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            if (!string.Equals(File.ReadAllText(tempPath), fileContent, StringComparison.Ordinal))
            {
                throw new IOException($"临时文件校验失败: {tempPath}");
            }

            if (File.Exists(path))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }

            if (!string.Equals(File.ReadAllText(path), fileContent, StringComparison.Ordinal))
            {
                throw new IOException($"保存结果校验失败: {path}");
            }

            return true;
        }
        catch (Exception e)
        {
            _log.Error(e, $"Writing file {path} failed : {ExceptionFormatter.FormatExcpetion(e)}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupException)
            {
                _log.Warning(cleanupException, "Failed to clean temporary file {TempPath}", tempPath);
            }

            semaphoreSlim.Release();
        }
    }

    public bool Delete(string folderPath, string fileName)
    {
        var path = GetPath(folderPath, fileName);
        if (fileName != null && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                _log.Error(e, $"Deleting file {path} failed : {ExceptionFormatter.FormatExcpetion(e)}");
            }
            return true;
        }

        return false;
    }

    private static string GetPath(string folderPath, string fileName, bool createDirectory = false)
    {
        if (createDirectory && (!Directory.Exists(folderPath)))
        {
            Directory.CreateDirectory(folderPath);
        }

        return Path.Combine(folderPath, fileName);
    }
}
