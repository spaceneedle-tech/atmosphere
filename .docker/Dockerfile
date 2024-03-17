ARG APP_NAME=Atmosphere

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS base

ARG APP_NAME

RUN apt-get update && apt-get install -y libicu-dev

WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build

ARG APP_NAME


WORKDIR /
COPY . .

RUN dotnet build /src/${APP_NAME}/${APP_NAME}.csproj -c Release -o ${APP_DIR}/build

FROM build AS publish

ARG APP_NAME

RUN dotnet publish /src/${APP_NAME}/${APP_NAME}.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Atmosphere.dll"] 