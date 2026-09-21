using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Models;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.IntegrationTests;
using TradingBot.Persistence.Context;
using Xunit;

namespace TradingBot.IntegrationTests.Telegram;

public class TelegramReprocessingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TelegramReprocessingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ValidDashboardReadToken");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task ReprocessMessageEndpoint_WithValidSeededMessage_ShouldReturnSuccessAndAttempt()
    {
        // 1. Seed TelegramMessage in DB
        using var scope = _factory.Services.CreateScope();
        var msgRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<TradingBot.Application.Repositories.IUnitOfWork>();

        var rawContent = @"💎 سیگنال
📊 جفت ارز: GBP/USD
📉 نوع معامله: خرید ( BUY)
📍 نقطه ورود: 1.33530
🎯 حد سود
تارگت اول: 1.33720
تارگت دوم: 1.33980
🛑 حد ضرر 1.33340";

        var telegramMsg = new TelegramMessage(
            channelId: -1001492324861,
            messageId: 48283,
            senderId: 10001,
            content: rawContent,
            receivedAt: DateTime.UtcNow
        );

        await msgRepo.AddAsync(telegramMsg);
        await unitOfWork.SaveChangesAsync();

        // 2. Act: POST /api/telegram/messages/{messageId}/reprocess
        var requestDto = new ReprocessMessageRequestDto(Mode: "REPROCESS_ONLY", TriggeredBy: "IntegrationTest");
        var response = await _client.PostAsJsonAsync($"/api/telegram/messages/{telegramMsg.Id}/reprocess", requestDto);

        // 3. Assert
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errStr = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP Status: {response.StatusCode}, Body: {errStr}");
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("success", json.GetProperty("status").GetString());

        var data = json.GetProperty("data");
        Assert.True(data.GetProperty("success").GetBoolean());
        Assert.True(data.TryGetProperty("attempt", out var attemptProp));
        Assert.Equal("GBPUSD", attemptProp.GetProperty("symbol").GetString());
        Assert.Equal("BUY", attemptProp.GetProperty("side").GetString());

        // 4. Act & Assert: GET attempts history
        var attemptsRes = await _client.GetAsync($"/api/telegram/messages/{telegramMsg.Id}/attempts");
        Assert.Equal(HttpStatusCode.OK, attemptsRes.StatusCode);

        var attemptsJson = await attemptsRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("success", attemptsJson.GetProperty("status").GetString());
        Assert.True(attemptsJson.GetProperty("data").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ReprocessMessageEndpoint_WithNonExistentMessageId_ShouldReturn404WithCorrelationId()
    {
        // Act
        var missingMessageId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync($"/api/telegram/messages/{missingMessageId}/reprocess", new ReprocessMessageRequestDto());

        // Assert
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var errStr = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP Status: {response.StatusCode}, Body: {errStr}");
        }
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error", json.GetProperty("status").GetString());
        Assert.Equal("NotFound", json.GetProperty("code").GetString());
        Assert.True(json.TryGetProperty("correlationId", out var corrId) && !string.IsNullOrEmpty(corrId.GetString()));
    }
}
