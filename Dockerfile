FROM mcr.microsoft.com/dotnet/sdk:10.0.201@sha256:478b9038d187e5b5c29bfa8173ded5d29e864b5ad06102a12106380ee01e2e49 AS publish

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.5@sha256:a04d1c1d2d26119049494057d80ea6cda25bbd8aef7c444a1fc1ef874fd3955b AS final
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DorisStorageAdapter.Server.dll"]