using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Repositories;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class ParserTemplateRepository : IParserTemplateRepository
{
    private readonly TradingDbContext _context;

    public ParserTemplateRepository(TradingDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ParserTemplates>> GetAllEnabledAsync()
    {
        return await _context.ParserTemplates
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync();
    }
}
