---
name: run-in-sandbox
description: How to create and run Python, Node.js, or .NET programs in the sandboxed workspace. Load this before writing or executing code.
---

# Running code in the sandbox

`run_shell` executes inside a Docker container with **Python 3.12, Node 18, .NET 10 SDK, and git**.
The working directory is `/workspace`, which is writable and persists across commands in a session.
Write files with `write_file`, then run them with `run_shell`. `run_shell` requires user approval.

## Python
```
write_file  app.py   →  run_shell: python3 app.py
```
Install deps in a venv: `python3 -m venv .venv && . .venv/bin/activate && pip install <pkg>`.

## Node.js
```
write_file  app.js   →  run_shell: node app.js
```
For a package: `npm init -y && npm install <pkg>`.

## .NET
```
run_shell: dotnet new console -o app
write_file app/Program.cs
run_shell: dotnet run --project app
```

## Git
`git clone <url>` works (the container has network access). Always confirm before pushing or any
destructive git operation.

## Tips
- Verify with a quick run before declaring success.
- Keep work inside `/workspace`; do not touch anything outside it.
