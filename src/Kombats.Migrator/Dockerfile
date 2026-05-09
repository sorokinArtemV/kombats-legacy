# src/Kombats.Migrator/Dockerfile

FROM mcr.microsoft.com/dotnet/sdk:10.0

# psql for Keycloak DB bootstrap
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*

# dotnet-ef tool for running migrations
RUN dotnet tool install --global dotnet-ef --version 10.*
ENV PATH="${PATH}:/root/.dotnet/tools"

WORKDIR /app

# Build coordination files at the repo root — apply to every project below them.
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy common projects first — services reference them via ProjectReference.
COPY src/Kombats.Common        ./src/Kombats.Common

# Copy source for the four services that have databases.
COPY src/Kombats.Players       ./src/Kombats.Players
COPY src/Kombats.Matchmaking   ./src/Kombats.Matchmaking
COPY src/Kombats.Battle        ./src/Kombats.Battle
COPY src/Kombats.Chat          ./src/Kombats.Chat

# Restore once at build time so runtime is faster.
RUN dotnet restore src/Kombats.Players/Kombats.Players.Bootstrap     \
 && dotnet restore src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap \
 && dotnet restore src/Kombats.Battle/Kombats.Battle.Bootstrap       \
 && dotnet restore src/Kombats.Chat/Kombats.Chat.Bootstrap

# Migration runner
COPY scripts/run-migrations.sh ./scripts/run-migrations.sh
RUN chmod +x ./scripts/run-migrations.sh

ENTRYPOINT ["./scripts/run-migrations.sh"]
