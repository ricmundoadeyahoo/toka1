<#
.SYNOPSIS
  Ejecuta los tests unitarios (.NET en contenedor + front-end) y abre el reporte de cobertura.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
#>
param([switch]$SkipFrontend, [switch]$NoOpen)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "=== Tests .NET + cobertura (contenedor sdk:8.0) ===" -ForegroundColor Cyan
docker compose -f docker-compose.tests.yml run --rm --build tests
$dotnetExit = $LASTEXITCODE

$frontendExit = 0
if (-not $SkipFrontend) {
    Write-Host "`n=== Tests front-end (Jest + React Testing Library) ===" -ForegroundColor Cyan
    docker run --rm -v "${root}\frontend:/app" -w /app node:20-alpine sh -c "set -o pipefail; npm ci --no-audit --no-fund && npx jest --coverage --ci --coverageReporters=text-summary --coverageReporters=html 2>&1 | tee /app/coverage-summary.txt"
    $frontendExit = $LASTEXITCODE
}

# Copia los resúmenes a docs/ para versionarlos en GitHub (el HTML completo queda en coverage/, ignorado por git)
$docsCoverage = Join-Path $root "docs\02-microservicios\coverage"
foreach ($f in @("Summary.md", "Summary.txt", "SummaryGithub.md")) {
    $src = Join-Path $root "coverage\report\$f"
    if (Test-Path $src) { Copy-Item $src (Join-Path $docsCoverage $f) -Force }
}
$feSummary = Join-Path $root "frontend\coverage-summary.txt"
if (Test-Path $feSummary) {
    Copy-Item $feSummary (Join-Path $docsCoverage "frontend-coverage.txt") -Force
    Copy-Item $feSummary (Join-Path $root "docs\03-frontend\coverage-summary.txt") -Force
}
Write-Host "Resúmenes de cobertura copiados a docs\02-microservicios\coverage" -ForegroundColor Green

$report = Join-Path $root "coverage\report\index.html"
if ((Test-Path $report) -and -not $NoOpen) { Start-Process $report }

if ($dotnetExit -ne 0 -or $frontendExit -ne 0) { Write-Host "Hay tests fallidos" -ForegroundColor Red; exit 1 }
Write-Host "Todos los tests pasaron" -ForegroundColor Green
