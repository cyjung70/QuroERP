# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore QuroErp.slnx -p:ContinuousIntegrationBuild=true
RUN dotnet build QuroErp.slnx -c Release --no-restore -warnaserror -p:ContinuousIntegrationBuild=true

FROM build AS tests
ENTRYPOINT ["dotnet", "test", "QuroErp.slnx", "-c", "Release", "--no-build", "--no-restore"]

FROM build AS publish
RUN dotnet publish src/QuroErp.Api/QuroErp.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false -p:ContinuousIntegrationBuild=true \
    && dotnet publish src/QuroErp.Web/QuroErp.Web.csproj -c Release --no-restore -o /out/web /p:UseAppHost=false -p:ContinuousIntegrationBuild=true \
    && dotnet publish src/QuroERP.Database/QuroERP.Database.csproj -c Release --no-restore -o /out/migrate /p:UseAppHost=false -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web-runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
USER app

FROM web-runtime AS api
COPY --from=publish /out/api .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuroErp.Api.dll"]

FROM web-runtime AS web
COPY --from=publish /out/web .
EXPOSE 8443
ENTRYPOINT ["dotnet", "QuroErp.Web.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migrate
WORKDIR /app
COPY --from=publish /out/migrate .
USER app
ENTRYPOINT ["dotnet", "QuroERP.Database.dll"]
