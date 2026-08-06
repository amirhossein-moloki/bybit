using System.Threading.Tasks;
using TradingBot.Application.Models;

namespace TradingBot.Application.Interfaces;

public interface ISignalStorageService
{
    Task StoreAsync(
        SignalCandidate candidate
    );
}
