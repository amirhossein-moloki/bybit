using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces;

public interface ITelegramSourceService
{
    Task<List<TelegramSourceDto>> GetSourcesAsync(TelegramSourceFilterDto filter, CancellationToken ct = default);
    Task<TelegramSourceDto?> GetSourceByIdAsync(Guid id, CancellationToken ct = default);
    Task<TelegramSourceDto> CreateSourceAsync(CreateTelegramSourceDto dto, CancellationToken ct = default);
    Task<TelegramSourceDto> UpdateSourceAsync(Guid id, UpdateTelegramSourceDto dto, CancellationToken ct = default);
    Task<bool> DeleteSourceAsync(Guid id, CancellationToken ct = default);
    Task<SyncSourcesResultDto> SyncSourcesAsync(CancellationToken ct = default);
    Task<List<TelegramMessagePreviewDto>> GetSourceMessagesAsync(Guid id, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<List<TelegramSignalPreviewDto>> GetSourceSignalsAsync(Guid id, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<TelegramSourceHealthDto> GetSourceHealthAsync(Guid id, CancellationToken ct = default);
    Task<TestSourceResultDto> TestSourceAsync(Guid id, CancellationToken ct = default);
    Task<int> BulkUpdateSourcesAsync(BulkUpdateSourcesDto dto, CancellationToken ct = default);
    Task<List<TelegramSource>> GetActiveSourcesAsync(CancellationToken ct = default);
}
