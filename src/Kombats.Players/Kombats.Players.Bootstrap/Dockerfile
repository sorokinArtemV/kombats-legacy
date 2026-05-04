FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Kombats.Players/Kombats.Players.Bootstrap/Kombats.Players.Bootstrap.csproj", "src/Kombats.Players/Kombats.Players.Bootstrap/"]
COPY ["src/Kombats.Players/Kombats.Players.Api/Kombats.Players.Api.csproj", "src/Kombats.Players/Kombats.Players.Api/"]
COPY ["src/Kombats.Players/Kombats.Players.Application/Kombats.Players.Application.csproj", "src/Kombats.Players/Kombats.Players.Application/"]
COPY ["src/Kombats.Players/Kombats.Players.Domain/Kombats.Players.Domain.csproj", "src/Kombats.Players/Kombats.Players.Domain/"]
COPY ["src/Kombats.Players/Kombats.Players.Infrastructure/Kombats.Players.Infrastructure.csproj", "src/Kombats.Players/Kombats.Players.Infrastructure/"]
COPY ["src/Kombats.Players/Kombats.Players.Contracts/Kombats.Players.Contracts.csproj", "src/Kombats.Players/Kombats.Players.Contracts/"]
COPY ["src/Kombats.Common/Kombats.Abstractions/Kombats.Abstractions.csproj", "src/Kombats.Common/Kombats.Abstractions/"]
COPY ["src/Kombats.Common/Kombats.Messaging/Kombats.Messaging.csproj", "src/Kombats.Common/Kombats.Messaging/"]
RUN dotnet restore "src/Kombats.Players/Kombats.Players.Bootstrap/Kombats.Players.Bootstrap.csproj"
COPY . .
WORKDIR "/src/src/Kombats.Players/Kombats.Players.Bootstrap"
RUN dotnet build "./Kombats.Players.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kombats.Players.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kombats.Players.Bootstrap.dll"]
