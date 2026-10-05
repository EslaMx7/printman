using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using Printman.Core.Models;
using Printman.Services;

namespace Printman.Tests.Services;

[TestClass]
public sealed class PrintEventHubTests
{
    [TestMethod]
    public async Task Publish_DeliversEventToSubscriber()
    {
        var hub = new PrintEventHub();
        var reader = hub.Subscribe();

        hub.Publish(new PrintEvent { Type = "progress", Message = "hello" });

        var received = await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("progress", received.Type);
        Assert.AreEqual("hello", received.Message);
    }

    [TestMethod]
    public async Task Publish_DeliversToEverySubscriber()
    {
        var hub = new PrintEventHub();
        var first = hub.Subscribe();
        var second = hub.Subscribe();

        hub.Publish(new PrintEvent { Type = "completed", Message = "done" });

        Assert.AreEqual("done", (await first.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Message);
        Assert.AreEqual("done", (await second.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Message);
    }

    [TestMethod]
    public async Task Unsubscribe_CompletesReaderAndStopsDelivery()
    {
        var hub = new PrintEventHub();
        var reader = hub.Subscribe();

        hub.Unsubscribe(reader);
        hub.Publish(new PrintEvent { Type = "error", Message = "ignored" });

        Assert.IsFalse(reader.TryRead(out _));
        await reader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void Unsubscribe_UnknownReader_DoesNotThrow()
    {
        var hub = new PrintEventHub();
        var stranger = Channel.CreateUnbounded<PrintEvent>().Reader;

        hub.Unsubscribe(stranger);

        Assert.IsFalse(stranger.Completion.IsCompleted);
    }

    [TestMethod]
    public void GetRecentEvents_ReturnsNewestWithinRequestedCount()
    {
        var hub = new PrintEventHub();
        for (int i = 1; i <= 5; i++)
        {
            hub.Publish(new PrintEvent { Type = "progress", Message = $"m{i}" });
        }

        var recent = hub.GetRecentEvents(2);

        Assert.AreEqual(2, recent.Count);
        Assert.AreEqual("m4", recent[0].Message);
        Assert.AreEqual("m5", recent[1].Message);
    }

    [TestMethod]
    public void GetRecentEvents_DefaultMaxCount_IsTwenty()
    {
        var hub = new PrintEventHub();
        for (int i = 1; i <= 25; i++)
        {
            hub.Publish(new PrintEvent { Type = "progress", Message = $"m{i}" });
        }

        var recent = hub.GetRecentEvents();

        Assert.AreEqual(20, recent.Count);
        Assert.AreEqual("m6", recent[0].Message);
        Assert.AreEqual("m25", recent[^1].Message);
    }

    [TestMethod]
    public void Publish_TrimsHistoryAtInternalLimit()
    {
        var hub = new PrintEventHub();
        for (int i = 1; i <= 60; i++)
        {
            hub.Publish(new PrintEvent { Type = "progress", Message = $"m{i}" });
        }

        var all = hub.GetRecentEvents(200);

        Assert.AreEqual(50, all.Count);
        Assert.AreEqual("m11", all[0].Message);
        Assert.AreEqual("m60", all[^1].Message);
    }

    [TestMethod]
    public void Publish_ClosedSubscriberChannel_IsDropped()
    {
        var hub = new PrintEventHub();
        var subscribers = GetSubscribers(hub);

        var channel = Channel.CreateBounded<PrintEvent>(1);
        channel.Writer.TryComplete();
        subscribers[channel.Reader] = channel.Writer;

        hub.Publish(new PrintEvent { Type = "error", Message = "closed" });

        Assert.IsTrue(SpinWait.SpinUntil(() => !subscribers.ContainsKey(channel.Reader), TimeSpan.FromSeconds(5)));
        Assert.IsTrue(channel.Reader.Completion.IsCompleted);
    }

    private static ConcurrentDictionary<ChannelReader<PrintEvent>, ChannelWriter<PrintEvent>> GetSubscribers(PrintEventHub hub) =>
        (ConcurrentDictionary<ChannelReader<PrintEvent>, ChannelWriter<PrintEvent>>)typeof(PrintEventHub)
            .GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hub)!;
}
