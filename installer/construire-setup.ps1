param(
    [ValidateSet("Autonome", "Compact")]
    [string]$Mode = "Compact",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$SelfContained = ($Mode -eq "Autonome")
$Racine = Split-Path -Parent $PSScriptRoot
$PubDir = Join-Path $Racine "publish\win-x64"
$RessourcesInstaller = Join-Path $Racine "installer\Resources"
$ZipPayload = Join-Path $RessourcesInstaller "KeyCodePayload.zip"
$DistDir = Join-Path $PSScriptRoot "dist"

Write-Output "=== 1. Publication de KeyCode ($Configuration, Autonome=$SelfContained) ==="
if (Test-Path $PubDir) { Remove-Item $PubDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PubDir | Out-Null

$scArg = if ($SelfContained) { "true" } else { "false" }
dotnet publish (Join-Path $Racine "KeyCode\KeyCode.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained $scArg `
    -p:PublishSingleFile=false `
    -o $PubDir

if ($LASTEXITCODE -ne 0) { throw "Échec de dotnet publish pour KeyCode." }

Write-Output "=== 2. Création de l'archive embarquée ==="
New-Item -ItemType Directory -Force -Path $RessourcesInstaller | Out-Null
if (Test-Path $ZipPayload) { Remove-Item $ZipPayload -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $PubDir,
    $ZipPayload,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)
Write-Output ("Archive créée : {0:N2} Mo" -f ((Get-Item $ZipPayload).Length / 1MB))

Write-Output "=== 3. Construction de KeyCodeSetup.exe ==="
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$SetupPubDir = Join-Path $DistDir "setup-bin"
if (Test-Path $SetupPubDir) { Remove-Item $SetupPubDir -Recurse -Force }

$publishArgs = @(
    (Join-Path $Racine "installer\KeyCodeSetup.csproj"),
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", $scArg,
    "-p:PublishSingleFile=true",
    "-o", $SetupPubDir
)
if ($SelfContained) {
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}
& dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) { throw "Échec de dotnet publish pour KeyCodeSetup." }

$ExeGenere = Join-Path $SetupPubDir "KeyCodeSetup.exe"
$ExeFinal = Join-Path $DistDir "KeyCodeSetup.exe"
Copy-Item $ExeGenere $ExeFinal -Force

Write-Output ""
Write-Output ("Installateur prêt : {0} ({1:N2} Mo)" -f $ExeFinal, ((Get-Item $ExeFinal).Length / 1MB))

# Si Inno Setup (ISCC.exe) est disponible, génère aussi l'installateur Inno Setup
$Iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($Iscc) {
    Write-Output "=== 4. Compilation optionnelle Inno Setup ==="
    & $Iscc.Source (Join-Path $PSScriptRoot "KeyCode.iss")
}
