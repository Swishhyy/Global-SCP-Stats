param(
    [string]$GameManagedPath = $env:SL_REFERENCES,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($GameManagedPath)) {
    throw 'Set SL_REFERENCES or pass -GameManagedPath pointing to SCPSL_Data/Managed.'
}

foreach ($assemblyName in @('UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll', 'Mirror.dll', 'Assembly-CSharp-firstpass.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $GameManagedPath $assemblyName) -PathType Leaf)) {
        throw "Missing game reference: $assemblyName"
    }
}

Push-Location $projectRoot
try {
    dotnet build GlobalSCPStats.sln -c $Configuration --disable-build-servers -m:1 "-p:GameManagedPath=$GameManagedPath"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    dotnet run --project tests/GlobalSCPStats.Tests -c $Configuration -f net10.0 --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }

    if ($env:OS -eq 'Windows_NT') {
        & "./tests/GlobalSCPStats.Tests/bin/$Configuration/net48/GlobalSCPStats.Tests.exe"
        if ($LASTEXITCODE -ne 0) { throw '.NET Framework regression checks failed.' }
    }

    [xml]$properties = Get-Content -LiteralPath Directory.Build.props
    $version = $properties.Project.PropertyGroup.Version
    $package = Join-Path $projectRoot "artifacts/GlobalSCPStats-$version-dev"
    $plugins = Join-Path $package 'EXILED/Plugins'
    $dependencies = Join-Path $plugins 'dependencies'
    New-Item -ItemType Directory -Path $dependencies -Force | Out-Null

    Copy-Item -LiteralPath "src/GlobalSCPStats/bin/$Configuration/net48/GlobalSCPStats.dll" -Destination $plugins -Force
    Copy-Item -LiteralPath "src/GlobalSCPStats.Core/bin/$Configuration/net48/GlobalSCPStats.Core.dll" -Destination $dependencies -Force
    Copy-Item -LiteralPath README.md, LICENSE -Destination $package -Force
    Copy-Item -LiteralPath docs, examples -Destination $package -Recurse -Force

    $archive = "$package.zip"
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -Force
    Write-Output "Package: $archive"
}
finally {
    Pop-Location
}
