param(
    [switch]$SkipUi
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    dotnet format whitespace Lokit.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Code format verification failed.' }

    dotnet build Lokit.sln -c Release --no-restore -p:StrictBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    dotnet run --no-build --no-restore --project Tests\EditingRegression\EditingRegression.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Data regression failed.' }

    if (-not $SkipUi) {
        dotnet run --no-build --no-restore --project Tests\EditingRegression\EditingRegression.csproj -c Release -- --ui
        if ($LASTEXITCODE -ne 0) { throw 'WPF UI regression failed.' }
    }
}
finally {
    Pop-Location
}
