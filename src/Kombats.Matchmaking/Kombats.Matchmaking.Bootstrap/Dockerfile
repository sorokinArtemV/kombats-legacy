FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Kombats.Matchmaking.Bootstrap.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/"]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Api/Kombats.Matchmaking.Api.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Api/"]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Application/Kombats.Matchmaking.Application.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Application/"]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Domain/Kombats.Matchmaking.Domain.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Domain/"]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Kombats.Matchmaking.Infrastructure.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/"]
COPY ["src/Kombats.Matchmaking/Kombats.Matchmaking.Contracts/Kombats.Matchmaking.Contracts.csproj", "src/Kombats.Matchmaking/Kombats.Matchmaking.Contracts/"]
COPY ["src/Kombats.Common/Kombats.Abstractions/Kombats.Abstractions.csproj", "src/Kombats.Common/Kombats.Abstractions/"]
COPY ["src/Kombats.Common/Kombats.Messaging/Kombats.Messaging.csproj", "src/Kombats.Common/Kombats.Messaging/"]
RUN dotnet restore "src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Kombats.Matchmaking.Bootstrap.csproj"
COPY . .
WORKDIR "/src/src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap"
RUN dotnet build "./Kombats.Matchmaking.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kombats.Matchmaking.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kombats.Matchmaking.Bootstrap.dll"]
