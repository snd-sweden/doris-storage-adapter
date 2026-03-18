#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:10.0.201@sha256:478b9038d187e5b5c29bfa8173ded5d29e864b5ad06102a12106380ee01e2e49 \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 7.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
