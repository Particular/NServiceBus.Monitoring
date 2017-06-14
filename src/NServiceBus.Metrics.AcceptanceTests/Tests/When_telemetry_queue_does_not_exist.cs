﻿namespace NServiceBus.Metrics.AcceptanceTests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Logging;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_telemetry_queue_does_not_exist : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_log_error()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>()
                .Done(c => c.Logs.Any(l => l.Level == LogLevel.Error))
                .Run()
                .ConfigureAwait(false);

            var logEntry = context.Logs.First(l => l.Level == LogLevel.Error);
            StringAssert.StartsWith("Error while sending metric data to", logEntry.Message);
        }

        class Context : ScenarioContext
        {
        }

        class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.EnableMetrics(TimeSpan.FromSeconds(1)).SendMetricDataToServiceControl("non-existing-queue");
                });
            }
        }
    }
}