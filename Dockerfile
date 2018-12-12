ARG BUILD_FROM

FROM $BUILD_FROM AS base
WORKDIR /app

FROM microsoft/dotnet:2.0-sdk AS build
WORKDIR /src
COPY hass-actron/hass-actron.csproj hass-actron/
RUN dotnet restore hass-actron/hass-actron.csproj
COPY . .
WORKDIR /src/hass-actron
RUN dotnet build hass-actron.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish hass-actron.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "hass-actron.dll"]
