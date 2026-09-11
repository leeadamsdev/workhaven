FROM node:24.20.0-bookworm-slim@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e AS build
WORKDIR /app

RUN corepack enable
COPY src/frontend/package.json src/frontend/pnpm-lock.yaml src/frontend/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY src/frontend/index.html src/frontend/tsconfig*.json src/frontend/vite.config.ts ./
COPY src/frontend/src/ ./src/
COPY src/frontend/public/ ./public/
RUN pnpm build

FROM nginxinc/nginx-unprivileged:1.30.4-alpine@sha256:442753882674b49ae2c1de83ed67896131c0777f56df5005e356e62bc3f7e7ce AS runtime
COPY --from=build /app/dist/ /usr/share/nginx/html/
