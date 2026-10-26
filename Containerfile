# Local source build: podman build --pull=always -f Containerfile -t shirobot:local .
# CI (.github/workflows/build.yml) uses the `prebuilt` target with the framework-dependent single-file
# release binary in artifacts/docker/<arch>/ShiroBot and publishes a linux/amd64 + linux/arm64 image to ghcr.io.
# The image binary is the same file the Dashboard self-update downloads; newer ones go to /data/host.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish Core/ShiroBot.csproj \
    --configuration Release \
    --runtime "linux-musl-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)" \
    --self-contained false \
    --output /out \
    -p:PublishSingleFile=true \
    -p:PublishAot=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -p:CopyOutputSymbolsToPublishDirectory=false \
    -p:RequireDashboard=true
RUN install -D -m 755 /out/ShiroBot /layers/ShiroBot && touch -t 197001010000.00 /layers/ShiroBot

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime-base
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN apk add --no-cache libstdc++ libgcc icu-libs icu-data-full tzdata fontconfig font-noto-cjk
RUN apk add --no-cache font-noto-emoji
ENV SHIROBOT_DEFAULT_FONT_FAMILY="Noto Sans CJK SC" \
    SHIROBOT_EMOJI_FONT_FAMILY="Noto Color Emoji" \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/cache/bundle \
    SHIROBOT_HOME=/app \
    SHIROBOT_HOST_UPDATE_DIR=/data/host
COPY docker/fonts.conf /etc/fonts/conf.d/99-shirobot-fonts.conf
RUN fc-cache -f
WORKDIR /app
COPY docker/entrypoint.sh /usr/local/bin/shirobot-entrypoint
COPY docker/config.container.toml /app/config.container.toml
LABEL org.opencontainers.image.source="https://github.com/ShirokaProject/ShiroBot"
RUN chmod +x /usr/local/bin/shirobot-entrypoint \
    && mkdir -p /data/adapters /data/plugins /app/cache \
    && ln -s /data/adapters /app/adapters \
    && chown -R 1654:1654 /data /app/cache
USER 1654:1654
VOLUME ["/data"]
EXPOSE 7001
ENTRYPOINT ["/usr/local/bin/shirobot-entrypoint"]

FROM runtime-base AS prepare-prebuilt
USER root
ARG TARGETARCH
COPY artifacts/docker/${TARGETARCH}/ShiroBot /layers/ShiroBot
# Normalize inside the builder, after it has checked the actual input contents.
RUN chmod 755 /layers/ShiroBot && touch -t 197001010000.00 /layers/ShiroBot

FROM runtime-base AS prebuilt
# Shown by the Dashboard as the image tag and image build time.
ARG VERSION_TAG=""
ARG BUILD_TIME=""
ENV SHIROBOT_VERSION_TAG=${VERSION_TAG} SHIROBOT_BUILD_TIME=${BUILD_TIME}
COPY --from=prepare-prebuilt /layers/ShiroBot /app/ShiroBot

FROM runtime-base AS final
COPY --from=publish /layers/ShiroBot /app/ShiroBot
