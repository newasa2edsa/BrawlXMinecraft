@echo off
title Build Minecraft Fabric Mod
echo ==================================================================
echo   Compiling passthrough Fabric Mod JAR...
echo ==================================================================
echo.
python build_mod.py
if errorlevel 1 pause
