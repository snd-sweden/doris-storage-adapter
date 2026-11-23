#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 8.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
