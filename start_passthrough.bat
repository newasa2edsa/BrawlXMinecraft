@echo off
title BrawlXMinecraft - Passthrough Bridge
echo ==================================================================
echo   BrawlXMinecraft - First-Person Passthrough Bridge
echo ==================================================================
echo.
echo Make sure MuMu Player (BSL v68) and Minecraft Fabric 26.3 are running!
echo Press F12 at any time in-game to cleanly exit.
echo.
python bsl-passthrough/bsl_passthrough.py
if errorlevel 1 pause
