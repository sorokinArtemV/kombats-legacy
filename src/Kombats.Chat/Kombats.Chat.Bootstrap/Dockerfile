FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Kombats.Chat/Kombats.Chat.Bootstrap/Kombats.Chat.Bootstrap.csproj", "src/Kombats.Chat/Kombats.Chat.Bootstrap/"]
COPY ["src/Kombats.Chat/Kombats.Chat.Api/Kombats.Chat.Api.csproj", "src/Kombats.Chat/Kombats.Chat.Api/"]
COPY ["src/Kombats.Chat/Kombats.Chat.Application/Kombats.Chat.Application.csproj", "src/Kombats.Chat/Kombats.Chat.Application/"]
COPY ["src/Kombats.Chat/Kombats.Chat.Domain/Kombats.Chat.Domain.csproj", "src/Kombats.Chat/Kombats.Chat.Domain/"]
COPY ["src/Kombats.Chat/Kombats.Chat.Infrastructure/Kombats.Chat.Infrastructure.csproj", "src/Kombats.Chat/Kombats.Chat.Infrastructure/"]
COPY ["src/Kombats.Chat/Kombats.Chat.Contracts/Kombats.Chat.Contracts.csproj", "src/Kombats.Chat/Kombats.Chat.Contracts/"]
COPY ["src/Kombats.Common/Kombats.Abstractions/Kombats.Abstractions.csproj", "src/Kombats.Common/Kombats.Abstractions/"]
COPY ["src/Kombats.Common/Kombats.Messaging/Kombats.Messaging.csproj", "src/Kombats.Common/Kombats.Messaging/"]
RUN dotnet restore "src/Kombats.Chat/Kombats.Chat.Bootstrap/Kombats.Chat.Bootstrap.csproj"
COPY . .
WORKDIR "/src/src/Kombats.Chat/Kombats.Chat.Bootstrap"
RUN dotnet build "./Kombats.Chat.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kombats.Chat.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kombats.Chat.Bootstrap.dll"]
