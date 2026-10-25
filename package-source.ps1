$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath "$root/src/XdcConverter.csproj" -Raw)).Project.PropertyGroup.Version
New-Item -ItemType Directory -Force -Path "$root/dist" | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $root "dist/XdcStudio-$version-source.zip"
$stream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
$zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    # Explicit source roots exclude local media, credentials, caches and old builds.
    $files = @(Get-ChildItem -LiteralPath $root -File | Where-Object {
        $_.Extension -in '.md','.txt','.ps1','.json','.Config','.PAS','.scr' -or
        $_.Name -in 'LICENSE','.gitignore','.gitattributes'
    })
    foreach ($directory in 'src','assets','docs','tests','.github','compiled','palette') {
        $files += Get-ChildItem -LiteralPath (Join-Path $root $directory) -File -Recurse | Where-Object {
            $relative = $_.FullName.Substring($root.Length + 1).Replace('\','/')
            $relative -notmatch '(^|/)(bin|obj|__pycache__|output|results|input)(/|$)' -and
            ($directory -ne 'tests' -or $_.Extension -in '.cs','.csproj','.py','.ps1','.md')
        }
    }
    foreach ($file in $files | Sort-Object FullName -Unique) {
        $name = $file.FullName.Substring($root.Length + 1).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
