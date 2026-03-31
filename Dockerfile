# QuantTrading Core — Multi-stage Docker Build
# 參考 NoFx 的容器化部署設計

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY quant-trading-core-dotnet.sln .
COPY src/QuantTrading.Core/QuantTrading.Core.csproj src/QuantTrading.Core/
COPY src/QuantTrading.Infrastructure/QuantTrading.Infrastructure.csproj src/QuantTrading.Infrastructure/
COPY src/QuantTrading.Web/QuantTrading.Web.csproj src/QuantTrading.Web/
COPY src/QuantTrading.Worker/QuantTrading.Worker.csproj src/QuantTrading.Worker/
COPY tests/QuantTrading.Core.Tests/QuantTrading.Core.Tests.csproj tests/QuantTrading.Core.Tests/
COPY tests/QuantTrading.E2E.Tests/QuantTrading.E2E.Tests.csproj tests/QuantTrading.E2E.Tests/

RUN dotnet restore

# Copy all source and build
COPY . .
RUN dotnet publish src/QuantTrading.Web/QuantTrading.Web.csproj -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create data directory for JSON persistence
RUN mkdir -p /app/Data

COPY --from=build /app/publish .

# Non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "QuantTrading.Web.dll"]
