using System.Threading;
using TradingBot.Application.Interfaces;

namespace TradingBot.Application.Services;

public class SignalStorageMetrics : ISignalStorageMetrics
{
    private long _signalsReceived;
    private long _signalsStored;
    private long _duplicatesIgnored;
    private long _storageFailures;

    public long SignalsReceived => Volatile.Read(ref _signalsReceived);
    public long SignalsStored => Volatile.Read(ref _signalsStored);
    public long DuplicatesIgnored => Volatile.Read(ref _duplicatesIgnored);
    public long StorageFailures => Volatile.Read(ref _storageFailures);

    public void IncrementSignalsReceived()
    {
        Interlocked.Increment(ref _signalsReceived);
    }

    public void IncrementSignalsStored()
    {
        Interlocked.Increment(ref _signalsStored);
    }

    public void IncrementDuplicatesIgnored()
    {
        Interlocked.Increment(ref _duplicatesIgnored);
    }

    public void IncrementStorageFailures()
    {
        Interlocked.Increment(ref _storageFailures);
    }
}
