param(
    [string]$Output = "publish\Cursor-INF-Maker-win-x64"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\CursorForge.csproj"
$nugetConfig = Join-Path $PSScriptRoot "src\NuGet.Config"
$outputPath = Join-Path $PSScriptRoot $Output
$env:APPDATA = Join-Path $PSScriptRoot "src"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --configfile $nugetConfig `
    -o $outputPath `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:PublishTrimmed=false `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published to: $outputPath"
