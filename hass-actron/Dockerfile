FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
RUN apk add --no-cache icu-libs tzdata && rm -rf /var/cache/apk/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
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
