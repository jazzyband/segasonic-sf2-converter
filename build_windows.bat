@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "DECOMP=%~1"
set "SOUND=%~2"
set "OUT=%~3"
if "%DECOMP%"=="" goto usage
if "%SOUND%"=="" goto usage
if "%OUT%"=="" set "OUT=%ROOT%Output"
if not exist "%DECOMP%\runtime\src\ym3438.c" (
  echo ERROR: SegaSonic Decomp source not found in "%DECOMP%".
  exit /b 1
)
if not exist "%SOUND%" (
  echo ERROR: sound.zip not found: "%SOUND%".
  exit /b 1
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: Install the .NET 8 SDK and run this again.
  exit /b 1
)
if not exist "%ROOT%build" mkdir "%ROOT%build"
set "LIB=%ROOT%build\segasonic_fm.dll"
where cl >nul 2>nul
if not errorlevel 1 (
  cl /nologo /O2 /LD /I"%DECOMP%\runtime\include" "%ROOT%native\segasonic_fm_bridge.c" "%DECOMP%\runtime\src\ym3438.c" /Fe:"%LIB%"
) else (
  where gcc >nul 2>nul
  if errorlevel 1 (
    echo ERROR: C compiler not found. Open x64 Native Tools Command Prompt for Visual Studio 2022.
    exit /b 1
  )
  gcc -O2 -shared -I"%DECOMP%\runtime\include" "%ROOT%native\segasonic_fm_bridge.c" "%DECOMP%\runtime\src\ym3438.c" -o "%LIB%" -lm
)
if errorlevel 1 exit /b 1

dotnet run --configuration Release --project "%ROOT%src\SegaSonicConverter\SegaSonicConverter.csproj" -- --sound "%SOUND%" --fm-library "%LIB%" --output "%OUT%" --drums-db 5
if errorlevel 1 exit /b 1
echo.
echo SegaSonic.sf2 and corrected MIDI files are in "%OUT%".
exit /b 0
:usage
echo Usage: build_windows.bat "C:\path\to\segasonic-decomp" "C:\path\to\sound.zip" ["C:\output\folder"]
exit /b 1
