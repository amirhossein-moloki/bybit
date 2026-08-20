using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.IntegrationTests;
using TradingBot.Persistence.Context;
using Xunit;

namespace TradingBot.IntegrationTests.Telegram;

public class TelegramSourcesEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public TelegramSourcesEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ValidDashboardReadToken");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetSources_ShouldReturnSuccessAndData()
    {
        // Act
        var response = await _client.GetAsync("/api/telegram/sources");

        // Assert
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errStr = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP Status: {response.StatusCode}, Body: {errStr}");
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ApiResponse<List<TelegramSourceDto>>>();
        Assert.NotNull(content);
        Assert.Equal("success", content.Status);
        Assert.NotNull(content.Data);
    }

    [Fact]
    public async Task CreateAndUpdateSource_ShouldPersistAndApplyChanges()
    {
        // 1. Create Source via repository
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramSourceRepository>();

        long chatId = DateTime.UtcNow.Ticks;
        var source = new TelegramSource(chatId, "Integration Test Channel", "@integration_test");
        await repo.AddAsync(source);

        // 2. GET single source
        var getRes = await _client.GetAsync($"/api/telegram/sources/{source.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getObj = await getRes.Content.ReadFromJsonAsync<ApiResponse<TelegramSourceDto>>();
        Assert.NotNull(getObj?.Data);
        Assert.Equal("Integration Test Channel", getObj.Data.Title);

        // 3. PATCH source capabilities
        var patchReq = new UpdateTelegramSourceDto(IsEnabled: true, ListenForSignals: false, ProcessMessages: true);
        var patchRes = await _client.PatchAsJsonAsync($"/api/telegram/sources/{source.Id}", patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var updatedObj = await patchRes.Content.ReadFromJsonAsync<ApiResponse<TelegramSourceDto>>();
        Assert.NotNull(updatedObj?.Data);
        Assert.False(updatedObj.Data.ListenForSignals);

        // 4. Test Source endpoint
        var testRes = await _client.PostAsync($"/api/telegram/sources/{source.Id}/test", null);
        Assert.Equal(HttpStatusCode.OK, testRes.StatusCode);

        // 5. DELETE source
        var delRes = await _client.DeleteAsync($"/api/telegram/sources/{source.Id}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
    }

    [Fact]
    public async Task UnauthorizedAccess_ShouldReturn401()
    {
        // Arrange client without auth token
        var unauthedClient = _factory.CreateClient();

        // Act
        var response = await unauthedClient.GetAsync("/api/telegram/sources");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private class ApiResponse<T>
    {
        public string Status { get; set; } = string.Empty;
        public T? Data { get; set; }
    }
}
