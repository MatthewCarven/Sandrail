# Runs the headless oracles and prints one line each (docs/M1-PLAN.md, M1a and M1c).
#
#   pwsh tools/oracles.ps1                     all eighteen
#   pwsh tools/oracles.ps1 -Oracles drop       just one
#   pwsh tools/oracles.ps1 -Spec variant.json  another spec than the shipped buggy
#
# straight, drop, brake, circle, handbrake, sweeper, respawn and climb drive the bare plain on road grip, as at M1a; jump, air, loop, bumps and lap
# drive the test ground; pieces drives the showroom (M2b), pieces2 the pieces added since and the crossing (PIECES-PLAN); track drives the shakedown, start to finish (M2d); bank climbs a generated track's tallest banks from rest (M3d); ghost records the shakedown as a ghost and plays it back against the car (M4b).
#
# --fixed-fps 120 makes every frame exactly one physics tick, so a run takes as long as the CPU
# needs rather than the seconds it simulates, and two runs of the same spec read the same.
param(
    [string[]]$Oracles = @("straight", "drop", "brake", "circle", "handbrake", "sweeper", "respawn", "climb", "jump", "air", "loop", "bumps", "lap", "pieces", "pieces2", "track", "bank", "ghost"),
    [string]$Spec = "",
    [string]$Godot = "Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"

# Resolved before moving to the repository root, so a relative path means relative to where you ran it.
$specPath = if ($Spec) { (Resolve-Path $Spec).Path } else { "" }
Set-Location (Split-Path $PSScriptRoot -Parent)

dotnet build Sandrail.Game --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed - not running stale code." }

# The oracles' log goes in the temp folder, not the game's user://logs, where Godot keeps only the last five and
# eighteen oracles would push out every one of Matthew's (2026-11-27).
$log = Join-Path ([System.IO.Path]::GetTempPath()) "sandrail-oracles.log"

foreach ($oracle in $Oracles) {
    $flags = @("--oracle=$oracle")
    if ($specPath) { $flags += "--spec=$specPath" }
    $output = & $Godot --log-file $log --path Sandrail.Game --headless --fixed-fps 120 -- @flags 2>&1
    $line = $output | Where-Object { "$_" -match "^oracle " }
    if ($line) { $line } else { "oracle ${oracle}: NO RESULT"; $output | Select-Object -Last 15 }
}
