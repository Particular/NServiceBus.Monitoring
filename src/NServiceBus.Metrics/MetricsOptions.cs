﻿namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using global::Metrics;
    using global::Metrics.Reports;
    using Hosting;
    using Logging;
    using ObjectBuilder;
    using Transport;

    /// <summary>
    /// Provides configuration options for Metrics feature
    /// </summary>
    public class MetricsOptions
    {
        /// <summary>
        /// Enables sending periodic updates of metric data to ServiceControl
        /// </summary>
        /// <param name="serviceControlMetricsAddress">The transport address of the ServiceControl instance</param>
        /// <param name="interval">How frequently metric data is sent to ServiceControl</param>
        [Obsolete("Not for public use.")]
        public void SendMetricDataToServiceControl(string serviceControlMetricsAddress, TimeSpan interval)
        {
            Guard.AgainstNullAndEmpty(nameof(serviceControlMetricsAddress), serviceControlMetricsAddress);
            Guard.AgainstNegativeAndZero(nameof(interval), interval);

            reportInstallers.Add((builder, config) => config.WithReport(
                new NServiceBusMetricReport(builder.Build<IDispatchMessages>(), serviceControlMetricsAddress, builder.Build<HostInformation>()),
                interval
            ));
        }

        /// <summary>
        /// Enables sending metric data to the trace log
        /// </summary>
        /// <param name="interval">How often metric data is sent to the trace log</param>
        public void EnableMetricTracing(TimeSpan interval)
        {
            Guard.AgainstNegativeAndZero(nameof(interval), interval);

            reportInstallers.Add((builder, config) => config.WithReport(
                new TraceReport(),
                interval
            ));
        }

        /// <summary>
        /// Enables sending metric data to the NServiceBus log
        /// </summary>
        /// <param name="interval">How often metric data is sent to the log</param>
        /// <param name="logLevel">Level at which log entries should be written. Default is DEBUG.</param>
        public void EnableLogTracing(TimeSpan interval, LogLevel logLevel = LogLevel.Debug)
        {
            Guard.AgainstNegativeAndZero(nameof(interval), interval);

            reportInstallers.Add((builder, config) => config.WithReport(
                new MetricsLogReport(logLevel),
                interval
            ));
        }

        /// <summary>
        /// Enables custom report, allowing to consume data by any func.
        /// </summary>
        /// <param name="func">A function that will be called with a raw JSON.</param>
        /// <param name="interval">How often metric data is sent to the log</param>
        public void EnableCustomReport(Func<string, Task> func, TimeSpan interval)
        {
            Guard.AgainstNull(nameof(func), func);
            Guard.AgainstNegativeAndZero(nameof(interval), interval);

            reportInstallers.Add((builder, config) => config.WithReport(
                new CustomReport(func),
                interval
            ));
        }

        internal void SetUpReports(MetricsConfig config, IBuilder builder)
        {
            config.WithReporting(reportsConfig => reportInstallers.ForEach(installer => installer(builder, reportsConfig)));
        }

        List<Action<IBuilder, MetricsReports>> reportInstallers = new List<Action<IBuilder, MetricsReports>>();
    }
}
