using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Application.Models;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface IPositionGateway
{
    Task<List<ExchangePositionDto>> GetOpenPositionsAsync();

    Task<ExchangePositionDto?> GetPositionAsync(
        string symbol,
        PositionSide side
    );
}
