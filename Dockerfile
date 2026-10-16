FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json VeganAdda.sln ./
COPY src/ ./src/
RUN dotnet restore src/VeganAdda.Web/VeganAdda.Web/VeganAdda.Web.csproj
RUN dotnet publish src/VeganAdda.Web/VeganAdda.Web/VeganAdda.Web.csproj -c Release --no-restore -o /out -m:1 /nr:false

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
