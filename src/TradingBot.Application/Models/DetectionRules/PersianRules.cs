namespace TradingBot.Application.Models;

using System.Collections.Generic;

public class PersianRules : LanguageRules
{
    public PersianRules()
    {
        LongKeywords = new List<string> { "خرید", "BUY", "LONG" };
        ShortKeywords = new List<string> { "فروش", "SELL", "SHORT" };
        PriceKeywords = new List<string> { "نقطه ورود", "ورود", "جفت ارز", "تارگت", "حد سود" };
        RiskKeywords = new List<string> { "حد ضرر", "استاپ", "تارگت اول", "تارگت دوم", "تارگت سوم" };
    }
}
