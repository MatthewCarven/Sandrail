@echo off
setlocal
cd /d "%~dp0"
rem Profile every car on every track headless, and keep it going overnight (2026-11-28).
rem   profile.cmd                Profile unprofiled cars: what the sheets haven't got yet.
rem   profile.cmd everywhere     Profile every car on every track, every car driven again.
rem Anything after that goes to the game as a flag, as with start.cmd.
rem
rem The game hands the rest to a fresh game every five or six hours, since Godot 4.7.1's Jolt stops for good after
rem about 13 hours at x16, and after three minutes with nothing moving. It leaves the next game's flags in a file and
rem ends with 75, and this starts the next game. (WORKLOG, 2026-11-28.)

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

rem Build first: Godot launched with --path loads whatever DLL is already built (start.cmd says why).
dotnet build Sandrail.Game --nologo -v q || (echo. & echo Build failed - not launching. & pause & exit /b 1)

set "MODE=unprofiled"
set "ARGS=%*"
if /i "%~1"=="everywhere" (
  set "MODE=everywhere"
  set "ARGS=%ARGS:*everywhere=%"
)
if /i "%~1"=="unprofiled" set "ARGS=%ARGS:*unprofiled=%"
set "NEXT=%TEMP%\sandrail-next-%RANDOM%%RANDOM%.txt"
if exist "%NEXT%" del "%NEXT%"

rem The game never writes to a console: scrolling or selecting text in a console window holds whatever writes to it
rem until you stop (2026-11-27). Its output goes to profile.txt, every game's in turn, and a second window shows that
rem file as it grows. Scroll that one as much as you like; close it when you're done.
type nul > profile.txt
start "Sandrail profile - safe to scroll" powershell -NoProfile -Command "Get-Content -Path 'profile.txt' -Wait -Tail 40"
echo Profiling (%MODE%), headless. Its output is in profile.txt and the other window.
echo To stop it, close this window.

"%EXE%" --path Sandrail.Game --headless -- --profile=%MODE% --restart-file="%NEXT%" %ARGS% >> profile.txt 2>&1

:ended
if not "%ERRORLEVEL%"=="75" goto done
if not exist "%NEXT%" goto done
set /p LINE=<"%NEXT%"
del "%NEXT%"
echo ===== %DATE% %TIME%: carrying on in a fresh game
echo ===== %DATE% %TIME%: carrying on in a fresh game>> profile.txt
"%EXE%" --path Sandrail.Game --headless -- %LINE% >> profile.txt 2>&1
goto ended

:done
if exist "%NEXT%" del "%NEXT%"
echo.
echo The profile has ended. Everything it said is in profile.txt.
pause
