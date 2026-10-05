using System.Collections.Concurrent;
using System.Threading.Channels;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class PrintEventHub : IPrintEventHub
{
    private readonly ConcurrentDictionary<ChannelReader<PrintEvent>, ChannelWriter<PrintEvent>> _subscribers = new();
    private readonly object _historyLock = new();
    private readonly List<PrintEvent> _history = [];
    private const int MaxHistory = 50;

    public ChannelReader<PrintEvent> Subscribe()
    {
        // Use bounded channel with DropOldest to prevent slow clients from buffering memory
        var channel = Channel.CreateBounded<PrintEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers[channel.Reader] = channel.Writer;
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<PrintEvent> reader)
    {
        if (_subscribers.TryRemove(reader, out var writer))
        {
            writer.TryComplete();
        }
    }

    public void Publish(PrintEvent printEvent)
    {
        lock (_historyLock)
        {
            _history.Add(printEvent);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
            }
        }

        foreach (var (reader, writer) in _subscribers)
        {
            if (!writer.TryWrite(printEvent))
            {
                // Slow or closed reader
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await writer.WriteAsync(printEvent);
                    }
                    catch
                    {
                        Unsubscribe(reader);
                    }
                });
            }
        }
    }

    public IReadOnlyList<PrintEvent> GetRecentEvents(int maxCount = 20)
    {
        lock (_historyLock)
        {
            return _history.TakeLast(maxCount).ToList();
        }
    }
}
