# syntax=docker/dockerfile:1
# Contexto de build: raíz del repositorio.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/User/ src/Services/User/
RUN dotnet restore src/Services/User/Toka.User.Api/Toka.User.Api.csproj
RUN dotnet publish src/Services/User/Toka.User.Api/Toka.User.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Toka.User.Api.dll"]
