@echo off
setlocal
set "WIDTH=960"
set "FPS=12"
set "DITHER=bayer:bayer_scale=3"

where ffmpeg >nul 2>&1
if errorlevel 1 (
  echo ffmpeg not found. Install with: winget install --id Gyan.FFmpeg.Essentials --exact
  exit /b 1
)

if "%~1"=="" (
  echo Drag one or more video files onto this script.
  echo Or run: make_gif.bat "C:\path\clip.mp4"
  exit /b 1
)

:loop
if "%~1"=="" goto done
set "IN=%~1"
set "OUT=%~dpn1.gif"
set "PAL=%TEMP%\gif_palette_%~n1.png"

echo Converting: %~nx1
ffmpeg -y -loglevel error -i "%IN%" -vf "fps=%FPS%,scale=%WIDTH%:-1:flags=lanczos,palettegen" "%PAL%"
if errorlevel 1 goto fail
ffmpeg -y -loglevel error -i "%IN%" -i "%PAL%" -lavfi "fps=%FPS%,scale=%WIDTH%:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=%DITHER%" -loop 0 "%OUT%"
if errorlevel 1 goto fail
del "%PAL%" >nul 2>&1
echo Done: %OUT%
shift
goto loop

:fail
echo Conversion failed.
exit /b 1

:done
echo All done.
