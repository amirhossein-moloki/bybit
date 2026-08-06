namespace TradingBot.Application.Interfaces;

public interface ISignalStorageMetrics
{
    long SignalsReceived { get; }
    long SignalsStored { get; }
    long DuplicatesIgnored { get; }
    long StorageFailures { get; }

    void IncrementSignalsReceived();
    void IncrementSignalsStored();
    void IncrementDuplicatesIgnored();
    void IncrementStorageFailures();
}
