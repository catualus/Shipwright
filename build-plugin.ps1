<#
.SYNOPSIS
    Packages Shipwright as a Compile Pal plugin.

.DESCRIPTION
    Produces artifacts/Shipwright/, a folder a user drops into their Compile Pal/Plugins directory.
    Compile Pal discovers it from the meta.json alone - there is nothing to register and no build of
    Compile Pal involved, which is the point: someone who does not want a compile step that can
    publish to the Workshop simply does not have the folder.

    Published self-contained and single-file so the folder is a handful of files rather than two
    hundred, and so it runs on a machine with no .NET installed. Compile Pal itself ships
    self-contained, so its users have no reason to have a runtime.

    NOT trimmed, unlike Meshwright. System.Text.Json is used for addon.json and for reading the
    Workshop lookup response, and its reflection-based paths are exactly what trimming removes: the
    failure would be at run time, on the one step that must not fail halfway, and it would look like
    a Steam problem rather than a build one. If the executable size ever justifies revisiting this,
    the way to do it is source generated serialisation and a trimmed publish that reports no
    warnings - not TrimMode partial and a hope.

.PARAMETER Zip
    Also write artifacts/Shipwright-plugin.zip, which is the form to attach to a release.

.PARAMETER Version
    Stamps the executable with a version. Left off, the build carries whatever the SDK defaults to,
    which is fine for a local build and not fine for something attached to a release: the release
    workflow passes the tag through here so one number is the source of both.
#>
[CmdletBinding()]
param(
    [switch]$Zip,

    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$staging = Join-Path $root 'artifacts/publish'
$out = Join-Path $root 'artifacts/Shipwright'

# The folder name is load-bearing: Compile Pal matches it against meta.json's "Name" to decide
# whether a step is already registered, and a mismatch loads the plugin under a name its parameters
# do not belong to.
$source = Join-Path $root 'CompilePalPlugin/Shipwright'

Write-Host 'Publishing shipwright...' -ForegroundColor Cyan

[string[]]$publishArgs = @(
    'publish'
    (Join-Path $root 'ShipwrightCli/ShipwrightCli.csproj')
    '--configuration', 'Release'
    '--runtime', 'win-x64'
    '--self-contained', 'true'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-warnaserror'
    '--output', $staging
)

if ($Version) { $publishArgs += "-p:Version=$Version" }

dotnet @publishArgs

if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Copy-Item (Join-Path $staging 'shipwright.exe') $out

# Not the .pdb: it is larger than the executable and a plugin folder is something people copy around.
Copy-Item (Join-Path $source 'meta.json') $out
Copy-Item (Join-Path $source 'parameters.json') $out
Copy-Item (Join-Path $root 'LICENSE') $out
Copy-Item (Join-Path $source 'README.md') $out -ErrorAction SilentlyContinue

$size = (Get-ChildItem $out -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ''
Write-Host "Plugin written to $out ($([math]::Round($size, 1)) MB)" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { Write-Host "  $($_.Name)" }

if ($Zip) {
    $archive = Join-Path $root 'artifacts/Shipwright-plugin.zip'
    if (Test-Path $archive) { Remove-Item $archive -Force }

    # Compressing the folder itself, not its contents, so the zip contains a Shipwright/ directory -
    # extracting it straight into Plugins/ then lands in the right place.
    Compress-Archive -Path $out -DestinationPath $archive
    Write-Host "Archive written to $archive" -ForegroundColor Green
}

Write-Host ''
Write-Host 'To install: copy the Shipwright folder into your Compile Pal "Plugins" directory,'
Write-Host 'then restart Compile Pal and add the Shipwright step to a preset.'
Write-Host 'It starts as a dry run. Nothing is uploaded until "Actually publish" is enabled.'
