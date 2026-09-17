#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, verifies and publishes mon-switch.

.DESCRIPTION
    One script for the whole release flow:
      1. makes sure Resources\app.ico exists (regenerating it when missing),
      2. compiles Release,
      3. runs "mon-switch.exe --check-lang" so a half-translated language pack
         can never reach a release,
      4. publishes two flavours:
           framework-dependent  - tiny, needs the .NET 8 Desktop Runtime
           self-contained       - no runtime needed, single file, with the unused
                                  half of the desktop shared framework (WPF) removed
      5. prints the on-disk size of both, which is what the README quotes.

    Add -Compress to also emit a DEFLATE-compressed single file. It roughly halves
    the download but multiplies resident memory, so it is off by default for a tray
    utility that is meant to sit idle all day. See README > "發布體積優化".

.PARAMETER Configuration
    Build configuration. Release by default.

.PARAMETER Runtime
    Target RID for the self-contained build.

.PARAMETER OutputRoot
    Where the published folders go. Defaults to <repo>\artifacts.

.EXAMPLE
    pwsh -File build.ps1
    pwsh -File build.ps1 -SkipSelfContained
    pwsh -File build.ps1 -Compress
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [string] $OutputRoot,

    [switch] $SkipLangCheck,

    [switch] $SkipFrameworkDependent,

    [switch] $SkipSelfContained,

    [switch] $SkipInstaller,

    [switch] $Compress
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'src\mon-switch\mon-switch.csproj'
$icon = Join-Path $repoRoot 'src\mon-switch\Resources\app.ico'

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'artifacts'
}

function Resolve-DotNet {
    # A machine can perfectly well have "dotnet" on PATH and still have no SDK: the
    # .NET *Runtime* installer also creates C:\Program Files\dotnet\dotnet.exe. That
    # host answers "dotnet --info" happily but fails on "dotnet build" with
    # "The application 'build' does not exist". So every candidate is probed for a
    # real SDK before it is accepted.
    $candidates = @()

    $fromPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($fromPath) {
        $candidates += $fromPath.Source
    }

    # Fallbacks for machines where the SDK was installed per user rather than system wide.
    $candidates += @(
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
        (Join-Path $env:USERPROFILE '.workbuddy\binaries\dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        'C:\Program Files\dotnet\dotnet.exe'
    )

    foreach ($candidate in $candidates) {
        if (-not $candidate -or -not (Test-Path $candidate)) {
            continue
        }

        $sdks = @(& $candidate --list-sdks 2>$null)
        if ($sdks.Count -gt 0) {
            return $candidate
        }
    }

    throw 'No .NET SDK found. Note that the .NET Runtime alone is not enough - "dotnet build" needs the SDK. Install the .NET 8 SDK from https://aka.ms/dotnet/download and run this script again.'
}

function Invoke-Step {
    param([string] $Label, [scriptblock] $Action)

    Write-Host ''
    Write-Host "==> $Label" -ForegroundColor Cyan

    # Out-String is not decoration: mon-switch.exe is a GUI-subsystem binary, and PowerShell
    # does not reliably block on one. Piping the output forces PowerShell to consume the
    # process's stdout, which is what makes $LASTEXITCODE trustworthy afterwards.
    $output = & $Action 2>&1 | Out-String
    if ($output.Trim().Length -gt 0) {
        Write-Host $output.Trim()
    }

    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Get-FolderSize {
    param([string] $Path)

    if (-not (Test-Path $Path)) {
        return 0
    }

    $sum = (Get-ChildItem -Path $Path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [int64] $sum
}

$dotnet = Resolve-DotNet
Write-Host "mon-switch build" -ForegroundColor Green
Write-Host "  dotnet  : $dotnet"
Write-Host "  project : $project"
Write-Host "  runtime : $Runtime"
Write-Host "  output  : $OutputRoot"

# 1. icon -------------------------------------------------------------------------
if (-not (Test-Path $icon)) {
    $generator = Join-Path $repoRoot 'tools\make_icon.py'
    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        throw "The icon $icon is missing and no 'python' is on PATH to regenerate it. Run tools\make_icon.py, or drop your own .ico in place."
    }

    Invoke-Step 'Generating Resources\app.ico' { & $python.Source $generator }
}

# 2. build ------------------------------------------------------------------------
$binRoot = Join-Path $repoRoot "src\mon-switch\bin\$Configuration\net8.0-windows"
$exe = Join-Path $binRoot 'mon-switch.exe'

Invoke-Step "Building $Configuration" {
    & $dotnet build $project -c $Configuration -v minimal -nologo
}

# 3. language pack check ----------------------------------------------------------
if (-not $SkipLangCheck) {
    Invoke-Step 'Checking language packs' {
        & $exe --check-lang
    }
}
else {
    Write-Host ''
    Write-Host '==> Language pack check skipped' -ForegroundColor Yellow
}

# 4. publish ----------------------------------------------------------------------
$frameworks = @()

if (-not $SkipFrameworkDependent) {
    $target = Join-Path $OutputRoot 'framework-dependent'
    Invoke-Step 'Publishing framework-dependent' {
        & $dotnet publish $project -c $Configuration --self-contained false `
            -p:DebugType=none -p:GenerateDocumentationFile=false -o $target -v minimal -nologo
    }

    $frameworks += [pscustomobject]@{ Flavour = 'framework-dependent'; Path = $target }
}

if (-not $SkipSelfContained) {
    $target = Join-Path $OutputRoot "self-contained-$Runtime"
    Invoke-Step "Publishing self-contained ($Runtime)" {
        & $dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:PublishReadyToRun=false `
            -p:DebugType=none -p:GenerateDocumentationFile=false -o $target -v minimal -nologo
    }

    $frameworks += [pscustomobject]@{ Flavour = "self-contained-$Runtime"; Path = $target }

    if ($Compress) {
        $compressed = Join-Path $OutputRoot "self-contained-$Runtime-compressed"
        Invoke-Step "Publishing self-contained + compressed ($Runtime)" {
            & $dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
                -p:PublishSingleFile=true `
                -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:PublishReadyToRun=false `
                -p:EnableCompressionInSingleFile=true `
                -p:DebugType=none -p:GenerateDocumentationFile=false -o $compressed -v minimal -nologo
        }

        $frameworks += [pscustomobject]@{ Flavour = "self-contained-$Runtime-compressed"; Path = $compressed }
    }
}

# 5. installer --------------------------------------------------------------------
$setupFolder = Join-Path $OutputRoot 'installer'

if (-not $SkipInstaller) {
    $wixproj = Join-Path $repoRoot 'installer\mon-switch.wixproj'
    $wixBin = Join-Path $repoRoot "installer\bin\x64\$Configuration"

    if (Test-Path $wixproj) {
        New-Item -ItemType Directory -Force -Path $setupFolder | Out-Null

        Invoke-Step 'Building the per-user installer' {
            & $dotnet build $wixproj -c $Configuration -t:Rebuild -v minimal -nologo
        }
        Copy-Item (Join-Path $wixBin 'mon-switch-setup.msi') (Join-Path $setupFolder 'mon-switch-setup.msi') -Force

        # Rebuild is not optional here. Changing OutputName leaves MSBuild's incremental
        # state pointing at the previous file name, and the copy step then fails with
        # "cannot find obj\...\mon-switch-setup-machine.msi".
        Invoke-Step 'Building the per-machine installer' {
            & $dotnet build $wixproj -c $Configuration -t:Rebuild `
                -p:DefineConstants=InstallScope=perMachine `
                -p:OutputName=mon-switch-setup-machine -v minimal -nologo
        }
        Copy-Item (Join-Path $wixBin 'mon-switch-setup-machine.msi') (Join-Path $setupFolder 'mon-switch-setup-machine.msi') -Force
    }
    else {
        Write-Host ''
        Write-Host '==> Installer skipped: installer\mon-switch.wixproj not found' -ForegroundColor Yellow
    }
}
else {
    Write-Host ''
    Write-Host '==> Installer build skipped' -ForegroundColor Yellow
}

# 6. report -----------------------------------------------------------------------
Write-Host ''
Write-Host 'Publish summary' -ForegroundColor Green
Write-Host ('-' * 62)

foreach ($item in $frameworks) {
    $size = Get-FolderSize $item.Path
    $mainExe = Join-Path $item.Path 'mon-switch.exe'
    $exeSize = if (Test-Path $mainExe) { (Get-Item $mainExe).Length } else { 0 }

    '{0,-26} {1,9:N2} MB total   {2,9:N2} MB exe' -f `
        $item.Flavour, ($size / 1MB), ($exeSize / 1MB) | Write-Host
}

if (Test-Path $setupFolder) {
    $msiFiles = Get-ChildItem $setupFolder -Filter *.msi -ErrorAction SilentlyContinue
    if ($msiFiles) {
        Write-Host ('-' * 62)
        foreach ($msiFile in $msiFiles) {
            '{0,-26} {1,9:N2} MB installer' -f $msiFile.Name, ($msiFile.Length / 1MB) | Write-Host
        }
    }
}

Write-Host ('-' * 62)
Write-Host 'Done.' -ForegroundColor Green
