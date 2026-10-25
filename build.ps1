param([switch]$Package, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$version = ([xml](Get-Content -LiteralPath "$root/src/XdcConverter.csproj" -Raw)).Project.PropertyGroup.Version
$destination = Join-Path $root "dist/XdcStudio-$version-win-x64"
$appDirectory = Join-Path $destination 'app'
$docsDirectory = Join-Path $destination 'docs'
if (!$NoRestore) {
    & $dotnet restore (Join-Path $root 'src/XdcConverter.csproj') --configfile (Join-Path $root 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
}
# A folder deployment avoids single-file runtime extraction into TEMP.
& $dotnet publish (Join-Path $root 'src/XdcConverter.csproj') -c Release -o $appDirectory --no-restore --self-contained true -p:PublishSingleFile=false -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$ffmpeg = Join-Path $root '.tools/ffmpeg/ffmpeg-9.0.2-essentials_build'
New-Item -ItemType Directory -Force -Path "$appDirectory/tools/ffmpeg","$appDirectory/tools/xdc","$appDirectory/licenses" | Out-Null
Copy-Item -LiteralPath "$ffmpeg/bin/ffmpeg.exe","$ffmpeg/bin/ffprobe.exe" -Destination "$appDirectory/tools/ffmpeg" -Force
Copy-Item -LiteralPath "$root/compiled/XDC_PLAY.EXE" -Destination "$appDirectory/tools/xdc" -Force
Copy-Item -LiteralPath "$root/LICENSE" -Destination "$appDirectory/licenses/XDC-MIT.txt" -Force
Copy-Item -LiteralPath "$ffmpeg/LICENSE" -Destination "$appDirectory/licenses/FFmpeg-GPL.txt" -Force
Copy-Item -LiteralPath "$ffmpeg/README.txt" -Destination "$appDirectory/licenses/FFmpeg-build-README.txt" -Force
Copy-Item -LiteralPath "$root/.tools/dotnet/LICENSE.txt" -Destination "$appDirectory/licenses/DotNet-MIT.txt" -Force
Copy-Item -LiteralPath "$root/.tools/dotnet/ThirdPartyNotices.txt" -Destination "$appDirectory/licenses/DotNet-ThirdPartyNotices.txt" -Force
New-Item -ItemType Directory -Force -Path $docsDirectory | Out-Null
# Standard .NET apphost points directly at the managed entry point in app/.
# No wrapper process, extraction to TEMP, or dependency on a system .NET install.
Add-Type -Path (Join-Path $root '.tools/dotnet/sdk/10.0.402/Microsoft.NET.HostModel.dll')
$template = Join-Path $root '.tools/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/10.0.13/runtimes/win-x64/native/apphost.exe'
[Microsoft.NET.HostModel.AppHost.HostWriter]::CreateAppHost($template, (Join-Path $destination 'XdcConverter.exe'), 'app/XdcConverter.dll', $true, (Join-Path $appDirectory 'XdcConverter.exe'), $false, $false, $null)
# Remove only the redundant published apphost after its icon/resources were copied.
Remove-Item -LiteralPath (Join-Path $appDirectory 'XdcConverter.exe') -Force
foreach ($file in 'README.txt','README.en.txt','THIRD-PARTY.txt','TEST-RESULTS.txt') { if (Test-Path "$root/$file") { $target = if ($file -in 'README.txt','README.en.txt') { $destination } else { $docsDirectory }; Copy-Item -LiteralPath "$root/$file" -Destination $target -Force } }
$manifest = Get-ChildItem "$appDirectory/tools" -File -Recurse | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($destination.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$appDirectory/components.sha256.json" -Encoding utf8
foreach ($name in 'help.ru.html','help.en.html','help.css','CGA-NTSC.txt') { Copy-Item -LiteralPath "$root/docs/$name" -Destination $docsDirectory -Force }
if ($Package) {
    $zip = Join-Path $root "dist/XdcStudio-$version-win-x64-portable.zip"
    Compress-Archive -Path "$destination/*" -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
}
