#!/usr/bin/env bash

docker build \
    -t doris-storage-adapter \
    --build-arg VERSION="$(./calculate-version.sh)" \
    --build-arg CI=true \
    --build-arg SOURCE_DATE_EPOCH="$(git log -1 --pretty=%ct)" \
    .
