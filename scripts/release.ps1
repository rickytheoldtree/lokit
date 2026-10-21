param(
    # 版本号，例如 1.2.0；必须比上一版大
    [Parameter(Mandatory = $true)]
    [string]$Version,
    # 上传到 GitHub Releases；不加时只在 release/velopack 生成安装包，便于本地试装
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$repoUrl = 'https://github.com/rickytheoldtree/lokit'
$publishDir = Join-Path $repositoryRoot 'release\publish'
$packDir = Join-Path $repositoryRoot 'release\velopack'
$notesPath = Join-Path $repositoryRoot "release-notes\$Version.md"

$token = $env:GITHUB_TOKEN
if (-not $token) { try { $token = (gh auth token 2>$null) } catch { } }
if ($Upload -and -not $token) { throw 'Set GITHUB_TOKEN or sign in with "gh auth login" first.' }

Push-Location $repositoryRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish Lokit.csproj -c Release -r win-x64 --self-contained false "-p:Version=$Version" -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    # 有上一版时生成增量包；第一次发布找不到上一版也没关系
    $downloadArgs = @('vpk', 'download', 'github', '--repoUrl', $repoUrl, '-o', $packDir)
    if ($token) { $downloadArgs += @('--token', $token) }
    dotnet @downloadArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning 'No previous release downloaded; building a full package only.' }

    $packArgs = @('vpk', 'pack',
        '--packId', 'Lokit',
        '--packVersion', $Version,
        '--packTitle', 'Lokit 本地化工具箱',
        '--packAuthors', 'rickytheoldtree',
        '--packDir', $publishDir,
        '--mainExe', 'Lokit.exe',
        '--icon', 'Assets\lokit.ico',
        '--runtime', 'win-x64',
        '--framework', 'net8-x64-desktop',
        '--outputDir', $packDir)
    if (Test-Path $notesPath) { $packArgs += @('--releaseNotes', $notesPath) }
    dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

    if (-not $Upload) {
        Write-Host "Packages are in $packDir"
        return
    }

    dotnet vpk upload github --repoUrl $repoUrl --token $token --outputDir $packDir `
        --tag "v$Version" --releaseName "v$Version" --publish
    if ($LASTEXITCODE -ne 0) { throw 'vpk upload failed.' }
    Write-Host "Released $repoUrl/releases/tag/v$Version"
}
finally {
    Pop-Location
}
