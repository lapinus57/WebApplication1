$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../../Client/Client.csproj'
$cases = @(
    @{ Name = 'Default'; Arguments = @(); Architecture = 'x64' },
    @{ Name = 'Visual Studio x86'; Arguments = @('-p:Platform=x86'); Architecture = 'x86' },
    @{ Name = 'Visual Studio x64'; Arguments = @('-p:Platform=x64'); Architecture = 'x64' },
    @{ Name = 'Visual Studio ARM64'; Arguments = @('-p:Platform=ARM64'); Architecture = 'arm64' },
    @{ Name = 'Publish x86'; Arguments = @('-p:PublishProfile=win-x86'); Architecture = 'x86' },
    @{ Name = 'Publish x64'; Arguments = @('-p:PublishProfile=win-x64'); Architecture = 'x64' },
    @{ Name = 'Publish ARM64'; Arguments = @('-p:PublishProfile=win-arm64'); Architecture = 'arm64' },
    @{ Name = 'x86 without profile'; Arguments = @('-p:Platform=x86', '-p:PublishProfile='); Architecture = 'x86' },
    @{ Name = 'ARM64 without profile'; Arguments = @('-p:Platform=ARM64', '-p:PublishProfile='); Architecture = 'arm64' },
    @{ Name = 'Explicit win-x86 runtime'; Arguments = @('-p:RuntimeIdentifier=win-x86'); Architecture = 'x86' },
    @{ Name = 'Explicit win-arm64 runtime'; Arguments = @('-p:RuntimeIdentifier=win-arm64'); Architecture = 'arm64' },
    @{ Name = 'Explicit x86 CPU'; Arguments = @('-p:PlatformTarget=x86'); Architecture = 'x86' }
)
foreach ($case in $cases) {
    $arguments = @('msbuild', $project, '-nologo', '-p:EnableWindowsTargeting=true', '-getProperty:PlatformTarget,RuntimeIdentifier,AppxBundlePlatforms') + $case.Arguments
    $output = & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Property evaluation failed: $($case.Name)" }
    $properties = ($output -join "`n" | ConvertFrom-Json).Properties
    $expected = $case.Architecture
    if ($properties.PlatformTarget -ne $expected -or $properties.RuntimeIdentifier -ne "win-$expected" -or $properties.AppxBundlePlatforms -ne $expected) {
        throw "Architecture mismatch ($($case.Name)): $($properties | ConvertTo-Json -Compress)"
    }
    Write-Host "PASS: $($case.Name) -> $expected / win-$expected / bundle $expected"
}
Write-Host "$($cases.Count) client architecture configurations passed."
