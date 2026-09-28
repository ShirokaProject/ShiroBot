FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY . .
RUN dotnet publish Core/ShiroBot.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output /out \
    -p:PublishSingleFile=true \
    -p:PublishAot=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:RequireDashboard=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install --no-install-recommends -y libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /out/ /app/
COPY docker/entrypoint.sh /usr/local/bin/shirobot-entrypoint
COPY docker/config.container.toml /app/config.container.toml
RUN chmod +x /app/ShiroBot /usr/local/bin/shirobot-entrypoint \
    && mkdir -p /data/adapters /data/plugins \
    && ln -s /data/adapters /app/adapters \
    && chown -R 1654:1654 /data
USER 1654:1654
VOLUME ["/data"]
EXPOSE 7001
ENTRYPOINT ["/usr/local/bin/shirobot-entrypoint"]
