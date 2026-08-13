# Multi-stage Dockerfile for TradingBot

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files first for caching
COPY src/TradingBot.sln src/
COPY src/TradingBot.Domain/TradingBot.Domain.csproj src/TradingBot.Domain/
COPY src/TradingBot.Application/TradingBot.Application.csproj src/TradingBot.Application/
COPY src/TradingBot.Infrastructure/TradingBot.Infrastructure.csproj src/TradingBot.Infrastructure/
COPY src/TradingBot.Exchange.Bybit/TradingBot.Exchange.Bybit.csproj src/TradingBot.Exchange.Bybit/
COPY src/TradingBot.Worker/TradingBot.Worker.csproj src/TradingBot.Worker/
COPY src/TradingBot.Persistence/TradingBot.Persistence.csproj src/TradingBot.Persistence/
COPY src/TradingBot.Telegram/TradingBot.Telegram.csproj src/TradingBot.Telegram/
COPY src/TradingBot.Parser/TradingBot.Parser.csproj src/TradingBot.Parser/
COPY tests/TradingBot.UnitTests/TradingBot.UnitTests.csproj tests/TradingBot.UnitTests/
COPY tests/TradingBot.IntegrationTests/TradingBot.IntegrationTests.csproj tests/TradingBot.IntegrationTests/

# Restore dependencies
RUN dotnet restore src/TradingBot.sln

# Copy the remaining files
COPY src/ src/

# Build and publish
WORKDIR /app/src/TradingBot.Worker
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose ports
EXPOSE 80
EXPOSE 443

# Start the application
ENTRYPOINT ["dotnet", "TradingBot.Worker.dll"]
