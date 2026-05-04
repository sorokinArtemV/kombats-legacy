FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj", "src/Kombats.Battle/Kombats.Battle.Bootstrap/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Api/Kombats.Battle.Api.csproj", "src/Kombats.Battle/Kombats.Battle.Api/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Application/Kombats.Battle.Application.csproj", "src/Kombats.Battle/Kombats.Battle.Application/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Domain/Kombats.Battle.Domain.csproj", "src/Kombats.Battle/Kombats.Battle.Domain/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Infrastructure/Kombats.Battle.Infrastructure.csproj", "src/Kombats.Battle/Kombats.Battle.Infrastructure/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Contracts/Kombats.Battle.Contracts.csproj", "src/Kombats.Battle/Kombats.Battle.Contracts/"]
COPY ["src/Kombats.Battle/Kombats.Battle.Realtime.Contracts/Kombats.Battle.Realtime.Contracts.csproj", "src/Kombats.Battle/Kombats.Battle.Realtime.Contracts/"]
COPY ["src/Kombats.Common/Kombats.Abstractions/Kombats.Abstractions.csproj", "src/Kombats.Common/Kombats.Abstractions/"]
COPY ["src/Kombats.Common/Kombats.Messaging/Kombats.Messaging.csproj", "src/Kombats.Common/Kombats.Messaging/"]
RUN dotnet restore "src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj"
COPY . .
WORKDIR "/src/src/Kombats.Battle/Kombats.Battle.Bootstrap"
RUN dotnet build "./Kombats.Battle.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kombats.Battle.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kombats.Battle.Bootstrap.dll"]
