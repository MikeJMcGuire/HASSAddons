ARG BUILD_FROM

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
RUN apk add --no-cache icu-libs tzdata && rm -rf /var/cache/apk/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
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

RUN apk add --no-cache icu-libs
ENTRYPOINT ["dotnet", "hass-actronque.dll"]
