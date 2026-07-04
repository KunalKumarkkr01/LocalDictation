<#
.SYNOPSIS
  End-to-end test for LocalDictation: real audio -> real mic pipeline -> Whisper -> insertion,
  verified by reading the inserted text back out of a real editable control.

.DESCRIPTION
  Drives the actual built app the way a user would:
    1. Launches LocalDictation (tray app) and the DictationSink target (a focusable textbox that
       holds foreground via AttachThreadInput and mirrors its content to a temp file).
    2. Presses the global hotkey to start recording.
    3. Plays a known sentence through the speaker so the real microphone captures it.
    4. Presses the hotkey again to stop -> transcribe -> insert.
    5. Reads the sink's textbox back and asserts the dictated words landed.

  This exercises the full production code path (mic capture, VAD, Whisper, clipboard/SendInput
  insertion) with real audio. The only substitution vs. a human is played audio instead of a
  live voice. Exit code 0 = PASS, 1 = FAIL.

.NOTES
  Requires: a working speaker + microphone audible to each other, and the Whisper base model
  present (scripts/setup-models.ps1). Run from the repo root:  pwsh tests/e2e/run-e2e.ps1
#>
[CmdletBinding()]
param(
    [string] $Expect = "brown"   # substring expected in the transcription
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repo

function Info($m) { Write-Host "[e2e] $m" -ForegroundColor Cyan }

# Kill any running instances FIRST so they neither lock bin/ during the clean rebuild nor keep
# the global hotkey registered (a stale instance would swallow the hotkey from the fresh one).
Get-Process LocalDictation,DictationSink -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Info "Building app + sink (clean, to avoid stale WPF binaries)…"
Remove-Item -Recurse -Force src/LocalDictation.Desktop/obj,src/LocalDictation.Desktop/bin -ErrorAction SilentlyContinue
dotnet build src/LocalDictation.Desktop/LocalDictation.Desktop.csproj -c Debug --nologo | Out-Null
dotnet build tools/DictationSink/DictationSink.csproj -c Debug --nologo | Out-Null

$appExe  = "src/LocalDictation.Desktop/bin/Debug/net8.0-windows/LocalDictation.exe"
$sinkExe = "tools/DictationSink/bin/Debug/net8.0-windows/DictationSink.exe"
$wav     = "src/LocalDictation.Evals/bin/Debug/net8.0-windows/fixtures/f1.wav"
if (-not (Test-Path $wav)) {
    Info "Generating audio fixtures via the evals harness…"
    dotnet run --project src/LocalDictation.Evals -- e2e | Out-Null
}
$sinkFile = Join-Path $env:TEMP "dictation-sink.txt"

Get-Process LocalDictation,DictationSink -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $sinkFile -ErrorAction SilentlyContinue

Info "Launching app + sink…"
Start-Process -FilePath $appExe | Out-Null
Start-Sleep -Seconds 8   # let Whisper warm
Start-Process -FilePath $sinkExe | Out-Null
Start-Sleep -Seconds 3   # sink force-grabs foreground

Add-Type @'
using System; using System.Runtime.InteropServices;
public class E2EKeys { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
function Hotkey {
    [E2EKeys]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [E2EKeys]::keybd_event(0x10,0,0,[UIntPtr]::Zero); [E2EKeys]::keybd_event(0x20,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [E2EKeys]::keybd_event(0x20,0,2,[UIntPtr]::Zero); [E2EKeys]::keybd_event(0x10,0,2,[UIntPtr]::Zero); [E2EKeys]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
}

$log = Join-Path $env:APPDATA "LocalDictation/startup.log"
Info "START recording"; Hotkey; Start-Sleep -Milliseconds 700
# Confirm the app actually began recording; if the hotkey was missed, retry once.
if (-not (Select-String -Path $log -Pattern "Recording started" -Quiet -ErrorAction SilentlyContinue)) {
    Info "hotkey not registered by app — retrying"; Hotkey; Start-Sleep -Milliseconds 700
}
Info "Playing audio into the microphone…"
(New-Object System.Media.SoundPlayer (Resolve-Path $wav).Path).PlaySync()
Start-Sleep -Milliseconds 400
Info "STOP recording -> transcribe -> insert"; Hotkey
Start-Sleep -Seconds 7

$content = (Get-Content $sinkFile -Raw -ErrorAction SilentlyContinue)
Get-Process LocalDictation,DictationSink -ErrorAction SilentlyContinue | Stop-Process -Force

Info "Inserted text: '$($content -replace '\r?\n','')'"
if ($content -match $Expect) {
    Write-Host "[e2e] PASS — dictated text reached the target control." -ForegroundColor Green
    exit 0
} else {
    Write-Host "[e2e] FAIL — expected substring '$Expect' not found. See %AppData%\LocalDictation\startup.log" -ForegroundColor Red
    exit 1
}
