FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Kombats.Bff/Kombats.Bff.Bootstrap/Kombats.Bff.Bootstrap.csproj", "src/Kombats.Bff/Kombats.Bff.Bootstrap/"]
COPY ["src/Kombats.Bff/Kombats.Bff.Api/Kombats.Bff.Api.csproj", "src/Kombats.Bff/Kombats.Bff.Api/"]
COPY ["src/Kombats.Bff/Kombats.Bff.Application/Kombats.Bff.Application.csproj", "src/Kombats.Bff/Kombats.Bff.Application/"]
RUN dotnet restore "src/Kombats.Bff/Kombats.Bff.Bootstrap/Kombats.Bff.Bootstrap.csproj"
COPY . .
WORKDIR "/src/src/Kombats.Bff/Kombats.Bff.Bootstrap"
RUN dotnet build "./Kombats.Bff.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kombats.Bff.Bootstrap.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kombats.Bff.Bootstrap.dll"]
