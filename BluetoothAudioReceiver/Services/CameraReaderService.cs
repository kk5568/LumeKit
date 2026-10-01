using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace BluetoothAudioReceiver.Services
{
    public sealed record CameraSampleMetrics(
        double Brightness,
        double ColorTemp,
        double AvgR,
        double AvgG,
        double AvgB,
        int SuccessFrames,
        int Width,
        int Height);

    public class CameraReaderService
    {
        private static readonly Serilog.ILogger _log = Serilog.Log.ForContext("SourceContext", nameof(CameraReaderService));

        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private MediaCapture? _mediaCapture;
        private MediaFrameReader? _frameReader;
        private bool _isInitialized;
        private bool _frameReaderReady;
        private bool _autoOpenAllowed;
        private string _lastStatus = "未初始化";
        private string _lastError = string.Empty;
        private int _sampleSeq;

        public string LastStatus => _lastStatus;
        public string LastError => _lastError;
        public bool IsInitialized => _isInitialized;
        public bool AutoOpenAllowed => _autoOpenAllowed;

        public void SetAutoOpenAllowed(bool allowed, string source)
        {
            _autoOpenAllowed = allowed;
            _log.Information("Camera auto-open permission changed from {Source}: {Allowed}", source, allowed);
        }

        public async Task<SoftwareBitmap?> GetLatestPreviewFrameAsync()
        {
            if (!await EnsureCameraReadyForReadAsync("preview"))
            {
                return null;
            }

            if (!_frameReaderReady || _frameReader == null)
            {
                return null;
            }

            for (var retry = 0; retry < 8; retry++)
            {
                await Task.Delay(40);
                using var frameRef = _frameReader.TryAcquireLatestFrame();
                var softwareBitmap = frameRef?.VideoMediaFrame?.SoftwareBitmap;
                if (softwareBitmap == null)
                {
                    continue;
                }

                var bitmap = softwareBitmap;
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                {
                    bitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                return SoftwareBitmap.Copy(bitmap);
            }

            return null;
        }

        public async Task InitializeAsync(bool forceOpen = false)
        {
            if (_isInitialized)
            {
                return;
            }

            if (!forceOpen && !_autoOpenAllowed)
            {
                _lastStatus = "摄像头自动打开已禁用";
                _log.Debug("InitializeAsync skipped because auto-open is disabled.");
                return;
            }

            await _stateLock.WaitAsync();
            try
            {
                if (_isInitialized)
                {
                    return;
                }

                if (!forceOpen && !_autoOpenAllowed)
                {
                    _lastStatus = "摄像头自动打开已禁用";
                    _log.Debug("InitializeAsync skipped inside lock because auto-open is disabled.");
                    return;
                }

                await InitializeCoreAsync();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task InitializeCoreAsync()
        {
            await DisposeCaptureResourcesAsync();

            // Path A: default init for unpackaged debug runs.
            try
            {
                _lastStatus = "正在默认初始化摄像头";
                _mediaCapture = CreateMediaCapture();
                var defaultSettings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                };
                await _mediaCapture.InitializeAsync(defaultSettings);
                _isInitialized = true;
                await EnsureFrameReaderReadyAsync(_mediaCapture);
                _lastStatus = _frameReaderReady ? "默认初始化成功(FrameReader)" : "默认初始化成功(未启动FrameReader)";
                _lastError = string.Empty;
                _log.Information("Camera initialized by default path.");
                return;
            }
            catch (Exception exDefault)
            {
                await DisposeCaptureResourcesAsync();
                _isInitialized = false;
                _frameReaderReady = false;
                _lastError = FormatException(exDefault);
                _log.Warning(exDefault, "Camera default initialization failed, fallback to device-id path.");
            }

            // Path B: fallback with explicit device id.
            try
            {
                _lastStatus = "正在枚举摄像头设备";
                var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _log.Information("Camera enum completed, count={Count}", devices.Count);

                if (devices.Count == 0)
                {
                    _lastStatus = "未发现摄像头设备";
                    if (string.IsNullOrWhiteSpace(_lastError))
                    {
                        _lastError = "DeviceInformation.FindAllAsync returned empty list";
                    }

                    _isInitialized = false;
                    _frameReaderReady = false;
                    _log.Warning("No camera device found.");
                    return;
                }

                var firstDevice = devices[0];
                _mediaCapture = CreateMediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = firstDevice.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                };

                await _mediaCapture.InitializeAsync(settings);
                _isInitialized = true;
                await EnsureFrameReaderReadyAsync(_mediaCapture);
                _lastStatus = _frameReaderReady
                    ? $"初始化成功(FrameReader): {firstDevice.Name}"
                    : $"初始化成功但FrameReader未启动: {firstDevice.Name}";
                _lastError = string.Empty;
                _log.Information("Camera initialized successfully, device={DeviceName}", firstDevice.Name);
            }
            catch (Exception ex)
            {
                await DisposeCaptureResourcesAsync();
                _isInitialized = false;
                _frameReaderReady = false;
                _lastStatus = "初始化失败";
                _lastError = FormatException(ex);
                _log.Warning(ex, "Camera initialization failed.");
            }
        }

        public async Task ShutdownAsync()
        {
            await _stateLock.WaitAsync();
            try
            {
                await DisposeCaptureResourcesAsync();
                _frameReaderReady = false;
                _isInitialized = false;
                _lastStatus = "摄像头已释放";
                _lastError = string.Empty;
                _log.Information("Camera resources released.");
            }
            catch (Exception ex)
            {
                _lastError = FormatException(ex);
                _log.Warning(ex, "ShutdownAsync failed.");
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task<(double brightness, double colorTemp)> GetAverageBrightnessAndColorTempAsync(int frameCount = 5)
        {
            var metrics = await GetSampleMetricsAsync(frameCount);
            return (metrics.Brightness, metrics.ColorTemp);
        }

        public async Task<CameraSampleMetrics> GetSampleMetricsAsync(int frameCount = 5)
        {
            if (!await EnsureCameraReadyForReadAsync("sample"))
            {
                _sampleSeq++;
                _lastStatus = $"采样#{_sampleSeq}失败，初始化不可用，返回回退值";
                _log.Warning("Camera sample fallback used. initialized={Initialized}, error={Error}", _isInitialized, _lastError);
                return new CameraSampleMetrics(128, 4000, 128, 128, 128, 0, 0, 0);
            }

            var mediaCapture = _mediaCapture;
            if (mediaCapture == null)
            {
                _sampleSeq++;
                _lastStatus = $"采样#{_sampleSeq}失败，摄像头句柄为空，返回回退值";
                return new CameraSampleMetrics(128, 4000, 128, 128, 128, 0, 0, 0);
            }

            if (!_frameReaderReady)
            {
                await EnsureFrameReaderReadyAsync(mediaCapture);
            }

            if (_frameReaderReady && _frameReader != null)
            {
                var sampled = await SampleByFrameReaderAsync(frameCount);
                if (sampled.successFrames > 0)
                {
                    _sampleSeq++;
                    _lastStatus = $"采样#{_sampleSeq}完成(FrameReader)，有效帧={sampled.successFrames}/{frameCount}，size={sampled.width}x{sampled.height}，raw={Math.Round(sampled.avgBrightness):0}";
                    _lastError = string.Empty;
                    return new CameraSampleMetrics(
                        sampled.avgBrightness,
                        sampled.avgColorTemp,
                        sampled.avgR,
                        sampled.avgG,
                        sampled.avgB,
                        sampled.successFrames,
                        sampled.width,
                        sampled.height);
                }

                // Try one reader recovery then sample once more.
                var recovered = await RecoverFrameReaderAsync();
                if (recovered)
                {
                    sampled = await SampleByFrameReaderAsync(frameCount);
                    if (sampled.successFrames > 0)
                    {
                        _sampleSeq++;
                        _lastStatus = $"采样#{_sampleSeq}恢复成功(FrameReader)，有效帧={sampled.successFrames}/{frameCount}，size={sampled.width}x{sampled.height}，raw={Math.Round(sampled.avgBrightness):0}";
                        _lastError = string.Empty;
                        return new CameraSampleMetrics(
                            sampled.avgBrightness,
                            sampled.avgColorTemp,
                            sampled.avgR,
                            sampled.avgG,
                            sampled.avgB,
                            sampled.successFrames,
                            sampled.width,
                            sampled.height);
                    }
                }

                _sampleSeq++;
                _lastStatus = $"采样#{_sampleSeq}失败(FrameReader)，有效帧=0";
                return new CameraSampleMetrics(128, 4000, 128, 128, 128, 0, 0, 0);
            }

            _sampleSeq++;
            _lastStatus = $"采样#{_sampleSeq}失败，FrameReader不可用，返回回退值";
            return new CameraSampleMetrics(128, 4000, 128, 128, 128, 0, 0, 0);
        }

        private async Task EnsureFrameReaderReadyAsync(MediaCapture mediaCapture)
        {
            if (_frameReaderReady && _frameReader != null)
            {
                return;
            }

            await DisposeFrameReaderAsync();

            try
            {
                var source = mediaCapture.FrameSources.Values
                    .FirstOrDefault(s =>
                        s.Info.SourceKind == MediaFrameSourceKind.Color &&
                        s.Info.MediaStreamType == MediaStreamType.VideoPreview)
                    ?? mediaCapture.FrameSources.Values
                        .FirstOrDefault(s =>
                            s.Info.SourceKind == MediaFrameSourceKind.Color &&
                            s.Info.MediaStreamType == MediaStreamType.VideoRecord)
                    ?? mediaCapture.FrameSources.Values.FirstOrDefault();

                if (source == null)
                {
                    _frameReaderReady = false;
                    _lastError = "No MediaFrameSource found.";
                    _log.Warning("No frame source found for MediaFrameReader.");
                    return;
                }

                var currentFormat = source.CurrentFormat;
                if (currentFormat != null)
                {
                    _log.Information(
                        "FrameReader using current source format. Source={SourceId}, Subtype={Subtype}, Width={Width}, Height={Height}, SupportedFormats={SupportedFormats}",
                        source.Info.Id,
                        currentFormat.Subtype,
                        currentFormat.VideoFormat?.Width ?? 0,
                        currentFormat.VideoFormat?.Height ?? 0,
                        source.SupportedFormats.Count);
                }
                else
                {
                    _log.Information(
                        "FrameReader using driver-selected source format. Source={SourceId}, SupportedFormats={SupportedFormats}",
                        source.Info.Id,
                        source.SupportedFormats.Count);
                }

                try
                {
                    _frameReader = await mediaCapture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                }
                catch (Exception bgraEx)
                {
                    _log.Warning(
                        bgraEx,
                        "CreateFrameReaderAsync with Bgra8 failed, retrying with driver-selected output format.");
                    _frameReader = await mediaCapture.CreateFrameReaderAsync(source);
                }

                var status = await _frameReader.StartAsync();
                _frameReaderReady = status == MediaFrameReaderStartStatus.Success;
                _lastStatus = _frameReaderReady
                    ? $"FrameReader已启动: {source.Info.Id}"
                    : $"FrameReader启动失败: {status}";

                if (!_frameReaderReady)
                {
                    _lastError = $"FrameReaderStartStatus={status}";
                    _log.Warning("FrameReader start failed, status={Status}", status);
                }
                else
                {
                    _lastError = string.Empty;
                    _log.Information("FrameReader started, source={SourceId}", source.Info.Id);
                }
            }
            catch (Exception ex)
            {
                await DisposeFrameReaderAsync();
                _lastError = FormatException(ex);
                _log.Warning(ex, "EnsureFrameReaderReadyAsync failed.");
            }
        }

        // Intentionally avoid CaptureElement preview path in WinUI 3;
        // we render frames via SoftwareBitmapSource instead.

        private async Task<(int successFrames, int width, int height, double avgBrightness, double avgColorTemp, double avgR, double avgG, double avgB)> SampleByFrameReaderAsync(int frameCount)
        {
            if (_frameReader == null)
            {
                return (0, 0, 0, 0, 0, 0, 0, 0);
            }

            double totalBrightness = 0;
            double totalColorTemp = 0;
            double totalR = 0;
            double totalG = 0;
            double totalB = 0;
            int successFrames = 0;
            int width = 0;
            int height = 0;

            for (var i = 0; i < frameCount; i++)
            {
                MediaFrameReference? frameRef = null;
                try
                {
                    SoftwareBitmap? softwareBitmap = null;
                    // Wait/retry a little while to avoid empty-frame bursts right after reader start.
                    for (var retry = 0; retry < 8; retry++)
                    {
                        await Task.Delay(40);
                        frameRef?.Dispose();
                        frameRef = _frameReader.TryAcquireLatestFrame();
                        softwareBitmap = frameRef?.VideoMediaFrame?.SoftwareBitmap;
                        if (softwareBitmap != null)
                        {
                            break;
                        }
                    }

                    if (softwareBitmap == null)
                    {
                        _log.Information("FrameReader sample frame is null at frame {FrameIndex}.", i);
                        continue;
                    }

                    var bitmap = softwareBitmap;
                    if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                    {
                        bitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
                    }

                    width = bitmap.PixelWidth;
                    height = bitmap.PixelHeight;

                    double brightness = 0;
                    double colorTemp = 0;
                    var pixelBuffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
                    bitmap.CopyToBuffer(pixelBuffer);
                    var bytes = new byte[pixelBuffer.Length];
                    DataReader.FromBuffer(pixelBuffer).ReadBytes(bytes);

                    var pixelCount = bytes.Length / 4;
                    if (pixelCount <= 0)
                    {
                        pixelCount = 1;
                    }

                    for (var j = 0; j + 3 < bytes.Length; j += 4)
                    {
                        var b = bytes[j + 0];
                        var g = bytes[j + 1];
                        var r = bytes[j + 2];

                        brightness += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                        totalR += r;
                        totalG += g;
                        totalB += b;
                        var ratio = r - b;
                        colorTemp += 4000 + (ratio * 100);
                    }

                    brightness /= pixelCount;
                    colorTemp /= pixelCount;

                    totalBrightness += brightness;
                    totalColorTemp += colorTemp;
                    successFrames++;
                }
                catch (Exception ex)
                {
                    _lastError = FormatException(ex);
                    _log.Warning(ex, "FrameReader sample failed at frame {FrameIndex}.", i);
                }
                finally
                {
                    frameRef?.Dispose();
                }
            }

            if (successFrames == 0)
            {
                return (0, width, height, 0, 0, 0, 0, 0);
            }

            var totalPixels = Math.Max(1, (double)successFrames * width * height);
            return (
                successFrames,
                width,
                height,
                totalBrightness / successFrames,
                totalColorTemp / successFrames,
                totalR / totalPixels,
                totalG / totalPixels,
                totalB / totalPixels);
        }

        private async Task<bool> EnsureCameraReadyForReadAsync(string source)
        {
            if (_isInitialized && _mediaCapture != null)
            {
                return true;
            }

            if (!_autoOpenAllowed)
            {
                _log.Debug("Camera read skipped from {Source}: auto-open disabled.", source);
                return false;
            }

            await InitializeAsync(forceOpen: false);
            return _isInitialized && _mediaCapture != null;
        }

        private async Task<bool> RecoverFrameReaderAsync()
        {
            try
            {
                if (_frameReader != null)
                {
                    try { await _frameReader.StopAsync(); } catch { }
                    _frameReader?.Dispose();
                    _frameReader = null;
                }

                _frameReaderReady = false;
                var capture = _mediaCapture;
                if (capture == null)
                {
                    return false;
                }

                await EnsureFrameReaderReadyAsync(capture);
                return _frameReaderReady;
            }
            catch (Exception ex)
            {
                _lastError = FormatException(ex);
                _log.Warning(ex, "RecoverFrameReaderAsync failed.");
                return false;
            }
        }

        private MediaCapture CreateMediaCapture()
        {
            var mediaCapture = new MediaCapture();
            mediaCapture.Failed += OnMediaCaptureFailed;
            return mediaCapture;
        }

        private void OnMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
        {
            _isInitialized = false;
            _frameReaderReady = false;
            _lastError = $"MediaCaptureFailed 0x{args.Code:X8}: {args.Message}";
            _log.Warning(
                "MediaCapture failed. Code={Code}, Message={Message}",
                $"0x{args.Code:X8}",
                args.Message);
        }

        private async Task DisposeCaptureResourcesAsync()
        {
            await DisposeFrameReaderAsync();

            if (_mediaCapture != null)
            {
                _mediaCapture.Failed -= OnMediaCaptureFailed;
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
        }

        private async Task DisposeFrameReaderAsync()
        {
            var frameReader = _frameReader;
            _frameReader = null;
            _frameReaderReady = false;

            if (frameReader == null)
            {
                return;
            }

            try { await frameReader.StopAsync(); } catch { }
            frameReader.Dispose();
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}";
        }
    }

}
