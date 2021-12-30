ARG BUILD_FROM

FROM $BUILD_FROM AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:latest AS build
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