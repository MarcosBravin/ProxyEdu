param(
    [ValidateSet('restore', 'build', 'publish', 'clean')]
    [string]$Action = 'publish',

    [ValidateSet('all', 'server', 'client', 'shared')]
    [string]$Target = 'all',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [string]$OutputRoot = '.\artifacts\publish',
    [bool]$CleanOutput = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projects = @{
    server = '.\ProxyEdu.Server\ProxyEdu.Server.csproj'
    client = '.\ProxyEdu.Client\ProxyEdu.Client.csproj'
    shared = '.\ProxyEdu.Shared\ProxyEdu.Shared.csproj'
}

function Get-Targets([string]$selectedTarget) {
    if ($selectedTarget -eq 'all') {
        return @('shared', 'server', 'client')
    }

    return @($selectedTarget)
}

function Invoke-Dotnet([string[]]$arguments) {
    Write-Host "dotnet $($arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar: dotnet $($arguments -join ' ')"
    }
}

Push-Location $PSScriptRoot
try {
    $targets = Get-Targets -selectedTarget $Target

    foreach ($item in $targets) {
        $projectPath = $projects[$item]
        Write-Host ""
        Write-Host "==> $Action [$item]" -ForegroundColor Yellow

        switch ($Action) {
            'restore' {
                Invoke-Dotnet @('restore', $projectPath)
            }
            'build' {
                Invoke-Dotnet @('build', $projectPath, '-c', $Configuration, '--nologo')
            }
            'publish' {
                $outputDir = Join-Path $OutputRoot $item
                if ($CleanOutput -and (Test-Path $outputDir)) {
                    Write-Host "Removendo build antiga em $outputDir" -ForegroundColor DarkYellow
                    Remove-Item -Path $outputDir -Recurse -Force
                }
                Invoke-Dotnet @(
                    'publish', $projectPath,
                    '-c', $Configuration,
                    '-r', $Runtime,
                    '--self-contained', $SelfContained.ToString().ToLowerInvariant(),
                    '-o', $outputDir,
                    '--nologo'
                )
            }
            'clean' {
                Invoke-Dotnet @('clean', $projectPath, '-c', $Configuration, '--nologo')
            }
        }
    }

    Write-Host ""
    Write-Host "Concluido com sucesso." -ForegroundColor Green
}
finally {
    Pop-Location
}
