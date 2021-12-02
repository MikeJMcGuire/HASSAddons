ARG BUILD_FROM

FROM $BUILD_FROM AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:latest AS build
WORKDIR /src
COPY hass-actronque/hass-actronque.csproj hass-actronque/
RUN dotnet restore hass-actronque/hass-actronque.csproj
COPY . .
WORKDIR /src/hass-actronque
RUN dotnet build hass-actronque.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish hass-actronque.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "hass-actronque.dll"]
