# syntax=docker/dockerfile:1
# Build multi-stage. El contexto de build es la RAÍZ del repositorio (por BuildingBlocks compartidos).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Auth/ src/Services/Auth/
RUN dotnet restore src/Services/Auth/Toka.Auth.Api/Toka.Auth.Api.csproj
RUN dotnet publish src/Services/Auth/Toka.Auth.Api/Toka.Auth.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# Directorio para la clave de firma (volumen); propiedad del usuario no-root "app".
RUN mkdir -p /keys && chown $APP_UID /keys
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Toka.Auth.Api.dll"]
