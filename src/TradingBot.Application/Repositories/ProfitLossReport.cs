using System;

namespace TradingBot.Application.Repositories;

public class ProfitLossReport
{
    public decimal TotalProfitLoss { get; set; }
    public decimal TotalFee { get; set; }
    public int TotalTrades { get; set; }
    public int WinTrades { get; set; }
    public int LossTrades { get; set; }
    public decimal WinRate { get; set; }
}
