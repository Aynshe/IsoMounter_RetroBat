@echo off
setlocal enabledelayedexpansion

:: Chemin relatif vers IsoMounter.exe (même structure que mount_iso.bat) // Relative path to IsoMounter.exe (same structure as mount_iso.bat)
for %%i in ("%cd%\..\..\..\..\plugins\IsoMounter\IsoMounter.exe") do set "IsoMounter_path=%%~fi"

:: Vérifier si le fichier existe // Check if the file exists
if not exist "%IsoMounter_path%" (
    echo Erreur: Impossible de trouver IsoMounter.exe
    echo Path essaye: %IsoMounter_path%
    pause
    exit /b 1
)

:: Démontage // Unmount
"%IsoMounter_path%" --unmount

exit /b 0
