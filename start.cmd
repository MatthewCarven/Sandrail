@echo off
setlocal
cd /d "%~dp0"
rem Launch Tumbleweed in the pinned Godot 4.7.1 mono editor build.
rem Prefers the copy beside this repo; falls back to Overburden's.

set "EXE=Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if not exist "%EXE%" set "EXE=..\-=Programming=-\MinerCraft\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"

if not exist "%EXE%" (
  echo.
  echo Godot 4.7.1-stable mono not found.
  echo Expected either:
  echo   .\Godot_v4.7.1-stable_mono_win64\
  echo   ..\-=Programming=-\MinerCraft\Godot_v4.7.1-stable_mono_win64\
  echo.
  pause
  exit /b 1
)

rem Godot launched with --path does not rebuild: it loads whatever DLL is in .godot/mono/temp/bin,
rem so a change made while the game was closed would play the previous binary (Overburden,
rem 2026-09-22). Build first, and refuse to launch stale code.
rem Everything you pass to start.cmd lands after the bare "--" as a game flag.
dotnet build Tumbleweed.Game --nologo -v q || (echo. & echo Build failed - not launching. & pause & exit /b 1)
"%EXE%" --path Tumbleweed.Game -- %*
pause
