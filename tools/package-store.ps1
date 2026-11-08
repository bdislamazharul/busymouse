<#
.SYNOPSIS
  Builds the Microsoft Store package: dist\BusyMouse-<version>-store.msixbundle, x64 and ARM64 in one file.

.DESCRIPTION
  Upload the .msixbundle in Partner Center (Apps and games > BusyMouse > Submission > Packages). It's unsigned on
  purpose: the Store signs it. To install it on your own PC first, sign it with a test certificate or use
  Visual Studio (see README).

  Before the first upload, put the identity from Partner Center (Product management > Product identity) into
  BusyMouse.WinUI\Package.appxmanifest: Identity Name, Identity Publisher and PublisherDisplayName.

  Needs: the .NET 10 SDK. MakeAppx comes from the Microsoft.Windows.SDK.BuildTools NuGet package the project uses.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\package-store.ps1
  powershell -ExecutionPolicy Bypass -File tools\package-store.ps1 -Version 1.0.1
#>
param (
  # Overrides the <Version> in the project file, e.g. from a release tag.
  [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'BusyMouse.WinUI'
$manifestPath = Join-Path $project 'Package.appxmanifest'
$dist = Join-Path $root 'dist'
$csproj = [xml] (Get-Content (Join-Path $project 'BusyMouse.WinUI.csproj'))
$version = if ($Version) { $Version.TrimStart('v') } else { $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }

function Step ($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check ($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

# The Store wants four parts with the last one 0: 1.2.3 -> 1.2.3.0.
$parts = @($version -split '\.' | Select-Object -First 3)
while ($parts.Count -lt 3) { $parts += '0' }
$packageVersion = ($parts + '0') -join '.'

$original = [IO.File]::ReadAllText($manifestPath)
if ($original -match 'REPLACE') {
  Write-Warning 'Package.appxmanifest still has placeholder identity values. The package builds, but Partner Center will reject it until you copy in the values from Product management > Product identity.'
}

New-Item -ItemType Directory -Force $dist | Out-Null
$staging = Join-Path $project 'bin\publish\store-bundle'
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

try {
  # Version the manifest for this build only; put it back afterwards.
  [IO.File]::WriteAllText($manifestPath, ($original -replace '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$packageVersion`${2}"))

  foreach ($platform in 'x64', 'ARM64') {
    Step "Building the Store package $packageVersion ($platform)"
    $out = Join-Path $project "bin\publish\$platform\store-package"
    Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
    dotnet publish $project -c Release "-p:Platform=$platform" -p:PublishProfile=Store "-p:Version=$version" -nologo
    Check "Store package ($platform)"

    $msix = Get-ChildItem $out -Recurse -Filter '*.msix' | Select-Object -First 1
    if (-not $msix) { throw "No .msix was built for $platform." }
    Copy-Item $msix.FullName (Join-Path $staging "BusyMouse_${packageVersion}_$($platform.ToLowerInvariant()).msix")
  }
}
finally {
  [IO.File]::WriteAllText($manifestPath, $original)
}

Step 'Bundling'
# MakeAppx from the Windows SDK build tools package the project restores, matching this PC's processor.
$sdkVersion = ($csproj.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools').Version
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$makeappx = Get-ChildItem (Join-Path $nuget "microsoft.windows.sdk.buildtools\$sdkVersion\bin") -Recurse -Filter makeappx.exe |
  Where-Object { $_.Directory.Name -eq $hostArch } | Select-Object -First 1
if (-not $makeappx) { throw "MakeAppx not found under $nuget\microsoft.windows.sdk.buildtools\$sdkVersion." }

$bundle = Join-Path $dist "BusyMouse-$version-store.msixbundle"
Remove-Item $bundle -ErrorAction SilentlyContinue
& $makeappx.FullName bundle /d $staging /p $bundle /bv $packageVersion /o
Check 'MakeAppx bundle'

Step 'Done'
'{0,8:N1} MB  {1}' -f ((Get-Item $bundle).Length / 1MB), (Split-Path $bundle -Leaf)
