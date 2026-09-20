using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.Interfaces;

public interface IPositionManagementHandler
{
    Task<PositionManagementResult> ProcessPositionManagementAsync(
        MessageAnalysis analysis,
        TelegramMessage message,
        CancellationToken cancellationToken = default);
}
