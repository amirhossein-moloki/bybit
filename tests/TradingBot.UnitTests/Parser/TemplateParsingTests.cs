using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Repositories;
using TradingBot.Parser;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Extractors;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Templates;
using Xunit;

namespace TradingBot.UnitTests.Parser;

public class TemplateParsingTests
{
    private readonly DefaultSignalTemplate _defaultTemplate = new();
    private readonly IOptions<ParserTemplatesOptions> _enabledDbOptions = Options.Create(new ParserTemplatesOptions { EnableDatabaseTemplates = true });
    private readonly IOptions<ParserTemplatesOptions> _disabledDbOptions = Options.Create(new ParserTemplatesOptions { EnableDatabaseTemplates = false });
    private readonly IOptions<ParserOptions> _parserOptions = Options.Create(new ParserOptions());

    [Fact]
    public void FromEntity_ShouldDeserializeJsonRulesCorrectly()
    {
        // Arrange
        var entity = new ParserTemplates
        {
            Id = Guid.NewGuid(),
            Name = "Crypto VIP",
            ChannelId = 12345,
            Enabled = true,
            ConfigurationJson = @"[
                {""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":1}
            ]"
        };

        // Act
        var template = SignalTemplate.FromEntity(entity);

        // Assert
        template.Should().NotBeNull();
        template.Id.Should().Be(entity.Id);
        template.Name.Should().Be("Crypto VIP");
        template.ChannelId.Should().Be(12345);
        template.Enabled.Should().BeTrue();
        template.Rules.Should().HaveCount(1);
        template.Rules[0].Field.Should().Be("EntryPrice");
        template.Rules[0].Pattern.Should().Be("BUY AREA");
        template.Rules[0].Required.Should().BeTrue();
    }

    [Fact]
    public async Task TemplateManager_ShouldFallbackToDefaultTemplate_WhenNoDbTemplateMatches()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>());

        var manager = new TemplateManager(
            _enabledDbOptions,
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var context = new ParserContext(Guid.NewGuid(), "BTC LONG", "99999", DateTime.UtcNow, "1.0");

        // Act
        var result = await manager.FindTemplateAsync(context);

        // Assert
        result.Should().BeOfType<DefaultSignalTemplate>();
    }

    [Fact]
    public async Task TemplateManager_ShouldIgnoreDisabledTemplates()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>
        {
            new ParserTemplates
            {
                Name = "Disabled Template",
                ChannelId = 12345,
                Enabled = false, // DISABLED
                ConfigurationJson = "[]"
            }
        });

        var manager = new TemplateManager(
            _enabledDbOptions,
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var context = new ParserContext(Guid.NewGuid(), "BTC LONG", "12345", DateTime.UtcNow, "1.0");

        // Act
        var result = await manager.FindTemplateAsync(context);

        // Assert
        result.Should().BeOfType<DefaultSignalTemplate>(); // Should fallback because matches are disabled or empty
    }

    [Fact]
    public async Task TemplateManager_ShouldIgnoreDbTemplates_WhenDbTemplatesAreDisabledInConfiguration()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>
        {
            new ParserTemplates
            {
                Name = "Channel-Specific Template",
                ChannelId = 12345,
                Enabled = true,
                ConfigurationJson = "[]"
            }
        });

        var manager = new TemplateManager(
            _disabledDbOptions, // DB TEMPLATES DISABLED!
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var context = new ParserContext(Guid.NewGuid(), "BTC LONG", "12345", DateTime.UtcNow, "1.0");

        // Act
        var result = await manager.FindTemplateAsync(context);

        // Assert
        result.Should().BeOfType<DefaultSignalTemplate>();
    }

    [Fact]
    public async Task TemplateManager_ShouldSelectChannelSpecificTemplate_OverGenericTemplate()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>
        {
            new ParserTemplates
            {
                Id = Guid.NewGuid(),
                Name = "Generic Template",
                ChannelId = null, // Generic
                Enabled = true,
                ConfigurationJson = @"[{""Field"":""EntryPrice"",""Pattern"":""BUY ZONE"",""Extractor"":""EntryExtractor""}]"
            },
            new ParserTemplates
            {
                Id = Guid.NewGuid(),
                Name = "Channel-Specific Template",
                ChannelId = 12345, // Channel specific
                Enabled = true,
                ConfigurationJson = @"[{""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor""}]"
            }
        });

        var manager = new TemplateManager(
            _enabledDbOptions,
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var context = new ParserContext(Guid.NewGuid(), "BTC LONG BUY AREA: 60000", "12345", DateTime.UtcNow, "1.0");

        // Act
        var result = await manager.FindTemplateAsync(context);

        // Assert
        result.Should().BeOfType<SignalTemplate>();
        var selected = (SignalTemplate)result!;
        selected.Name.Should().Be("Channel-Specific Template");
    }

    [Fact]
    public async Task Pipeline_ShouldRespectCustomTemplatePatterns_WhenChannelTemplateMatches()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        var customTemplateRulesJson = @"[
            {""Field"":""Symbol"",""Pattern"":"""",""Extractor"":""SymbolExtractor"",""Required"":true,""Order"":1},
            {""Field"":""Side"",""Pattern"":"""",""Extractor"":""DirectionExtractor"",""Required"":true,""Order"":2},
            {""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":3},
            {""Field"":""StopLoss"",""Pattern"":""STOP"",""Extractor"":""StopLossExtractor"",""Required"":true,""Order"":4},
            {""Field"":""TakeProfits"",""Pattern"":""TARGET"",""Extractor"":""TakeProfitExtractor"",""Required"":true,""Order"":5}
        ]";

        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>
        {
            new ParserTemplates
            {
                Id = Guid.NewGuid(),
                Name = "Channel B Template",
                ChannelId = 67890,
                Enabled = true,
                ConfigurationJson = customTemplateRulesJson
            }
        });

        var manager = new TemplateManager(
            _enabledDbOptions,
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var extractors = new List<ISignalExtractor>
        {
            new SymbolExtractor(),
            new DirectionExtractor(),
            new EntryExtractor(),
            new StopLossExtractor(),
            new TakeProfitExtractor(),
            new LeverageExtractor()
        };

        var pipeline = new SignalParserPipeline(extractors, _parserOptions, NullLogger<SignalParserPipeline>.Instance, manager);

        // This signal uses custom keywords: BUY AREA, STOP, TARGET
        var messageText = @"
            🔥 LONG SOL-USDT
            BUY AREA: 150.50
            STOP: 145.00
            TARGET1: 160.00
            TARGET2: 170.00
        ";

        var context = new ParserContext(Guid.NewGuid(), messageText, "67890", DateTime.UtcNow, "1.0");

        // Act
        var parsedSignal = await pipeline.ExecuteAsync(context);

        // Assert
        parsedSignal.Should().NotBeNull();
        parsedSignal.Symbol.Should().Be("SOLUSDT");
        parsedSignal.Side.Should().Be(OrderSide.Buy);
        parsedSignal.EntryPrice.Should().Be(150.50m);
        parsedSignal.StopLoss.Should().Be(145.00m);
        parsedSignal.TakeProfits.Should().HaveCount(2).And.ContainInOrder(160.00m, 170.00m);
        parsedSignal.Errors.Should().BeEmpty();
        parsedSignal.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Pipeline_ShouldRecordWarning_WhenRequiredTemplateRuleFieldIsMissing()
    {
        // Arrange
        var repositoryMock = new Mock<IParserTemplateRepository>();
        var customTemplateRulesJson = @"[
            {""Field"":""Symbol"",""Pattern"":"""",""Extractor"":""SymbolExtractor"",""Required"":true,""Order"":1},
            {""Field"":""Side"",""Pattern"":"""",""Extractor"":""DirectionExtractor"",""Required"":true,""Order"":2},
            {""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":3}
        ]";

        repositoryMock.Setup(r => r.GetAllEnabledAsync()).ReturnsAsync(new List<ParserTemplates>
        {
            new ParserTemplates
            {
                Id = Guid.NewGuid(),
                Name = "Required Field Missing Template",
                ChannelId = 88888,
                Enabled = true,
                ConfigurationJson = customTemplateRulesJson
            }
        });

        var manager = new TemplateManager(
            _enabledDbOptions,
            NullLogger<TemplateManager>.Instance,
            _defaultTemplate,
            repositoryMock.Object
        );

        var extractors = new List<ISignalExtractor>
        {
            new SymbolExtractor(),
            new DirectionExtractor(),
            new EntryExtractor()
        };

        var pipeline = new SignalParserPipeline(extractors, _parserOptions, NullLogger<SignalParserPipeline>.Instance, manager);

        // This message is missing the required BUY AREA field
        var messageText = "🔥 LONG SOL-USDT";

        var context = new ParserContext(Guid.NewGuid(), messageText, "88888", DateTime.UtcNow, "1.0");

        // Act
        var parsedSignal = await pipeline.ExecuteAsync(context);

        // Assert
        parsedSignal.Should().NotBeNull();
        parsedSignal.Symbol.Should().Be("SOLUSDT");
        parsedSignal.Side.Should().Be(OrderSide.Buy);
        parsedSignal.EntryPrice.Should().BeNull();
        parsedSignal.Warnings.Should().ContainSingle()
            .Which.Should().Be("Required template field EntryPrice was not extracted.");
    }
}
