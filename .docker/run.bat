@echo off

cls

echo Stopping docker container
echo docker stop atmosphere
docker stop atmosphere
echo.

echo Removing docker container
echo docker rm atmosphere
docker rm atmosphere
echo.

echo Running docker container
echo docker run --name atmosphere --publish 8007:8080 -it -d atmosphere
docker run --name atmosphere --publish 8007:8080 -it -d atmosphere
echo.

docker ps --filter name=atmosphere

echo.
echo Retrieving updated Metadata
curl http://localhost:8007/.metadata
echo