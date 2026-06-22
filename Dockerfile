FROM mcr.microsoft.com/dotnet/sdk:10.0.301@sha256:548d93f8a18a1acbe6cc127bc4f47281430d34a9e35c18afa80a8d6741c2adc3 AS publish

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9@sha256:ddcf70ad1ab963a4fcd41fbd722a6b660e404e87567cfbd46fd2809c21b02088 AS final
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DorisStorageAdapter.Server.dll"]