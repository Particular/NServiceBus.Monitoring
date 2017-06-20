﻿namespace NServiceBus.Metrics
{
    using System;
    using System.Linq;
    using global::Metrics.MetricData;

    static class MetricsDataExtentions
    {

        public static MetricsData SnapshotMetrics(this MetricsData data, DateTime reportWindowStart, DateTime reportWindowEnd)
        {
            var environment = data.Environment.ToList();

            environment.Add(new EnvironmentEntry("ReportWindowStart", $"{reportWindowStart:yyyy-MM-ddTHH:mm:ss.FFFZ}"));
            environment.Add(new EnvironmentEntry("ReportWindowEnd", $"{reportWindowEnd:yyyy-MM-ddTHH:mm:ss.FFFZ}"));

            return new MetricsData(
                data.Context,
                DateTime.UtcNow,
                environment,
                data.Gauges.Select(g => g.Snapshot()).ToArray(),
                data.Counters.Select(c => c.Snapshot()).ToArray(),
                data.Meters.Select(m => m.Snapshot(true)).ToArray(),
                data.Histograms.Select(h => h.Snapshot(true)).ToArray(),
                data.Timers.Select(t => t.Snapshot(true)).ToArray(),
                data.ChildMetrics);
        }
    }

    static class GagueValueSourceExtentions
    {
        public static GaugeValueSource Snapshot(this GaugeValueSource source, bool reset = false)
        {
            return new GaugeValueSource(source.Name, new SnapshotMetricValueProvider<double>(source.ValueProvider, reset), source.Unit, source.Tags);
        }
    }

    static class CounterValueSourceExtentions
    {
        public static CounterValueSource Snapshot(this CounterValueSource source, bool reset = false)
        {
            return new CounterValueSource(source.Name, new SnapshotMetricValueProvider<CounterValue>(source.ValueProvider, reset), source.Unit, source.Tags);
        }
    }

    static class MeterValueSourceExtentions
    {
        public static MeterValueSource Snapshot(this MeterValueSource source, bool reset = false)
        {
            return new MeterValueSource(source.Name, new SnapshotMetricValueProvider<MeterValue>(source.ValueProvider, reset), source.Unit, source.RateUnit, source.Tags);
        }
    }

    static class HistogramValueSourceExtentions
    {
        public static HistogramValueSource Snapshot(this HistogramValueSource source, bool reset = false)
        {
            return new HistogramValueSource(source.Name, new SnapshotMetricValueProvider<HistogramValue>(source.ValueProvider, reset), source.Unit, source.Tags);
        }
    }

    static class TimerValueSourceExtentions
    {
        public static TimerValueSource Snapshot(this TimerValueSource source, bool reset = false)
        {
            return new TimerValueSource(source.Name, new SnapshotMetricValueProvider<TimerValue>(source.ValueProvider, reset), source.Unit, source.RateUnit, source.DurationUnit, source.Tags);
        }
    }

    class SnapshotMetricValueProvider<T> : MetricValueProvider<T>
    {
        public SnapshotMetricValueProvider(MetricValueProvider<T> source, bool reset)
        {
            Value = source.GetValue(reset);
        }

        public T GetValue(bool resetMetric = false)
        {
            return Value;
        }

        public T Value { get; }
    }
}