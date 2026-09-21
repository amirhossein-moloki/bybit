using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageReprocessingService
{
    Task<ReprocessingResultDto> ReprocessMessageAsync(
        Guid messageId,
        ReprocessMessageRequestDto request,
        CancellationToken cancellationToken = default);

    Task<List<MessageProcessingAttemptDto>> GetMessageAttemptsAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<ExplicitExecutionResultDto> ExecuteAttemptTradeAsync(
        Guid messageId,
        Guid attemptId,
        ExplicitExecutionRequestDto request,
        CancellationToken cancellationToken = default);
}
