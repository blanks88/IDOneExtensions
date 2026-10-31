using System.Text.Json;

namespace Syscom.Load;

public enum SyscomSyncStage
{
    Preparation,
    Download,
    Staging,
    CatalogPromotion,
    PriceRecalculation,
    Promotion
}

/// <summary>
/// Monotonic, thread-safe timing for one synchronization run. Repeated measurements of a
/// stage accumulate. Scopes can overlap or nest, and Promotion contains its sub-stages,
/// so stage durations must not be added to calculate the total elapsed time.
/// </summary>
public sealed class SyscomSyncMetrics
{
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly object _gate = new();
    private readonly long[] _completedTicks = new long[Enum.GetValues<SyscomSyncStage>().Length];
    private readonly HashSet<Measurement> _active = [];

    public SyscomSyncMetrics(TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
    }

    public IDisposable Measure(SyscomSyncStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        lock (_gate)
        {
            var measurement = new Measurement(this, stage, _clock.GetTimestamp());
            _active.Add(measurement);
            return measurement;
        }
    }

    public long GetElapsedMilliseconds(SyscomSyncStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        lock (_gate) return StageTicks(stage, _clock.GetTimestamp()) / TimeSpan.TicksPerMillisecond;
    }

    public long TotalElapsedMilliseconds
    {
        get
        {
            lock (_gate)
                return _clock.GetElapsedTime(_started, _clock.GetTimestamp()).Ticks / TimeSpan.TicksPerMillisecond;
        }
    }

    public string ToJson()
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            return JsonSerializer.Serialize(new
            {
                total_elapsed_ms = _clock.GetElapsedTime(_started, now).Ticks / TimeSpan.TicksPerMillisecond,
                preparation_ms = StageTicks(SyscomSyncStage.Preparation, now) / TimeSpan.TicksPerMillisecond,
                download_ms = StageTicks(SyscomSyncStage.Download, now) / TimeSpan.TicksPerMillisecond,
                staging_ms = StageTicks(SyscomSyncStage.Staging, now) / TimeSpan.TicksPerMillisecond,
                catalog_promotion_ms = StageTicks(SyscomSyncStage.CatalogPromotion, now) / TimeSpan.TicksPerMillisecond,
                price_recalculation_ms = StageTicks(SyscomSyncStage.PriceRecalculation, now) / TimeSpan.TicksPerMillisecond,
                promotion_ms = StageTicks(SyscomSyncStage.Promotion, now) / TimeSpan.TicksPerMillisecond
            });
        }
    }

    private long StageTicks(SyscomSyncStage stage, long now)
    {
        var ticks = _completedTicks[(int)stage];
        foreach (var scope in _active)
            if (scope.Stage == stage)
                ticks += _clock.GetElapsedTime(scope.Started, now).Ticks;
        return ticks;
    }

    private void Complete(Measurement measurement)
    {
        lock (_gate)
        {
            if (_active.Remove(measurement))
                _completedTicks[(int)measurement.Stage] +=
                    _clock.GetElapsedTime(measurement.Started, _clock.GetTimestamp()).Ticks;
        }
    }

    private sealed class Measurement(SyscomSyncMetrics owner, SyscomSyncStage stage, long started) : IDisposable
    {
        public SyscomSyncStage Stage { get; } = stage;
        public long Started { get; } = started;
        public void Dispose() => owner.Complete(this);
    }
}
