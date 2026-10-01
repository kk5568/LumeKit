using Serilog;

namespace BluetoothAudioReceiver.Extensions;

internal static class TaskExtensions
{
    public static void Forget(this Task task, string sourceContext)
    {
        _ = ObserveAsync(task, sourceContext);
    }

    public static void Forget<T>(this Task<T> task, string sourceContext)
    {
        _ = ObserveAsync(task, sourceContext);
    }

    private static async Task ObserveAsync(Task task, string sourceContext)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignored.
        }
        catch (Exception ex)
        {
            Log.ForContext("SourceContext", sourceContext)
                .Warning(ex, "Background task failed");
        }
    }
}
