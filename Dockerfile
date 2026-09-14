FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5 AS publish

ARG BUILD_CONFIGURATION=Release
ARG CI
ARG VERSION
ARG SOURCE_DATE_EPOCH

WORKDIR /src

COPY src/DorisStorageAdapter.BagIt/DorisStorageAdapter.BagIt.csproj DorisStorageAdapter.BagIt/
COPY src/DorisStorageAdapter.Common/DorisStorageAdapter.Common.csproj DorisStorageAdapter.Common/
COPY src/DorisStorageAdapter.Server/DorisStorageAdapter.Server.csproj DorisStorageAdapter.Server/
COPY src/DorisStorageAdapter.Services/DorisStorageAdapter.Services.csproj DorisStorageAdapter.Services/
COPY src/Directory.Build.props .
COPY src/Directory.Packages.props .
RUN dotnet restore -p:CI=$CI DorisStorageAdapter.Server/DorisStorageAdapter.Server.csproj

COPY src .
RUN dotnet publish DorisStorageAdapter.Server/DorisStorageAdapter.Server.csproj \
-c $BUILD_CONFIGURATION \
-o /app/publish \
--no-restore \
-p:UseAppHost=false \
-p:MinVerVersionOverride=$VERSION \
-p:CI=$CI

RUN if [[ -n "${SOURCE_DATE_EPOCH}" ]]; then \
        SOURCE_DATE_FORMATTED="$(date -u -d "@${SOURCE_DATE_EPOCH}" '+%Y-%m-%d %H:%M:%S')" && \
        find /app/publish -exec touch -d "${SOURCE_DATE_FORMATTED}" --no-dereference {} +; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:1fe86375600b62e6566b465da9553eef0621f13c67f40fe764cd8dbb1dee1497 AS final
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DorisStorageAdapter.Server.dll"]