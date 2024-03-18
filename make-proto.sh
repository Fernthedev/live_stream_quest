#!/bin/sh

./vcpkg_installed/arm64-android/tools/protobuf/protoc --proto_path=./protos \
    --cpp_out=./qmod/protos/ \
    --csharp_out=./pcmod/Protos/ \
    ./protos/live_stream.proto
