#!/usr/bin/env bash

docker run -q --rm -v "$PWD":/src \
    mcr.microsoft.com/dotnet/sdk:8.0@sha256:58359d0b8fe8237be1d63ac335ca378e2857f976c7f92791f7c84b3c117037f5 \
    bash -lc '
        git config --global --add safe.directory /src && 
        dotnet tool install -g minver-cli --version 7.0.0 > /dev/null && 
        /root/.dotnet/tools/minver -t v /src
    '
