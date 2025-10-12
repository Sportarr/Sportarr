#!/bin/bash
set -e

FRAMEWORK="net8.0"
PLATFORM=$1
ARCHITECTURE="${2:-x64}"

if [ "$PLATFORM" = "Windows" ]; then
  RUNTIME="win-$ARCHITECTURE"
elif [ "$PLATFORM" = "Linux" ]; then
  RUNTIME="linux-$ARCHITECTURE"
elif [ "$PLATFORM" = "Mac" ]; then
  RUNTIME="osx-$ARCHITECTURE"
else
  echo "Platform must be provided as first argument: Windows, Linux or Mac"
  exit 1
fi

outputFolder='_output'
testPackageFolder='_tests'

rm -rf $outputFolder
rm -rf $testPackageFolder

slnFile=src/Fightarr.sln
outputFileV1=src/Fightarr.Api.V3/openapi.json
outputFileV5=src/Fightarr.Api.V5/openapi.json
platform=Posix

if [ "$PLATFORM" = "Windows" ]; then
  application=Fightarr.Console.dll
else
  application=Fightarr.dll
fi

dotnet clean $slnFile -c Debug
dotnet clean $slnFile -c Release

dotnet msbuild -restore $slnFile -p:Configuration=Debug -p:Platform=$platform -p:RuntimeIdentifiers=$RUNTIME -t:PublishAllRids

dotnet new tool-manifest
dotnet tool install --version 8.0.0 Swashbuckle.AspNetCore.Cli

# Remove the openapi.json files so we can check if they were created
rm -f $outputFileV1
rm -f $outputFileV5

# Generate V1 API docs (unversioned /api endpoints)
dotnet tool run swagger tofile --output ./src/Fightarr.Api.V3/openapi.json "$outputFolder/$FRAMEWORK/$RUNTIME/$application" v1 &
pid1=$!

# Generate V5 API docs (/api/v5 endpoints)
dotnet tool run swagger tofile --output ./src/Fightarr.Api.V5/openapi.json "$outputFolder/$FRAMEWORK/$RUNTIME/$application" v5 &
pid2=$!

sleep 45

kill $pid1 2>/dev/null || true
kill $pid2 2>/dev/null || true

if [ ! -f $outputFileV1 ]; then
  echo "$outputFileV1 not found, check logs for errors"
  exit 1
fi

if [ ! -f $outputFileV5 ]; then
  echo "$outputFileV5 not found, check logs for errors"
  exit 1
fi

exit 0
