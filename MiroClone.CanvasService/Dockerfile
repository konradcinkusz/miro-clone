# Production image for MiroClone.CanvasService (WP13, ADR 0017). Build context: the repository root, as flyio/*.fly.toml declares.
# Stage 1 restores against project files only, so the layer is reused until a project file changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Versions, analyzer settings and the SDK pin are read from these files at restore and build time.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./

COPY MiroClone.ServiceDefaults/MiroClone.ServiceDefaults.csproj MiroClone.ServiceDefaults/
COPY MiroClone.CanvasService/MiroClone.CanvasService.csproj MiroClone.CanvasService/
RUN dotnet restore MiroClone.CanvasService/MiroClone.CanvasService.csproj

COPY MiroClone.ServiceDefaults/ MiroClone.ServiceDefaults/
COPY MiroClone.CanvasService/ MiroClone.CanvasService/
RUN dotnet publish MiroClone.CanvasService/MiroClone.CanvasService.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# Runtime major (10) equals the target framework major: roll-forward does not cross a major.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/miro-clone
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The image's unprivileged user (UID 1654). Port 8080 needs no root.
USER app
ENTRYPOINT ["dotnet", "MiroClone.CanvasService.dll"]
