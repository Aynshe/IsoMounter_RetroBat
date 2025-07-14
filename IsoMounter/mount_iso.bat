@echo off
setlocal enabledelayedexpansion

:: Chemin vers IsoMounter.exe // Path to IsoMounter.exe
for %%i in ("%cd%\..\..\..\..\plugins\IsoMounter\IsoMounter.exe") do set "IsoMounter_path=%%~fi"

:: Vérifier si le fichier existe / Check if the file exists
if not exist "%IsoMounter_path%" (
    exit /b 1
)

:: Lancer le montage de l'ISO // Start mounting the ISO
"%IsoMounter_path%" %*

endlocal