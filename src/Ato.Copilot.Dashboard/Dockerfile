# ─── Stage 1: Build React SPA ────────────────────────────────────
FROM node:20-alpine AS build

WORKDIR /app

# Copy package files for layer caching
COPY src/Ato.Copilot.Dashboard/package.json src/Ato.Copilot.Dashboard/package-lock.json ./

RUN npm ci

# Copy source and build
COPY src/Ato.Copilot.Dashboard/ ./
RUN rm -rf dist tsconfig.tsbuildinfo

# API calls use relative /api path — nginx proxies to MCP server
ENV VITE_API_BASE_URL=/api/dashboard
RUN npm run build

# ─── Stage 2: Serve with nginx ──────────────────────────────────
FROM nginx:stable-alpine AS runtime

# Remove default config
RUN rm /etc/nginx/conf.d/default.conf

# Default MCP upstream for local docker-compose runs; overridden in Container Apps.
ENV MCP_BASE_URL=http://ato-copilot:3001

# Copy nginx template to enable runtime env var substitution.
COPY src/Ato.Copilot.Dashboard/nginx.conf /etc/nginx/templates/default.conf.template

# Copy built SPA
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
