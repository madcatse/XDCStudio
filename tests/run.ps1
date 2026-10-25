param([switch]$SkipIntegration)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$version = ([xml](Get-Content -LiteralPath "$root/src/XdcConverter.csproj" -Raw)).Project.PropertyGroup.Version
$output = Join-Path $PSScriptRoot ('output/' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location $root
try {
    foreach ($suite in 'NativeCoreTests','OutputTransactionTests','TemporalContracts') {
        $suiteOutput = Join-Path $output $suite
        & $dotnet run --project "$PSScriptRoot/$suite" -c Release -- $suiteOutput | Tee-Object -FilePath "$output/$suite.txt"
        if ($LASTEXITCODE -ne 0) { throw "$suite failed; see $output" }
    }
    if (!$SkipIntegration) {
        & python "$PSScriptRoot/integration-smoke.py" "$root/dist/XdcStudio-$version-win-x64/XdcConverter.exe" "$output/integration"
        if ($LASTEXITCODE -ne 0) { throw "Integration failed; see $output" }
    }
    @{ Success = $true; Version = $version; Integration = !$SkipIntegration } | ConvertTo-Json | Set-Content -LiteralPath "$output/results.json" -Encoding utf8
    Write-Output "PASS: results in $output"
} finally { Pop-Location }
