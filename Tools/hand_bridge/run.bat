@echo off
rem Starts the webcam hand bridge. First run creates a Python 3.12 venv and installs dependencies.
cd /d "%~dp0"
if not exist .venv\Scripts\python.exe (
  py -3.12 -m venv .venv || goto :err
  .venv\Scripts\pip install -r requirements.txt || goto :err
)
.venv\Scripts\python hand_bridge.py %*
goto :eof
:err
echo Needs Python 3.12 (MediaPipe doesn't support newer versions yet): https://www.python.org/downloads/
pause
