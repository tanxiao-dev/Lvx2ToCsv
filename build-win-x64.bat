@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found. Install .NET 8 SDK or build the project in Visual Studio 2022.
  pause
  exit /b 1
)
dotnet publish Lvx2ToCsv.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if errorlevel 1 exit /b 1
echo.
echo EXE created in: bin\Release\net8.0\win-x64\publish\
pause
