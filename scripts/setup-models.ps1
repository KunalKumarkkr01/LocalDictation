<#
.SYNOPSIS
  Downloads the Whisper ggml models LocalDictation uses into models/whisper/.
.DESCRIPTION
  Models are not committed to the repo (they are large binaries). This script fetches
  base.en (default) and small (multilingual) from the public whisper.cpp Hugging Face repo.
.EXAMPLE
  ./scripts/setup-models.ps1
#>
param(
    [string[]] $Models = @('ggml-base.en.bin', 'ggml-small.bin')
)

$ErrorActionPreference = 'Stop'
$baseUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/'
$dir = Join-Path $PSScriptRoot '..\models\whisper'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

foreach ($m in $Models) {
    $dest = Join-Path $dir $m
    if ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 1MB)) {
        Write-Host "[skip] $m already present ($([math]::Round((Get-Item $dest).Length/1MB)) MB)"
        continue
    }
    Write-Host "[down] $m ..."
    Invoke-WebRequest -Uri ($baseUrl + $m) -OutFile $dest
    Write-Host "[ ok ] $m ($([math]::Round((Get-Item $dest).Length/1MB)) MB)"
}

Write-Host "`nDone. Models in: $((Resolve-Path $dir).Path)"
