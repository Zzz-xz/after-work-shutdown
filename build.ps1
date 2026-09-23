param([switch]$Test)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$buildDirectory = Join-Path $PSScriptRoot 'build'
[void](New-Item -ItemType Directory -Path $buildDirectory -Force)
$executableName = 'after-work-shutdown.exe'
$executable = Join-Path $buildDirectory $executableName
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerArguments = @('/nologo', '/warn:4', '/warnaserror+', '/optimize+', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')

& $compiler @compilerArguments '/target:winexe' "/out:$executable" "/win32manifest:$manifest" @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output "Build succeeded: build/$executableName"

if ($Test) {
    $testDirectory = Join-Path $buildDirectory 'tests'
    [void](New-Item -ItemType Directory -Path $testDirectory -Force)
    $testExecutable = Join-Path $testDirectory 'ReminderTests.exe'
    & $compiler @compilerArguments '/target:exe' '/main:AfterWork.ReminderTests' "/out:$testExecutable" "/win32manifest:$manifest" @sourceFiles (Join-Path $PSScriptRoot 'tests\ReminderTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}
