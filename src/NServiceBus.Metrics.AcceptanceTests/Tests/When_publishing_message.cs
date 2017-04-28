namespace NServiceBus.Metrics.AcceptanceTests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Extensibility;
    using Features;
    using global::Newtonsoft.Json.Linq;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using ObjectBuilder;
    using Pipeline;
    using Routing;
    using Transport;

    public class When_publishing_message : NServiceBusAcceptanceTest
    {
        static Guid HostId = Guid.NewGuid();

        [Test]
        public async Task Should_enhance_it_with_queue_length_properties()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Publisher>(c => c.When(ctx => ctx.SubscriptionCount == 2, async s =>
                  {
                      await s.Publish(new TestEventMessage1());
                      await s.Publish(new TestEventMessage1());
                      await s.Publish(new TestEventMessage2());
                      await s.Publish(new TestEventMessage2());
                  }))
                .WithEndpoint<Subscriber>(b => b.When(async (session, c) =>
                {
                    await session.Subscribe<TestEventMessage1>();
                    await session.Subscribe<TestEventMessage2>();
                }))
                .Done(c => c.Headers1.Count == 2 && c.Headers2.Count == 2)
                .Run()
                .ConfigureAwait(false);

            var sessionIds = new[] { AssertHeaders(context.Headers1), AssertHeaders(context.Headers2) };

            // assert data
            var data = JObject.Parse(context.Data);
            var counters = (JArray)data["Counters"];
            var counterTokens = counters.Where(c => c.Value<string>("Name").StartsWith("QueueLengthSend_"));

            foreach (var counter in counterTokens)
            {
                var name = counter.Value<string>("Name");
                var counterNameBasedSessionId = Guid.Parse(name.Substring(name.LastIndexOf("_") + 1));

                CollectionAssert.Contains(sessionIds, counterNameBasedSessionId);
                Assert.AreEqual(2, counter.Value<int>("Count"));
            }
        }

        static Guid AssertHeaders(IProducerConsumerCollection<IReadOnlyDictionary<string, string>> oneReceiverHeaders)
        {
            var headers = oneReceiverHeaders.ToArray();

            Guid sessionId1, sessionId2;
            long sequence1, sequence2;

            Parse(headers[0], out sessionId1, out sequence1);
            Parse(headers[1], out sessionId2, out sequence2);

            Assert.AreEqual(sessionId1, sessionId2);
            Assert.AreEqual(1, sequence1);
            Assert.AreEqual(2, sequence2);
            return sessionId1;
        }

        static void Parse(IReadOnlyDictionary<string, string> headers, out Guid sessionId, out long sequence)
        {
            var rawHeader = headers["NServiceBus.Metrics.QueueLength"];
            var parts = rawHeader.Split('_');
            sessionId = Guid.Parse(parts[0]);
            sequence = long.Parse(parts[1]);
        }

        class Context : ScenarioContext
        {
            public volatile int SubscriptionCount;
            public ConcurrentQueue<IReadOnlyDictionary<string, string>> Headers1 { get; } = new ConcurrentQueue<IReadOnlyDictionary<string, string>>();
            public ConcurrentQueue<IReadOnlyDictionary<string, string>> Headers2 { get; } = new ConcurrentQueue<IReadOnlyDictionary<string, string>>();
            public string Data { get; set; }
        }

        class Publisher : EndpointConfigurationBuilder
        {
            public Publisher()
            {
                EndpointSetup<DefaultServer>((c, r) =>
                {
                    var context = (Context)r.ScenarioContext;

                    c.UniquelyIdentifyRunningInstance().UsingCustomIdentifier(HostId);
                    c.OnEndpointSubscribed<Context>((s, ctx) =>
                    {
                        if (s.SubscriberReturnAddress.Contains("Subscriber"))
                        {
                            Interlocked.Increment(ref ctx.SubscriptionCount);
                        }
                    });

                    c.Pipeline.Register(new PreQueueLengthStep());
                    c.Pipeline.Register(new PostQueueLengthStep());

                    c.EnableMetrics().EnableCustomReport(payload =>
                    {
                        context.Data = payload;
                        return Task.FromResult(0);
                    }, TimeSpan.FromMilliseconds(5));
                });
            }
        }

        class Subscriber : EndpointConfigurationBuilder
        {
            public Subscriber()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.LimitMessageProcessingConcurrencyTo(1);
                    c.DisableFeature<AutoSubscribe>();

                    var routing = c.UseTransport<MsmqTransport>()
                        .Routing();
                    var publisher = AcceptanceTesting.Customization.Conventions.EndpointNamingConvention(typeof(Publisher));
                    routing.RegisterPublisher(typeof(TestEventMessage1), publisher);
                    routing.RegisterPublisher(typeof(TestEventMessage2), publisher);
                });
            }

            public class TestEventMessage1Handler : IHandleMessages<TestEventMessage1>
            {
                public Context TestContext { get; set; }

                public Task Handle(TestEventMessage1 message, IMessageHandlerContext context)
                {
                    TestContext.Headers1.Enqueue(context.MessageHeaders);

                    return Task.FromResult(0);
                }
            }

            public class TestEventMessage2Handler : IHandleMessages<TestEventMessage2>
            {
                public Context TestContext { get; set; }

                public Task Handle(TestEventMessage2 message, IMessageHandlerContext context)
                {
                    TestContext.Headers2.Enqueue(context.MessageHeaders);
                    return Task.FromResult(0);
                }
            }
        }

        public class TestEventMessage1 : IEvent
        {
        }

        public class TestEventMessage2 : IEvent
        {
        }

        class PreQueueLengthStep : RegisterStep
        {
            public PreQueueLengthStep()
                : base("PreQueueLengthStep", typeof(Behavior), "Registers behavior replacing context")
            {
                InsertBefore("QueueLengthBehavior");
            }

            class Behavior : IBehavior<IDispatchContext,IDispatchContext>
            {
                public Task Invoke(IDispatchContext context, Func<IDispatchContext, Task> next)
                {
                    return next(new MultiDispatchContext(context));
                }
            }
        }

        class PostQueueLengthStep : RegisterStep
        {
            public PostQueueLengthStep()
                : base("PostQueueLengthStep", typeof(Behavior), "Registers behavior restoring context")
            {
                InsertAfter("QueueLengthBehavior");
            }

            class Behavior : IBehavior<IDispatchContext, IDispatchContext>
            {
                public Task Invoke(IDispatchContext context, Func<IDispatchContext, Task> next)
                {
                    return next(((MultiDispatchContext) context).Original);
                }
            }
        }

        class MultiDispatchContext : IDispatchContext
        {
            public MultiDispatchContext(IDispatchContext original)
            {
                Extensions = original.Extensions;
                Builder = original.Builder;
                Operations = original.Operations.Select(t => new TransportOperation(t.Message, new MulticastAddressTag(Type.GetType(t.Message.Headers[Headers.EnclosedMessageTypes])), t.RequiredDispatchConsistency, t.DeliveryConstraints)).ToArray();
                Original = original;
            }

            public IDispatchContext Original { get; }
            public ContextBag Extensions { get; }
            public IBuilder Builder { get; }
            public IEnumerable<TransportOperation> Operations { get; }
        }
    }
}