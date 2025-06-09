@echo off
setlocal EnableDelayedExpansion

:: Set input and output folders (modify these)
set INPUT_FOLDER=SortingHeightAdvanced\test\org_videos
set OUTPUT_FOLDER=SortingHeightAdvanced\test\cropped_videos

:: Known width and height (update these if needed)
set WIDTH=1920
set HEIGHT=1080

:: Crop top and bottom
set TOP=100
set BOTTOM=100
set LEFT=500
set RIGHT=100

:: Calculate new height
set /a NEW_HEIGHT=%HEIGHT% - %TOP% - %BOTTOM%
set /a NEW_WIDTH=%WIDTH% - %LEFT% - %RIGHT%

:: Create output folder if it does not exist
if not exist "%OUTPUT_FOLDER%" mkdir "%OUTPUT_FOLDER%"

:: Iterate over all mp4 files in input folder
for %%F in ("%INPUT_FOLDER%\*.mp4") do (
    echo Processing %%~nxF ...
    ffmpeg -i "%%F" -vf "crop=%NEW_WIDTH%:!NEW_HEIGHT!:%LEFT%:%TOP%" -c:v libx264 -crf 23 -preset medium -an "%OUTPUT_FOLDER%\%%~nxF"
)

echo All videos processed.
pause