# Runs M1a's headless oracles and prints one line each (docs/M1-PLAN.md, M1a).
#
#   pwsh tools/oracles.ps1                     all four
#   pwsh tools/oracles.ps1 -Oracles drop       just one
#   pwsh tools/oracles.ps1 -Spec variant.json  another spec than the shipped buggy
#
# --fixed-fps 120 makes every frame exactly one physics tick, so a run takes as long as the CPU
# needs rather than the seconds it simulates, and two runs of the same spec read the same.
param(
    [string[]]$Oracles = @("straight", "drop", "brake", "circle"),
    [string]$Spec = "",
    [string]$Godot = "Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"

# Resolved before moving to the repository root, so a relative path means relative to where you ran it.
$specPath = if ($Spec) { (Resolve-Path $Spec).Path } else { "" }
Set-Location (Split-Path $PSScriptRoot -Parent)

dotnet build Tumbleweed.Game --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed - not running stale code." }

foreach ($oracle in $Oracles) {
    $flags = @("--oracle=$oracle")
    if ($specPath) { $flags += "--spec=$specPath" }
    $output = & $Godot --path Tumbleweed.Game --headless --fixed-fps 120 -- @flags 2>&1
    $line = $output | Where-Object { "$_" -match "^oracle " }
    if ($line) { $line } else { "oracle ${oracle}: NO RESULT"; $output | Select-Object -Last 15 }
}
