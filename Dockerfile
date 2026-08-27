FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DataMigrator.csproj", "nuget.config", "./"]
RUN dotnet restore "DataMigrator.csproj" --configfile nuget.config
COPY . .
RUN dotnet publish "DataMigrator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DataMigrator.dll"]

LABEL org.opencontainers.image.title="ATSPM DataMigrator"
LABEL org.opencontainers.image.description="Utility for migrating ATSPM 4.3 data into ATSPM 5"
LABEL org.opencontainers.image.vendor="OpenSourceTransportation"
LABEL org.opencontainers.image.source="https://github.com/opensourcetransportation/ATSPM-43-to-5-Data-Migration"
LABEL org.opencontainers.image.documentation="https://github.com/opensourcetransportation/ATSPM-43-to-5-Data-Migration/tree/main/docs"
LABEL org.opencontainers.image.licenses="Apache-2.0"
