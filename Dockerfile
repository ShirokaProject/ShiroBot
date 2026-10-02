# Local source build: docker build -t shirobot:local .
# CI (.github/workflows/build.yml) uses the `prebuilt` target with the verified release binaries in
# artifacts/docker/<arch>/ and publishes a linux/amd64 + linux/arm64 image to ghcr.io.
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
    -p:RequireDashboard=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime-base
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN apk add --no-cache libstdc++ libgcc icu-libs icu-data-full tzdata fontconfig font-noto-cjk
RUN apk add --no-cache font-noto-emoji
ENV SHIROBOT_DEFAULT_FONT_FAMILY="Noto Sans CJK SC" \
    SHIROBOT_EMOJI_FONT_FAMILY="Noto Color Emoji"
COPY docker/fonts.conf /etc/fonts/conf.d/99-shirobot-fonts.conf
RUN fc-cache -f
WORKDIR /app
COPY docker/entrypoint.sh /usr/local/bin/shirobot-entrypoint
COPY docker/config.container.toml /app/config.container.toml
LABEL org.opencontainers.image.source="https://github.com/ShirokaProject/ShiroBot"
RUN chmod +x /usr/local/bin/shirobot-entrypoint \
    && mkdir -p /data/adapters /data/plugins \
    && ln -s /data/adapters /app/adapters \
    && chown -R 1654:1654 /data
USER 1654:1654
VOLUME ["/data"]
EXPOSE 7001
ENTRYPOINT ["/usr/local/bin/shirobot-entrypoint"]

FROM runtime-base AS prebuilt
ARG TARGETARCH
# Shown by the Dashboard as the image tag and image build time.
ARG VERSION_TAG=""
ARG BUILD_TIME=""
ENV SHIROBOT_VERSION_TAG=${VERSION_TAG} SHIROBOT_BUILD_TIME=${BUILD_TIME}
COPY artifacts/docker/${TARGETARCH}/ /app/

FROM runtime-base AS final
COPY --from=publish /out/ /app/
