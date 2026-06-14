@echo off
echo ================================
echo   Cleaning VS2022 build folders
echo ================================

for /r %%d in (bin) do (
    if exist "%%d" (
        echo Deleting %%d
        rmdir /s /q "%%d"
    )
)

for /r %%d in (obj) do (
    if exist "%%d" (
        echo Deleting %%d
        rmdir /s /q "%%d"
    )
)

if exist ".vs" (
    echo Deleting .vs folder
    rmdir /s /q ".vs"
)

echo ================================
echo       Done cleaning!
echo ================================
pause
