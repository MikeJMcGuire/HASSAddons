ARG BUILD_FROM

FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine AS base
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY hass-blueriiot/hass-blueriiot.csproj hass-blueriiot/
RUN dotnet restore hass-blueriiot/hass-blueriiot.csproj
COPY . .
WORKDIR /src/hass-blueriiot
RUN dotnet build hass-blueriiot.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish hass-blueriiot.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "hass-blueriiot.dll"]