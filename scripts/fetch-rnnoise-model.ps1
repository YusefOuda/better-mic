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
$actual = (Get-FileHash $tarball -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $hash) {
    Remove-Item $tarball
    throw "Checksum mismatch for RNNoise model (expected $hash, got $actual)"
}
tar -xzf $tarball -C $dest
if (-not (Test-Path "$dest\src\rnnoise_data.c")) { throw "Model archive did not contain src\rnnoise_data.c" }
Remove-Item $tarball
Write-Host "RNNoise model extracted to $dest\src"
