@echo off
setlocal
cd /d "%~dp0"
rem Builds SandrailProfiles.exe, the car profile's viewer, into dist\ (not committed).
rem Needs Python 3.11+ with what requirements.txt lists:  python -m pip install -r requirements.txt
rem Refuses to build if the tests fail, as start.cmd refuses to launch a failed build.

python -m pytest -q -p no:cacheprovider tests || (echo. & echo Tests failed - not building. & exit /b 1)
python -m PyInstaller --noconfirm --log-level WARN --onefile --windowed --name SandrailProfiles ^
  --distpath dist --workpath build\work --specpath build viewer.py || (echo. & echo PyInstaller failed. & exit /b 1)
echo.
echo Built %~dp0dist\SandrailProfiles.exe
