# Downloads the RNNoise model weights matching third_party\rnnoise\model_version and extracts
# them to third_party\rnnoise-model\src (rnnoise_data.c/.h). Verifies the SHA-256.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hash = (Get-Content "$root\third_party\rnnoise\model_version" -Raw).Trim()
$dest = "$root\third_party\rnnoise-model"
$tarball = "$dest\rnnoise_data-$hash.tar.gz"

if (Test-Path "$dest\src\rnnoise_data.c") { Write-Host "RNNoise model already present"; exit 0 }
New-Item -ItemType Directory -Force $dest | Out-Null

if (-not (Test-Path $tarball)) {
    Write-Host "Downloading RNNoise model $hash"
    Invoke-WebRequest "https://media.xiph.org/rnnoise/models/rnnoise_data-$hash.tar.gz" -OutFile $tarball
}
# .NET directly instead of Get-FileHash: Windows PowerShell 5.1 launched from a PowerShell 7
# session inherits PS7's PSModulePath and fails to load the module that provides it.
$stream = [System.IO.File]::OpenRead($tarball)
try { $bytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream) } finally { $stream.Dispose() }
$actual = -join ($bytes | ForEach-Object { $_.ToString('x2') })
if ($actual -ne $hash) {
    Remove-Item $tarball
    throw "Checksum mismatch for RNNoise model (expected $hash, got $actual)"
}
# Windows' bsdtar explicitly: Git's GNU tar (often first on PATH) parses "C:" as a remote host.
& "$env:SystemRoot\System32\tar.exe" -xzf $tarball -C $dest
if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
Remove-Item -Recurse -Force "$dest\models" -ErrorAction SilentlyContinue  # training checkpoints, unused
if (-not (Test-Path "$dest\src\rnnoise_data.c")) { throw "Model archive did not contain src\rnnoise_data.c" }
Remove-Item $tarball
Write-Host "RNNoise model extracted to $dest\src"
