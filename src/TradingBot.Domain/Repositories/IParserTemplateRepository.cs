using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Repositories;

public interface IParserTemplateRepository
{
    Task<IReadOnlyList<ParserTemplates>> GetAllEnabledAsync();
}
