using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.Models
{
    public class BrightnessSettings : ObservableObject
    {
        private bool _autoEnabled = true;
        public bool AutoEnabled
        {
            get => _autoEnabled;
            set => SetProperty(ref _autoEnabled, value);
        }

        private bool _usePreference = true;
        public bool UsePreference
        {
            get => _usePreference;
            set => SetProperty(ref _usePreference, value);
        }

        private byte _minBrightness = 30;
        public byte MinBrightness
        {
            get => _minBrightness;
            set => SetProperty(ref _minBrightness, value);
        }

        private byte _maxBrightness = 100;
        public byte MaxBrightness
        {
            get => _maxBrightness;
            set => SetProperty(ref _maxBrightness, value);
        }

        private double _minColorTemp = 2700;
        public double MinColorTemp
        {
            get => _minColorTemp;
            set => SetProperty(ref _minColorTemp, value);
        }

        private double _maxColorTemp = 6500;
        public double MaxColorTemp
        {
            get => _maxColorTemp;
            set => SetProperty(ref _maxColorTemp, value);
        }

        private double _brightnessBias = 0;
        public double BrightnessBias
        {
            get => _brightnessBias;
            set => SetProperty(ref _brightnessBias, value);
        }

        private double _colorTempBias = 0;
        public double ColorTempBias
        {
            get => _colorTempBias;
            set => SetProperty(ref _colorTempBias, value);
        }
    }
}
