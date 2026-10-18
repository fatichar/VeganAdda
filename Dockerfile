FROM node:22-bookworm-slim AS firebase-client
WORKDIR /client
COPY src/VeganAdda.Web/VeganAdda.Web.Client/package*.json ./
RUN npm ci
COPY src/VeganAdda.Web/VeganAdda.Web.Client/src/ ./src/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json VeganAdda.sln ./
COPY src/ ./src/
COPY --from=firebase-client /client/wwwroot/js/firebase-auth.js ./src/VeganAdda.Web/VeganAdda.Web.Client/wwwroot/js/firebase-auth.js
RUN dotnet restore src/VeganAdda.Web/VeganAdda.Web/VeganAdda.Web.csproj
RUN dotnet publish src/VeganAdda.Web/VeganAdda.Web/VeganAdda.Web.csproj -c Release --no-restore -p:SkipFirebaseClientBuild=true -o /out -m:1 /nr:false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl libfontconfig1 && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/media /data/keys && chown -R app:app /data
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_HTTPS_PORT=443 ASPNETCORE_ENVIRONMENT=Production Storage__MediaPath=/data/media Storage__KeysPath=/data/keys
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "VeganAdda.Web.dll"]
