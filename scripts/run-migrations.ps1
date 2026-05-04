param(
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    if (-not [string]::IsNullOrWhiteSpace($env:KOMBATS_POSTGRES_CONNECTION)) {
        $ConnectionString = $env:KOMBATS_POSTGRES_CONNECTION
    }
    else {
        $ConnectionString = "Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres"
    }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Kombats Migration Runner ==="
Write-Host "Repo root: $RepoRoot"
Write-Host ""

function Apply-Migrations {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][string]$BootstrapProject,
        [Parameter(Mandatory = $true)][string]$InfrastructureProject
    )

    Write-Host "--- Applying migrations for $ServiceName ---"

    $startupProjectPath = Join-Path $RepoRoot $BootstrapProject
    $infrastructureProjectPath = Join-Path $RepoRoot $InfrastructureProject

    dotnet ef database update `
        --startup-project $startupProjectPath `
        --project $infrastructureProjectPath `
        --connection $ConnectionString `
        --verbose

    if ($LASTEXITCODE -ne 0) {
        throw "Migration failed for $ServiceName."
    }

    Write-Host "--- $ServiceName migrations complete ---"
    Write-Host ""
}

Apply-Migrations `
    -ServiceName "Players" `
    -BootstrapProject "src/Kombats.Players/Kombats.Players.Bootstrap" `
    -InfrastructureProject "src/Kombats.Players/Kombats.Players.Infrastructure"

Apply-Migrations `
    -ServiceName "Matchmaking" `
    -BootstrapProject "src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap" `
    -InfrastructureProject "src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure"

Apply-Migrations `
    -ServiceName "Battle" `
    -BootstrapProject "src/Kombats.Battle/Kombats.Battle.Bootstrap" `
    -InfrastructureProject "src/Kombats.Battle/Kombats.Battle.Infrastructure"

Apply-Migrations `
    -ServiceName "Chat" `
    -BootstrapProject "src/Kombats.Chat/Kombats.Chat.Bootstrap" `
    -InfrastructureProject "src/Kombats.Chat/Kombats.Chat.Infrastructure"

Write-Host "=== All migrations applied successfully ==="
