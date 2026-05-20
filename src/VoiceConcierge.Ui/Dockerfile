# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /ui
COPY src/VoiceConcierge.Ui/package.json src/VoiceConcierge.Ui/package-lock.json* ./
RUN npm ci --no-audit --no-fund
COPY src/VoiceConcierge.Ui/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /ui/dist /usr/share/nginx/html
COPY src/VoiceConcierge.Ui/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
