# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer, so code edits don't invalidate the package cache.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Ticketing.Domain/Ticketing.Domain.csproj src/Ticketing.Domain/
COPY src/Ticketing.Application/Ticketing.Application.csproj src/Ticketing.Application/
COPY src/Ticketing.Infrastructure/Ticketing.Infrastructure.csproj src/Ticketing.Infrastructure/
COPY src/Ticketing.Api/Ticketing.Api.csproj src/Ticketing.Api/
RUN dotnet restore src/Ticketing.Api/Ticketing.Api.csproj

COPY src/ src/
RUN dotnet publish src/Ticketing.Api/Ticketing.Api.csproj -c Release -o /app --no-restore

# One-shot schema migrator: an EF Core migration bundle (a single executable that applies any
# pending migrations and exits). Runs at deploy time, before the API, so app instances never
# race each other on schema changes and the API needs no DDL permissions.
FROM build AS migrator-build
COPY .config/ .config/
RUN dotnet tool restore  && dotnet ef migrations bundle --project src/Ticketing.Infrastructure --startup-project src/Ticketing.Infrastructure       --configuration Release --output /bundle/efbundle --force

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migrator
COPY --from=migrator-build /bundle/efbundle /efbundle
USER app
ENTRYPOINT ["/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Built-in non-root user in the official .NET images.
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ticketing.Api.dll"]
