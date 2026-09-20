using System.Threading.Channels;
using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Core.Abstractions;

public interface IPrintEventHub
{
    /// <summary>
    /// Subscribes an SSE client to the real-time event stream.
    /// </summary>
    ChannelReader<PrintEvent> Subscribe();

    /// <summary>
    /// Unsubscribes an SSE client channel.
    /// </summary>
    void Unsubscribe(ChannelReader<PrintEvent> reader);

    /// <summary>
    /// Broadcasts a print event to all subscribed clients.
    /// </summary>
    void Publish(PrintEvent printEvent);

    /// <summary>
    /// Gets recent history of events to populate client UI on initial connection.
    /// </summary>
    IReadOnlyList<PrintEvent> GetRecentEvents(int maxCount = 20);
}
