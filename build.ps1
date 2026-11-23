<#
.SYNOPSIS
    Construit l'installateur WiFiBandSelector (publish .NET 8 + NSIS).
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests -Version 0.1.0
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

function Save-Download {
    param([string]$Url, [string]$Path, [string]$ExpectedHash)

    Write-Host "Téléchargement : $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing

    if ($ExpectedHash) {
        $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $ExpectedHash) {
            Remove-Item -Path $Path -Force
            throw "Empreinte SHA-256 inattendue pour $Path ($actual)"
        }
    }
}

try {
    if (-not $Version) {
        $project = Get-Content -Path 'src\WiFiBandSelector\WiFiBandSelector.csproj' -Raw
        $Version = [regex]::Match($project, '<Version>([^<]+)</Version>').Groups[1].Value
    }
    if (-not $Version) { throw 'Version introuvable dans le projet.' }
    Write-Host "Version : $Version"

    if (-not $SkipTests) {
        Write-Host 'Tests…'
        dotnet test 'tests\WiFiBandSelector.Tests\WiFiBandSelector.Tests.csproj' --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Les tests ont échoué.' }
    }

    Write-Host 'Publication…'
    dotnet publish 'src\WiFiBandSelector\WiFiBandSelector.csproj' -c Release -r win-x64 --self-contained false -o 'publish' --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
    if (-not (Test-Path 'publish\WiFiBandSelector.exe')) {
        throw 'publish\WiFiBandSelector.exe est absent.'
    }

    $nsisRoot = 'tools\nsis\nsis-3.11'
    $makensis = Join-Path $nsisRoot 'makensis.exe'
    $inetc = Join-Path $nsisRoot 'Plugins\x86-unicode\INetC.dll'

    if (-not (Test-Path $makensis)) {
        Write-Host 'NSIS portable absent : téléchargement…'
        $zip = Join-Path $env:TEMP 'WiFiBandSelector-nsis-3.11.zip'
        Save-Download -Url 'https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip' `
            -Path $zip -ExpectedHash 'c7d27f780ddb6cffb4730138cd1591e841f4b7edb155856901cdf5f214394fa1'
        if (-not (Test-Path 'tools\nsis')) { New-Item -ItemType Directory -Path 'tools\nsis' -Force | Out-Null }
        Expand-Archive -Path $zip -DestinationPath 'tools\nsis' -Force
    }

    if (-not (Test-Path $inetc)) {
        Write-Host 'Plugin inetc absent : téléchargement…'
        $zip = Join-Path $env:TEMP 'WiFiBandSelector-inetc.zip'
        $extract = Join-Path $env:TEMP 'WiFiBandSelector-inetc'
        Save-Download -Url 'https://nsis.sourceforge.io/mediawiki/images/c/c9/Inetc.zip' `
            -Path $zip -ExpectedHash '88d9dcffbe967df6ee9f5820f8199a673383e581bf650dbc35b94846314b1a4b'
        if (Test-Path $extract) { Remove-Item -Path $extract -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $extract -Force
        Copy-Item -Path (Join-Path $extract 'Plugins\x86-unicode\INetC.dll') -Destination $inetc -Force
    }

    if (-not (Test-Path 'dist')) { New-Item -ItemType Directory -Path 'dist' -Force | Out-Null }
    Write-Host 'Génération de l’installateur…'
    & $makensis "/DAPP_VERSION=$Version" 'installer\setup.nsi'
    if ($LASTEXITCODE -ne 0) { throw "makensis a échoué (code $LASTEXITCODE)." }

    $setup = "dist\WiFiBandSelector-Setup-$Version.exe"
    if (-not (Test-Path $setup)) { throw "$setup n'a pas été produit." }
    Write-Host "Terminé : $setup"
}
finally {
    Pop-Location
}
