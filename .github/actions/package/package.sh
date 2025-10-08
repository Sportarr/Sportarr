#!/bin/bash

outputFolder=_output
artifactsFolder=_artifacts
uiFolder="$outputFolder/UI"
framework="${FRAMEWORK:=net8.0}"

rm -rf $artifactsFolder
mkdir $artifactsFolder

for runtime in _output/*
do
  name="${runtime##*/}"
  folderName="$runtime/$framework"
  fightarrFolder="$folderName/Fightarr"
  archiveName="Fightarr.$BRANCH.$FIGHTARR_VERSION.$name"

  if [[ "$name" == 'UI' ]]; then
    continue
  fi
    
  echo "Creating package for $name"

  echo "Copying UI"
  cp -r $uiFolder $fightarrFolder
  
  echo "Setting permissions"
  find $fightarrFolder -name "ffprobe" -exec chmod a+x {} \;
  find $fightarrFolder -name "Fightarr" -exec chmod a+x {} \;
  find $fightarrFolder -name "Fightarr.Update" -exec chmod a+x {} \;
  
  if [[ "$name" == *"osx"* ]]; then
    echo "Creating macOS package"
      
    packageName="$name-app"
    packageFolder="$outputFolder/$packageName"
      
    rm -rf $packageFolder
    mkdir $packageFolder
      
    cp -r distribution/macOS/Fightarr.app $packageFolder
    mkdir -p $packageFolder/Fightarr.app/Contents/MacOS
      
    echo "Copying Binaries"
    cp -r $fightarrFolder/* $packageFolder/Fightarr.app/Contents/MacOS
      
    echo "Removing Update Folder"
    rm -r $packageFolder/Fightarr.app/Contents/MacOS/Fightarr.Update
              
    echo "Packaging macOS app Artifact"
    (cd $packageFolder; zip -rq "../../$artifactsFolder/$archiveName-app.zip" ./Fightarr.app)
  fi

  echo "Packaging Artifact"
  if [[ "$name" == *"linux"* ]] || [[ "$name" == *"osx"* ]] || [[ "$name" == *"freebsd"* ]]; then
    tar -zcf "./$artifactsFolder/$archiveName.tar.gz" -C $folderName Fightarr
	fi
    
  if [[ "$name" == *"win"* ]]; then
    if [ "$RUNNER_OS" = "Windows" ]
      then
        (cd $folderName; 7z a -tzip "../../../$artifactsFolder/$archiveName.zip" ./Fightarr)
      else
      (cd $folderName; zip -rq "../../../$artifactsFolder/$archiveName.zip" ./Fightarr)
    fi
	fi
done
