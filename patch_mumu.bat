@echo off
title BrawlXMinecraft - Patch MuMu Player
echo ==================================================================
echo   MuMu Player 12 First-Person POV ^& Steve Patcher
echo ==================================================================
echo.
echo Make sure MuMu Player 12 is running with BSL v68 installed!
echo.
python bsl-passthrough/mumu_steve_patch.py
if errorlevel 1 pause
