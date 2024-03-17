#!/bin/bash

# Clear the console
clear

echo "Removing docker image"
echo "docker rmi atmosphere"
docker rmi atmosphere
echo ""

echo "Building docker image"
echo "docker build --tag atmosphere --progress=plain --file Dockerfile ../src"
docker build --tag atmosphere --progress=plain --file Dockerfile ../src
echo ""

docker images --filter reference=atmosphere
