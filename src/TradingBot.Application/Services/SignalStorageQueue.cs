using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;

namespace TradingBot.Application.Services;

public class SignalStorageQueue : ISignalStorageQueue
{
    private readonly Channel<SignalCandidate> _channel;

    public SignalStorageQueue()
    {
        var options = new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateUnbounded<SignalCandidate>(options);
    }

    public ValueTask EnqueueAsync(SignalCandidate candidate, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(candidate, cancellationToken);
    }

    public ValueTask<SignalCandidate> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
