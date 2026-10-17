using BluetoothAudioReceiver.Automation.Models;
using Serilog;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 性能采样的调度方。节拍归调度中心，只有存在订阅者时才运行；
/// 没人订阅就空转等待，不再常驻轮询。
/// 订阅者拿到采样入口后自己决定要不要真的采样。
/// </summary>
internal sealed class PerformanceObservationSampler : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(PerformanceObservationSampler));
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CandidatePollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(2);

    private readonly object _syncRoot = new();
    private readonly List<Subscription> _subscriptions = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private Func<PerformanceSample>? _capture;
    private volatile bool _paused;
    private bool _started;

    /// <summary>
    /// 登记一个订阅者。回调返回 true 表示"现在处于活跃状态"，节拍会放慢到 3 秒；
    /// 返回 false 时用 500 毫秒。
    /// </summary>
    public IDisposable Subscribe(string owner, Func<Func<PerformanceSample>, bool> onTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(onTick);

        var subscription = new Subscription(this, owner, onTick);
        lock (_syncRoot)
        {
            _subscriptions.Add(subscription);
        }

        _log.Information("Performance sampler subscriber added. Owner={Owner}", owner);
        return subscription;
    }

    public void Start(Func<PerformanceSample> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        lock (_syncRoot)
        {
            if (_started)
            {
                _capture = capture;
                return;
            }

            _started = true;
            _capture = capture;
            _cts = new CancellationTokenSource();
        }

        _loopTask = Task.Run(() => RunLoopAsync(_cts!.Token));
        _log.Information("Performance sampler started");
    }

    /// <summary>锁屏时暂停采样，和原来的行为一致。</summary>
    public void SetPaused(bool paused) => _paused = paused;

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Subscription[] snapshot;
            Func<PerformanceSample>? capture;
            lock (_syncRoot)
            {
                snapshot = _subscriptions.ToArray();
                capture = _capture;
            }

            // 没人订阅，或者还没有采样入口：不采样，空转等待。
            if (snapshot.Length == 0 || capture is null || _paused)
            {
                try
                {
                    await Task.Delay(IdlePollInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var isActive = false;
            var failed = false;
            foreach (var subscription in snapshot)
            {
                try
                {
                    if (subscription.OnTick(capture))
                    {
                        isActive = true;
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    _log.Warning(ex, "Performance sampler subscriber failed. Owner={Owner}", subscription.Owner);
                }
            }

            var delay = failed
                ? FailureRetryInterval
                : isActive ? ActivePollInterval : CandidatePollInterval;

            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_syncRoot)
        {
            if (!_subscriptions.Remove(subscription))
            {
                return;
            }
        }

        _log.Information("Performance sampler subscriber removed. Owner={Owner}", subscription.Owner);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? loopTask;
        lock (_syncRoot)
        {
            cts = _cts;
            loopTask = _loopTask;
            _cts = null;
            _loopTask = null;
            _started = false;
            _subscriptions.Clear();
        }

        cts?.Cancel();
        try
        {
            loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 退出时忽略循环里的异常。
        }

        cts?.Dispose();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly PerformanceObservationSampler _sampler;
        private int _disposed;

        public Subscription(
            PerformanceObservationSampler sampler,
            string owner,
            Func<Func<PerformanceSample>, bool> onTick)
        {
            _sampler = sampler;
            Owner = owner;
            OnTick = onTick;
        }

        public string Owner { get; }

        public Func<Func<PerformanceSample>, bool> OnTick { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _sampler.Unsubscribe(this);
        }
    }
}
