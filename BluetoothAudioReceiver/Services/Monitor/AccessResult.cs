using System;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public enum AccessStatus
    {
        Succeeded,
        Failed,
        NotSupported,
        Unreachable
    }

    public class AccessResult
    {
        public AccessStatus Status { get; }
        public string? Message { get; }

        public static AccessResult Succeeded => new(AccessStatus.Succeeded, null);
        public static AccessResult Failed => new(AccessStatus.Failed, null);
        public static AccessResult NotSupported => new(AccessStatus.NotSupported, null);
        public static AccessResult Unreachable => new(AccessStatus.Unreachable, null);

        public AccessResult(AccessStatus status, string? message)
        {
            Status = status;
            Message = message;
        }

        public override string ToString()
        {
            return $"{Status}: {Message ?? string.Empty}";
        }
    }
}
