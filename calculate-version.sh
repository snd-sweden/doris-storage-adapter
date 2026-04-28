#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:10.0.203@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 7.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
