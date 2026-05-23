using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BluetoothAudioReceiver.Services
{
    public class CameraReaderService
    {
        private MediaCapture? _mediaCapture;
        private bool _isInitialized = false;

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync();
                _isInitialized = true;
            }
            catch
            {
                _isInitialized = false;
            }
        }

        public async Task<(double brightness, double colorTemp)> GetAverageBrightnessAndColorTempAsync(int frameCount = 5)
        {
            if (!_isInitialized || _mediaCapture == null)
            {
                await InitializeAsync();
                if (!_isInitialized) return (128, 4000);
            }

            double totalBrightness = 0;
            double totalColorTemp = 0;

            for (int i = 0; i < frameCount; i++)
            {
                try
                {
                    var videoFrame = new Windows.Media.VideoFrame(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, 320, 240);
                    var mediaCapture = _mediaCapture;
                    if (mediaCapture == null)
                    {
                        return (128, 4000);
                    }

                    await mediaCapture.GetPreviewFrameAsync(videoFrame);

                    var bitmap = videoFrame.SoftwareBitmap;
                    var pixels = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
                    var reference = pixels.CreateReference();

                    double brightness = 0;
                    double colorTemp = 0;

                    unsafe
                    {
                        IMemoryBufferByteAccess buffer = (IMemoryBufferByteAccess)reference;
                        byte* data;
                        uint capacity;
                        buffer.GetBuffer(out data, out capacity);
                        
                        int pixelCount = (int)(capacity / 4);
                        if (pixelCount == 0) pixelCount = 1;
                        
                        for (int j = 0; j < capacity; j += 4)
                        {
                            byte r = data[j + 2];
                            byte g = data[j + 1];
                            byte b = data[j + 0];

                            brightness += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            
                            double ratio = r - b;
                            colorTemp += 4000 + (ratio * 100);
                        }
                        
                        brightness /= pixelCount;
                        colorTemp /= pixelCount;
                    }

                    totalBrightness += brightness;
                    totalColorTemp += colorTemp;
                }
                catch
                {
                    continue;
                }
            }

            return (totalBrightness / frameCount, totalColorTemp / frameCount);
        }

        public bool IsInitialized => _isInitialized;
    }

    [System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-8653-2F1D5D5C1DF4")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
