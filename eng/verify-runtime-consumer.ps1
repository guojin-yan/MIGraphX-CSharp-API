[CmdletBinding()]
param(
    [string] $Version = '1.0.0',
    [Parameter(Mandatory)][string] $PackageDirectory
)
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$consumer = Join-Path $root 'tests/fixtures/runtime-package-consumer/RuntimePackageConsumer.csproj'
$platform = if ($IsWindows) { 'win-x64' } else { 'ubuntu.24.04-x64' }
$audit = Join-Path (Get-ArtifactsRoot) "runtime-consumer/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $audit | Out-Null
$source = [Security.SecurityElement]::Escape((Resolve-Path $PackageDirectory).Path)
$config = Join-Path $audit 'NuGet.Config'
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"runtime-local`" value=`"$source`"/><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources><packageSourceMapping><clear/><packageSource key=`"runtime-local`"><package pattern=`"JYPPX.ROCm.MIGraphX.CSharp.API*`"/></packageSource><packageSource key=`"nuget.org`"><package pattern=`"Microsoft.*`"/></packageSource></packageSourceMapping></configuration>")
Invoke-DotNet -Arguments @('restore', $consumer, '--configfile', $config, '--packages', (Join-Path $audit 'packages'), '--force-evaluate', "-p:MIGraphXConsumerVersion=$Version", "-p:MIGraphXRuntimePlatform=$platform")
Invoke-DotNet -Arguments @('build', $consumer, '-c', 'Release', '--no-restore', "-p:MIGraphXConsumerVersion=$Version", "-p:MIGraphXRuntimePlatform=$platform")
& (Join-Path $root 'tests/fixtures/generate-onnx-identity.ps1') | Out-Host
$assembly = Join-Path (Get-ArtifactsRoot) 'bin/RuntimePackageConsumer/Release/net10.0/RuntimePackageConsumer.dll'
Invoke-DotNet -Arguments @($assembly, (Join-Path (Get-ArtifactsRoot) 'models/m2-identity-float32.onnx'))
$rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
$publish = Join-Path $audit 'publish'
Invoke-DotNet -Arguments @('restore', $consumer, '--runtime', $rid, '--configfile', $config, '--packages', (Join-Path $audit 'packages'), '-p:SelfContained=false', "-p:MIGraphXConsumerVersion=$Version", "-p:MIGraphXRuntimePlatform=$platform")
Invoke-DotNet -Arguments @('publish', $consumer, '-c', 'Release', '--no-restore', '--runtime', $rid, '--self-contained', 'false', '--output', $publish, "-p:MIGraphXConsumerVersion=$Version", "-p:MIGraphXRuntimePlatform=$platform")
Invoke-DotNet -Arguments @((Join-Path $publish 'RuntimePackageConsumer.dll'), (Join-Path (Get-ArtifactsRoot) 'models/m2-identity-float32.onnx'))
