# The demo seed as a one-shot container (ADR 0050), run by compose.yaml after the services are healthy. Build context: the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY MiroClone.Seed/MiroClone.Seed.csproj MiroClone.Seed/
RUN dotnet restore MiroClone.Seed/MiroClone.Seed.csproj

COPY MiroClone.Seed/ MiroClone.Seed/
RUN dotnet publish MiroClone.Seed/MiroClone.Seed.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# The seed uses the plain runtime: it hosts nothing, it only calls the services.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/miro-clone
COPY --from=build /app/publish .
# Development only. compose.yaml runs it as root so it can read authservice's log over the Docker socket (the dev outbox).
ENTRYPOINT ["dotnet", "MiroClone.Seed.dll"]
