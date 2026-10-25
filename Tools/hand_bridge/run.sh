#!/usr/bin/env bash
# Starts the webcam hand bridge. First run creates a Python 3.12 venv and installs dependencies.
set -e
cd "$(dirname "$0")"
if [ ! -x .venv/bin/python ]; then
  if command -v uv >/dev/null; then
    uv venv --python 3.12 .venv && uv pip install --python .venv/bin/python -r requirements.txt
  else
    python3.12 -m venv .venv && .venv/bin/pip install -r requirements.txt
  fi
fi
exec .venv/bin/python hand_bridge.py "$@"
