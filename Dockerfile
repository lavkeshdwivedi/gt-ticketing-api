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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Built-in non-root user in the official .NET images.
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ticketing.Api.dll"]
