using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IAIProvider
{
    Task<string> AnalyzeAsync(string prompt, CancellationToken token);
}
