param(
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation falhou com código $LASTEXITCODE."
    }
}

if ($Restore) {
    dotnet restore Nfe-Agendamento.sln
    Assert-LastExitCode 'dotnet restore'
}

$reportPath = Join-Path ([System.IO.Path]::GetTempPath()) 'nfe-nuget-vulnerabilities.json'
dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json |
    Out-File -FilePath $reportPath -Encoding utf8
Assert-LastExitCode 'auditoria de dependências NuGet'

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$findings = @()
foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0) {
                $findings += "$($project.path): $($package.id) $($package.resolvedVersion)"
            }
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | ForEach-Object { Write-Error $_ }
    throw 'Dependências NuGet vulneráveis encontradas.'
}

dotnet test Nfe-Agendamento.sln -c Release --no-restore
Assert-LastExitCode 'dotnet test'

$jsTests = @(
    'tests/js/product-mapping-regression.test.js',
    'tests/js/lookup-feedback-regression.test.js',
    'tests/js/portal-fallback-regression.test.js',
    'tests/js/pairing-lookup-regression.test.js',
    'tests/js/batch-lookup-regression.test.js',
    'tests/js/release-readiness-regression.test.js',
    'tests/js/audit-hardening-regression.test.js'
)

foreach ($test in $jsTests) {
    node $test
    Assert-LastExitCode "node $test"
}

dotnet build Nfe-Agendamento.sln -c Release --no-restore
Assert-LastExitCode 'dotnet build'
