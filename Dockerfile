FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:f2f0cb3af991eb6959c8a20551b0152f10cce61354c089dd863a7b72c0f00fea AS publish

ARG BUILD_CONFIGURATION=Release
ARG CI
ARG VERSION
ARG SOURCE_DATE_EPOCH

WORKDIR /src

COPY src/DorisStorageAdapter.Helpers/DorisStorageAdapter.Helpers.csproj DorisStorageAdapter.Helpers/
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

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:590a934508c35c2ef61203ded76f088f5ec6263b863b55d3deaea8fc2d6b55a6 AS final
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DorisStorageAdapter.Server.dll"]