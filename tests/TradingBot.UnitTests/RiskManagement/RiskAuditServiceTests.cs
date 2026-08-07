using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskAuditServiceTests
{
    private readonly Mock<ISystemLogRepository> _mockLogRepo = new();
    private readonly RiskAuditService _auditService;

    public RiskAuditServiceTests()
    {
        _auditService = new RiskAuditService(_mockLogRepo.Object, NullLogger<RiskAuditService>.Instance);
    }

    [Fact]
    public async Task RecordEvaluationStartedAsync_ShouldAddSystemLogEntry()
    {
        // Arrange
        var signalId = Guid.NewGuid();

        // Act
        await _auditService.RecordEvaluationStartedAsync(signalId);

        // Assert
        _mockLogRepo.Verify(r => r.AddAsync(
            It.Is<SystemLog>(log => log.Category == "Audit" && log.Message.Contains("Evaluation Started")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRulesExecutedAsync_ShouldAddSystemLogEntry()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var rules = new[] { "Rule1", "Rule2" };

        // Act
        await _auditService.RecordRulesExecutedAsync(signalId, rules);

        // Assert
        _mockLogRepo.Verify(r => r.AddAsync(
            It.Is<SystemLog>(log => log.Category == "Audit" && log.Message.Contains("Rule1, Rule2")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordRuleFailuresAsync_ShouldAddSystemLogEntry()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var failures = new[] { "Fail1" };

        // Act
        await _auditService.RecordRuleFailuresAsync(signalId, failures);

        // Assert
        _mockLogRepo.Verify(r => r.AddAsync(
            It.Is<SystemLog>(log => log.Category == "Audit" && log.Message.Contains("Fail1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFinalDecisionAsync_ShouldAddSystemLogEntry()
    {
        // Arrange
        var signalId = Guid.NewGuid();

        // Act
        await _auditService.RecordFinalDecisionAsync(signalId, RiskDecisionStatus.Approved, "All rules passed.");

        // Assert
        _mockLogRepo.Verify(r => r.AddAsync(
            It.Is<SystemLog>(log => log.Category == "Audit" && log.Message.Contains("Approved") && log.Message.Contains("All rules passed.")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordProcessingDurationAsync_ShouldAddSystemLogEntry()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var duration = TimeSpan.FromMilliseconds(50);

        // Act
        await _auditService.RecordProcessingDurationAsync(signalId, duration);

        // Assert
        _mockLogRepo.Verify(r => r.AddAsync(
            It.Is<SystemLog>(log => log.Category == "Audit" && log.Message.Contains("50 ms")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
