param([switch]$IncludeTestTools)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force -Path "$root/downloads","$root/.tools" | Out-Null
$packages = @(
    @{Name='dotnet-sdk';Url='https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip';Hash='ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd';Algorithm='SHA512';Destination='dotnet'},
    @{Name='dosbox-x';Url='https://github.com/joncampbell123/dosbox-x/releases/download/dosbox-x-v2026.10.01/dosbox-x-mingw64-2026.10.01-portable.zip';Hash='DEF2D84D4415FC26C77252839B4DAFB6B476E3A4204B896292E6B05505CA8C59';Algorithm='SHA256';Destination='dosbox-x'},
    @{Name='ffmpeg-mirror';Url='https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip';Hash='60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA';Algorithm='SHA256';Destination='ffmpeg'}
)
foreach ($package in $packages) {
    if ($package.Name -eq 'dosbox-x' -and !$IncludeTestTools) { continue }
    $zip = Join-Path $root ('downloads/' + $package.Name + '.zip')
    if (!(Test-Path -LiteralPath $zip)) { Invoke-WebRequest $package.Url -OutFile $zip -TimeoutSec 900 }
    if ((Get-FileHash -LiteralPath $zip -Algorithm $package.Algorithm).Hash -ne $package.Hash) { throw "Hash mismatch: $zip" }
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root ('.tools/' + $package.Destination)) -Force
    Write-Output ($package.Name + ' verified and extracted')
}
if (!(Test-Path -LiteralPath "$root/compiled/XDC_PLAY.EXE")) {
    throw 'Original XDC player is missing. Clone the complete fork, including compiled/.'
}
