using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Validation.Rules;

public class SymbolValidationRule : IValidationRule
{
    private readonly IRepository<Symbol> _symbolRepository;
    private readonly IOptions<ValidationOptions> _options;

    public SymbolValidationRule(IRepository<Symbol> symbolRepository, IOptions<ValidationOptions> options)
    {
        _symbolRepository = symbolRepository ?? throw new ArgumentNullException(nameof(symbolRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var symbol = context.ParsedSignal.Symbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add("Symbol is empty or missing.");
            result.FailedRules.Add(nameof(SymbolValidationRule));
            return;
        }

        var symbolClean = symbol.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(symbolClean, "^[A-Z0-9]{3,20}$"))
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add($"Symbol format is invalid: '{symbol}'.");
            result.FailedRules.Add(nameof(SymbolValidationRule));
            return;
        }

        if (_options.Value.RejectUnknownSymbols)
        {
            var allSymbols = await _symbolRepository.GetAllAsync();
            var exists = allSymbols.Any(s => s.SymbolCode.Equals(symbolClean, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add($"Symbol '{symbolClean}' is not a supported trading pair.");
                result.FailedRules.Add(nameof(SymbolValidationRule));
                return;
            }
        }
    }
}
