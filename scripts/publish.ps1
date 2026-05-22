# Publish a self-contained, single-directory release build.
# Output lands in: hybrid\publish\
param(
    [switch]$Run   # pass -Run to launch after publish
)

$ErrorActionPreference = 'Stop'
$root   = Split-Path $PSScriptRoot -Parent   # repo root
$hybrid = Join-Path $root 'hybrid'
$proj   = Join-Path $hybrid 'src\AgentGroupChat.Hybrid\AgentGroupChat.Hybrid.csproj'
$out    = Join-Path $hybrid 'publish'

Write-Host "Publishing to $out ..." -ForegroundColor Cyan

dotnet publish $proj `
    -c Release `
    -f net10.0-windows10.0.19041.0 `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:WindowsPackageType=None `
    -o $out

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "`nDone. Output: $out" -ForegroundColor Green
Write-Host "Run with:  & '$out\AgentGroupChat.Hybrid.exe'" -ForegroundColor Yellow

if ($Run) {
    & "$out\AgentGroupChat.Hybrid.exe"
}
