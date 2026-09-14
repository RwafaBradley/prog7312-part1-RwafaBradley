@echo off
REM Convenience wrapper so the solution can be launched from Explorer.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-desktop.ps1" %*
