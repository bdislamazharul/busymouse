<#
.SYNOPSIS
  Builds both BusyMouse bundles and their installers into dist\.

.DESCRIPTION
  with runtime   bin\publish\<platform>\with-runtime   .NET included; runs anywhere.
  no runtime     bin\publish\<platform>\no-runtime     app\ + a native launcher (BusyMouse.exe) that installs .NET if missing.

  Output in dist\: a portable .zip and a setup .exe for each.

  Needs: the .NET 10 SDK; the Visual Studio C++ Build Tools ("Desktop development with C++") for the launcher;
  Inno Setup 6 for the installers (winget install JRSoftware.InnoSetup). Missing tools are skipped with a warning.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\package.ps1
  powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Platform ARM64 -Trim
#>
param (
  [ValidateSet ('x64', 'ARM64')] [string] $Platform = 'x64',
  # Strip unused .NET code from the "with runtime" bundle. Much smaller; test the app afterwards.
  [switch] $Trim,
  # Overrides the <Version> in the project file, e.g. from a release tag.
  [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'BusyMouse.WinUI'
$publish = Join-Path $project "bin\publish\$Platform"
$dist = Join-Path $root 'dist'
$arch = $Platform.ToLowerInvariant()
$version = if ($Version) { $Version.TrimStart('v') } else { ([xml] (Get-Content (Join-Path $project 'BusyMouse.WinUI.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }

function Step ($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check ($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

# ---- the two bundles
Step "Publishing BusyMouse $version ($Platform), no runtime"
dotnet publish $project -c Release "-p:Platform=$Platform" -p:PublishProfile=NoRuntime "-p:Version=$version" -nologo
Check 'Publish (no runtime)'

Step "Publishing BusyMouse $version ($Platform), with runtime$(if ($Trim) { ', trimmed' })"
dotnet publish $project -c Release "-p:Platform=$Platform" -p:PublishProfile=WithRuntime "-p:PublishTrimmed=$($Trim.IsPresent)" "-p:Version=$version" -nologo
Check 'Publish (with runtime)'

# ---- the launcher for the no-runtime bundle
Step 'Building the launcher'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
$launcherBuilt = $false

if (-not $vs) {
  Write-Warning 'Visual Studio C++ Build Tools not found, so the no-runtime bundle has no launcher and is skipped. Install "Desktop development with C++".'
}
else {
  $source = Join-Path $root 'launcher'
  $obj = Join-Path $source "obj\$Platform"
  New-Item -ItemType Directory -Force $obj | Out-Null
  $parts = ($version -split '\.') + @(0, 0, 0, 0) | Select-Object -First 4
  Set-Content (Join-Path $obj 'version.h') "#define VER_COMMA $($parts -join ',')`r`n#define VER_TEXT `"$version`"" -Encoding Ascii

  $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
  $vcArch = if ($hostArch -eq $arch) { $arch } else { "${hostArch}_$arch" }
  $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
  $exe = Join-Path $publish 'no-runtime\BusyMouse.exe'

  Push-Location $source
  try {
    cmd /c "call `"$vcvars`" $vcArch >nul && rc /nologo /I `"$obj`" /fo `"$obj\launcher.res`" launcher.rc && cl /nologo /O1 /MT /EHsc /W4 /std:c++17 /Fo`"$obj\\`" launcher.cpp `"$obj\launcher.res`" /Fe`"$exe`" /link /SUBSYSTEM:WINDOWS"
    Check 'Launcher build'
    $launcherBuilt = $true
  }
  finally { Pop-Location }
}

# ---- portable zips
Step 'Zipping'
$flavors = @(@{ Name = 'WithRuntime'; Folder = 'with-runtime'; Suffix = '' })
if ($launcherBuilt) { $flavors += @{ Name = 'NoRuntime'; Folder = 'no-runtime'; Suffix = '-no-runtime' } }

foreach ($f in $flavors) {
  $zip = Join-Path $dist "BusyMouse-$version-$arch-portable$($f.Suffix).zip"
  Remove-Item $zip -ErrorAction SilentlyContinue
  Compress-Archive -Path (Join-Path $publish "$($f.Folder)\*") -DestinationPath $zip
  Write-Host $zip
}

# ---- installers
Step 'Building installers'
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  Write-Warning 'Inno Setup 6 not found, so no installers. Install it with: winget install JRSoftware.InnoSetup'
}
else {
  foreach ($f in $flavors) {
    & $iscc /Q "/DFlavor=$($f.Name)" "/DPlatform=$Platform" "/DAppVersion=$version" "/DSource=$(Join-Path $publish $f.Folder)" "/DOutputDir=$dist" (Join-Path $root 'installer\BusyMouse.iss')
    Check "Installer ($($f.Name))"
  }
}

Step 'Done'
Get-ChildItem $dist -File | Where-Object Name -like "BusyMouse-$version-$arch-*" | ForEach-Object { '{0,8:N1} MB  {1}' -f ($_.Length / 1MB), $_.Name }
