# 容器部署与构建

此目录集中保存镜像构建文件、构建排除规则、Compose、入口脚本、默认配置、字体配置和启动验证脚本。
以下命令均在仓库根目录运行。

## 使用发布镜像

```sh
docker compose -f container/compose.yaml pull
docker compose -f container/compose.yaml up -d
docker compose -f container/compose.yaml logs -f shirobot
```

数据使用固定名称的 `shirobot_data` 卷，移动 Compose 文件不会改变数据卷名称。时区、镜像版本与协议端文件共享的配置见 [安装文档](../docs/guide/installation.md)。

## 从源码构建

构建上下文必须是仓库根目录，以便读取 `Core`、`SDK` 等源码：

```sh
docker build -f container/Containerfile -t shirobot:local .
# 或使用 Podman，显式指定同一份构建排除规则：
podman build --ignorefile container/Containerfile.dockerignore -f container/Containerfile -t shirobot:local .
```

`Containerfile.dockerignore` 是 [Docker 支持的构建文件专用排除规则](https://docs.docker.com/build/concepts/context/#filename-and-location)；Podman 使用 [`--ignorefile`](https://docs.podman.io/en/stable/markdown/podman-build.1.html) 指向该文件。

CI 将同批发布的二进制放在 `artifacts/container/<amd64|arm64>/ShiroBot`，用 `prebuilt` 阶段构建镜像。

## 验证镜像启动

```sh
sh container/verify-startup.sh shirobot:local linux/arm64
```

脚本验证非 root 启动、配置初始化、复用数据卷，以及镜像升级时主程序的选择，使用独立的测试容器和数据卷。
