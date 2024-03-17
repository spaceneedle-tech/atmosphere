@echo off

cls

echo Removing docker image
echo docker rmi atmosphere
docker rmi atmosphere
echo.

echo Building docker image
echo docker build --tag atmosphere --progress=plain --file Dockerfile ../
docker build --tag atmosphere --progress=plain --file Dockerfile ../
echo.

docker images --filter reference=atmosphere