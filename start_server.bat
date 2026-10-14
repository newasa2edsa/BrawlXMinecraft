@echo off
title CustomBrawlServer (Port 9339 / Bridge 8080)
echo ==================================================================
echo   Starting CustomBrawlServer...
echo ==================================================================
echo.
if exist "CustomBrawlServer\bin\Debug\net10.0\CustomBrawlServer.exe" (
    "CustomBrawlServer\bin\Debug\net10.0\CustomBrawlServer.exe"
) else (
    dotnet run --project CustomBrawlServer/CustomBrawlServer.csproj
)
if errorlevel 1 pause
