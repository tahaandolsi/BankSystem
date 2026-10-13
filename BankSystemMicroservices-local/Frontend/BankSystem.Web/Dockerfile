# Build the Angular application.
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# Serve the compiled SPA and fall back to index.html for Angular routes.
FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/BankSystem.Web/browser /usr/share/nginx/html

EXPOSE 80
