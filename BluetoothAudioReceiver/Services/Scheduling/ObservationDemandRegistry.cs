using BluetoothAudioReceiver.Automation.Models;
using Serilog;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 调度中心的需求登记表。各模块在这里登记「我需要哪些系统信息」和「我需要哪些事件源」，
/// 调度中心按所有登记项的并集工作；没人登记的信息一条都不采，没人登记的事件源一个都不订。
/// 登记项是委托而不是快照，所以模块自己的开关变化后不用重新登记。
/// </summary>
internal sealed class ObservationDemandRegistry
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(ObservationDemandRegistry));

    private readonly object _syncRoot = new();
    private readonly List<Registration<ObservationInfoKind>> _infoRegistrations = new();
    private readonly List<Registration<ObservationSourceKind>> _sourceRegistrations = new();

    /// <summary>
    /// 登记一个模块的采集需求。调用方应持有返回的句柄，模块停止时释放。
    /// </summary>
    public IDisposable Register(string owner, Func<IReadOnlyCollection<ObservationInfoKind>> demandProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(demandProvider);

        var registration = new Registration<ObservationInfoKind>(
            owner,
            demandProvider,
            OnInfoRegistrationDisposed);

        lock (_syncRoot)
        {
            _infoRegistrations.Add(registration);
        }

        _log.Information("Observation demand registered. Owner={Owner}", owner);
        return registration;
    }

    /// <summary>
    /// 登记一个模块需要的事件源。没人登记的事件源不会被订阅。
    /// </summary>
    public IDisposable RegisterSources(string owner, Func<IReadOnlyCollection<ObservationSourceKind>> demandProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(demandProvider);

        var registration = new Registration<ObservationSourceKind>(
            owner,
            demandProvider,
            OnSourceRegistrationDisposed);

        lock (_syncRoot)
        {
            _sourceRegistrations.Add(registration);
        }

        _log.Information("Observation source demand registered. Owner={Owner}", owner);
        return registration;
    }

    /// <summary>
    /// 所有登记项的并集。登记项由模块自己决定，这里不做任何猜测。
    /// </summary>
    public IReadOnlyCollection<ObservationInfoKind> GetUnion()
        => Collect(_infoRegistrations, nameof(GetUnion));

    /// <summary>
    /// 所有模块需要的事件源并集。
    /// </summary>
    public IReadOnlyCollection<ObservationSourceKind> GetSourceUnion()
        => Collect(_sourceRegistrations, nameof(GetSourceUnion));

    private HashSet<T> Collect<T>(List<Registration<T>> registrations, string caller)
        where T : struct, Enum
    {
        Registration<T>[] snapshot;
        lock (_syncRoot)
        {
            snapshot = registrations.ToArray();
        }

        var union = new HashSet<T>();
        foreach (var registration in snapshot)
        {
            IReadOnlyCollection<T>? demanded = null;
            try
            {
                demanded = registration.DemandProvider();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Observation demand provider failed. Caller={Caller}, Owner={Owner}", caller, registration.Owner);
            }

            if (demanded is null)
            {
                continue;
            }

            foreach (var item in demanded)
            {
                union.Add(item);
            }
        }

        return union;
    }

    private void OnInfoRegistrationDisposed(Registration<ObservationInfoKind> registration)
        => Unregister(_infoRegistrations, registration);

    private void OnSourceRegistrationDisposed(Registration<ObservationSourceKind> registration)
        => Unregister(_sourceRegistrations, registration);

    private void Unregister<T>(List<Registration<T>> registrations, Registration<T> registration)
        where T : struct, Enum
    {
        lock (_syncRoot)
        {
            if (!registrations.Remove(registration))
            {
                return;
            }
        }

        _log.Information("Observation demand unregistered. Owner={Owner}", registration.Owner);
    }

    private sealed class Registration<T> : IDisposable
        where T : struct, Enum
    {
        private readonly Action<Registration<T>> _onDispose;
        private int _disposed;

        public Registration(
            string owner,
            Func<IReadOnlyCollection<T>> demandProvider,
            Action<Registration<T>> onDispose)
        {
            Owner = owner;
            DemandProvider = demandProvider;
            _onDispose = onDispose;
        }

        public string Owner { get; }

        public Func<IReadOnlyCollection<T>> DemandProvider { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _onDispose(this);
        }
    }
}
