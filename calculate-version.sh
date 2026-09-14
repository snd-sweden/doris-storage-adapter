#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5 \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 8.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
