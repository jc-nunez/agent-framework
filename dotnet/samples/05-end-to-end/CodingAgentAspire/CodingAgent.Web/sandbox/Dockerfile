# Sandbox image for the coding agent's run_shell tool: Python, Node.js, .NET SDK, and git.
# Based on the multi-arch .NET SDK image (includes arm64), with python3 + node added via apt.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 python3-pip python3-venv \
        nodejs npm \
        git curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace
