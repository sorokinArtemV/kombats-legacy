param(
    [string]$Root = "."
)

$excludedDirs = @(
    "bin",
    "obj",
    ".git",
    ".vs",
    ".idea",
    ".vscode",
    "node_modules",
    "packages",
    "TestResults",
    "artifacts",
    "out",
    "publish",
    "dist",
    "coverage"
)

function Show-Tree {
    param(
        [string]$Path,
        [string]$Indent = ""
    )

    $items = Get-ChildItem -LiteralPath $Path -Force |
        Where-Object {
            if ($_.PSIsContainer) {
                $_.Name -notin $excludedDirs
            }
            else {
                $true
            }
        } |
        Sort-Object @{ Expression = { -not $_.PSIsContainer } }, Name

    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $isLast = $i -eq ($items.Count - 1)

        $branch = if ($isLast) { "└── " } else { "├── " }
        Write-Output "$Indent$branch$($item.Name)"

        if ($item.PSIsContainer) {
            $nextIndent = if ($isLast) { "$Indent    " } else { "$Indent│   " }
            Show-Tree -Path $item.FullName -Indent $nextIndent
        }
    }
}

$resolvedRoot = Resolve-Path $Root
Write-Output $resolvedRoot.Path
Show-Tree -Path $resolvedRoot.Path