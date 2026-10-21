# syntax=docker/dockerfile:1
# Ejecuta TODOS los tests unitarios y genera el reporte de cobertura sin necesidad de instalar
# el SDK de .NET en Windows. Contexto de build: raíz del repositorio.
#   docker compose -f docker-compose.tests.yml run --rm tests
# Resultado: ./coverage/report/index.html y ./coverage/report/Summary.txt
FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src
RUN dotnet tool install --global dotnet-reportgenerator-globaltool
ENV PATH="${PATH}:/root/.dotnet/tools"

COPY Directory.Build.props Directory.Packages.props coverage.runsettings ./
COPY src/ src/
COPY tests/ tests/
RUN for p in tests/*/*.csproj; do dotnet restore "$p" || exit 1; done

CMD ["sh", "-c", "rm -rf /coverage/raw /coverage/report; status=0; for p in tests/*/*.csproj; do dotnet test \"$p\" --no-restore --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory /coverage/raw --logger 'console;verbosity=normal' || status=1; done; reportgenerator -reports:'/coverage/raw/**/coverage.cobertura.xml' -targetdir:/coverage/report -reporttypes:'Html;TextSummary;Cobertura;MarkdownSummary;MarkdownSummaryGithub'; cat /coverage/report/Summary.txt; exit $status"]
