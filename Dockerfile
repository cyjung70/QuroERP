# syntax=docker/dockerfile:1
# 빌드 전용 SDK입니다. global.json의 .NET 10 SDK 요구사항을 만족해야 합니다.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# .dockerignore로 비밀 파일, Git 이력, 로컬 빌드 결과를 제외합니다.
COPY . .
RUN dotnet restore QuroErp.slnx -p:ContinuousIntegrationBuild=true
RUN dotnet build QuroErp.slnx -c Release --no-restore -warnaserror -p:ContinuousIntegrationBuild=true

# 선택적으로 컨테이너에서 테스트할 때 쓰는 단계입니다. 배포 워크플로는 기존 CI를 먼저 실행합니다.
FROM build AS tests
ENTRYPOINT ["dotnet", "test", "QuroErp.slnx", "-c", "Release", "--no-build", "--no-restore"]

# 호스트 세 개의 배포 파일을 분리합니다. SDK/소스 전체를 최종 이미지에 넣지 않습니다.
FROM build AS publish
RUN dotnet publish src/QuroErp.Api/QuroErp.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false -p:ContinuousIntegrationBuild=true \
    && dotnet publish src/QuroErp.Web/QuroErp.Web.csproj -c Release --no-restore -o /out/web /p:UseAppHost=false -p:ContinuousIntegrationBuild=true \
    && dotnet publish src/QuroERP.Database/QuroERP.Database.csproj -c Release --no-restore -o /out/migrate /p:UseAppHost=false -p:ContinuousIntegrationBuild=true

# API/Web 공통 런타임입니다. curl은 상태 검사, app 계정은 비루트 실행에 사용합니다.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web-runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet
WORKDIR /app
# dev 서버에서도 상세 오류/샘플 화면 노출을 막기 위해 Production으로 실행합니다.
ENV ASPNETCORE_ENVIRONMENT=Production
USER app

# API는 Compose 내부 HTTP 8080으로 통신합니다.
FROM web-runtime AS api
COPY --from=publish /out/api .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuroErp.Api.dll"]

# Web HTTPS 인증서는 이미지에 넣지 않고 서버 파일을 읽기 전용으로 연결합니다.
FROM web-runtime AS web
COPY --from=publish /out/web .
EXPOSE 8443
ENTRYPOINT ["dotnet", "QuroErp.Web.dll"]

# 마이그레이션은 일회성 콘솔이며 ASP.NET 런타임이 필요하지 않습니다.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migrate
WORKDIR /app
COPY --from=publish /out/migrate .
USER app
ENTRYPOINT ["dotnet", "QuroERP.Database.dll"]
