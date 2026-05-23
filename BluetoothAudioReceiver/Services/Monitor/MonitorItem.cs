using System;
using System.Threading.Tasks;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public abstract class MonitorItem
    {
        public string DeviceInstanceId { get; protected set; }
        public string Description { get; protected set; }
        public byte DisplayIndex { get; protected set; }
        public byte MonitorIndex { get; protected set; }
        public int Brightness { get; protected set; } = -1;
        public int Contrast { get; protected set; } = -1;
        public bool IsInternal { get; protected set; }
        public bool IsReachable { get; protected set; }

        protected MonitorItem(string deviceInstanceId, string description, byte displayIndex, byte monitorIndex, bool isInternal, bool isReachable)
        {
            DeviceInstanceId = deviceInstanceId;
            Description = description;
            DisplayIndex = displayIndex;
            MonitorIndex = monitorIndex;
            IsInternal = isInternal;
            IsReachable = isReachable;
        }

        public abstract AccessResult UpdateBrightness(int value = -1);
        public abstract AccessResult SetBrightness(int brightness);
        public abstract AccessResult UpdateContrast();
        public abstract AccessResult SetContrast(int contrast);

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async Task UpdateBrightnessAsync(int value = -1)
        {
            await Task.Run(() => UpdateBrightness(value));
        }

        public async Task SetBrightnessAsync(int brightness)
        {
            await Task.Run(() => SetBrightness(brightness));
        }

        public async Task UpdateContrastAsync()
        {
            await Task.Run(() => UpdateContrast());
        }

        public async Task SetContrastAsync(int contrast)
        {
            await Task.Run(() => SetContrast(contrast));
        }
    }
}
