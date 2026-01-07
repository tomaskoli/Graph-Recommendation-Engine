FROM node:22-alpine AS build
WORKDIR /app

COPY src/Recommendation.Web/package.json src/Recommendation.Web/package-lock.json ./
RUN npm ci

COPY src/Recommendation.Web/ .
RUN npm run build

FROM nginx:alpine AS runtime

LABEL org.opencontainers.image.description="Recommendation Web Frontend"

RUN rm -rf /usr/share/nginx/html/*

COPY --from=build /app/dist /usr/share/nginx/html
COPY src/Recommendation.Web/nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]

