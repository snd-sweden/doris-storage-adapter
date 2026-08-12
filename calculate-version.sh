#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1fc6e423f543119c406d24e2e687d67c569f18f04a37a8b0005d80ad0dcee80 \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 7.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
