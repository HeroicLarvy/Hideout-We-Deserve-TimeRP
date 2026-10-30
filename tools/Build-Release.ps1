param(
    [Parameter(Mandatory = $true)]
    [string]$SPTRuntimePath
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $repositoryRoot "src\HideoutWeDeserveTimeRP"
$projectPath = Join-Path $sourceDirectory "HideoutWeDeserveTimeRP.csproj"
$metadataPath = Join-Path $sourceDirectory "package.json"
$manifest = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$sdkVersion = (& dotnet --version).Trim()

if ($LASTEXITCODE -ne 0) {
    throw "Unable to determine the installed .NET SDK version."
}

if ([version]$sdkVersion -lt [version]"10.0") {
    throw "Hideout We Deserve - TimeRP targets .NET 10. Install the .NET 10 SDK before building."
}

if (-not (Test-Path -LiteralPath (Join-Path $SPTRuntimePath "SPTarkov.Server.Core.dll"))) {
    throw "SPTRuntimePath must point to the SPT_Runtime folder containing SPTarkov.Server.Core.dll."
}

& dotnet build $projectPath -c Release "-p:SPTRuntimePath=$SPTRuntimePath"
if ($LASTEXITCODE -ne 0) {
    throw "The Hideout We Deserve - TimeRP build failed."
}

$buildDirectory = Join-Path $sourceDirectory "bin\Release\net10.0"
$builtAssembly = Join-Path $buildDirectory "HideoutWeDeserveTimeRP.dll"
if (-not (Test-Path -LiteralPath $builtAssembly)) {
    throw "The build succeeded but did not produce HideoutWeDeserveTimeRP.dll."
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("HideoutWeDeserveTimeRP-" + [guid]::NewGuid().ToString("N"))
$modRoot = Join-Path $stagingRoot "SPT_Runtime\user\mods\HideoutWeDeserveTimeRP"
$outputDirectory = Join-Path $repositoryRoot "dist"
$archivePath = Join-Path $outputDirectory "Hideout-We-Deserve-TimeRP-$($manifest.version).zip"

try {
    New-Item -ItemType Directory -Path (Join-Path $modRoot "wwwroot") -Force | Out-Null
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

    Copy-Item -LiteralPath $builtAssembly -Destination $modRoot
    Copy-Item -LiteralPath (Join-Path $sourceDirectory "config.json") -Destination $modRoot
    Copy-Item -LiteralPath $metadataPath -Destination $modRoot
    Copy-Item -LiteralPath (Join-Path $sourceDirectory "wwwroot\index.html") -Destination (Join-Path $modRoot "wwwroot")

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stagingRoot,
        $archivePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
        $requiredEntries = @(
            "SPT_Runtime/user/mods/HideoutWeDeserveTimeRP/HideoutWeDeserveTimeRP.dll",
            "SPT_Runtime/user/mods/HideoutWeDeserveTimeRP/config.json",
            "SPT_Runtime/user/mods/HideoutWeDeserveTimeRP/package.json",
            "SPT_Runtime/user/mods/HideoutWeDeserveTimeRP/wwwroot/index.html"
        )

        foreach ($entry in $requiredEntries) {
            if ($entries -notcontains $entry) {
                throw "The release ZIP is missing required file $entry."
            }
        }

        if ($entries -match "(^|/)(bin|obj)/|\.csproj$|\.cs$") {
            throw "The release ZIP unexpectedly contains source files or build intermediates."
        }

        if ($entries | Where-Object { [System.IO.Path]::GetFileName($_) -match "(?i)^readme(?:\..*)?$" }) {
            throw "The release ZIP unexpectedly contains a README."
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    Write-Output "Created $archivePath"
    Write-Output "SHA256: $hash"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
