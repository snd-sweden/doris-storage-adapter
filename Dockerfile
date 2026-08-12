FROM mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1fc6e423f543119c406d24e2e687d67c569f18f04a37a8b0005d80ad0dcee80 AS publish

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11@sha256:207cc51496778557731c81ff670333d8ade4a4fec22768fd1be8e78474a84ecf AS final
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DorisStorageAdapter.Server.dll"]