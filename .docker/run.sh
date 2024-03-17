#!/bin/bash

# Clear the console
# clear

echo "Stopping docker container"
echo "docker stop atmosphere"
docker stop atmosphere
echo ""

echo "Removing docker container"
echo "docker rm atmosphere"
docker rm atmosphere
echo ""

echo "Running docker container"
echo "docker run --name atmosphere --publish 8007:80 -it -d atmosphere"
docker run --name atmosphere --publish 8007:80 -it -d atmosphere
echo ""

docker ps --filter name=atmosphere

echo ""
echo "Retrieving updated Metadata"
echo "curl http://localhost:8007/.metadata"
curl http://localhost:8007/.metadata
echo ""
