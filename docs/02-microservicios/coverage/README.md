# Reporte de cobertura (.NET)

Este folder guarda el **resumen versionado** del reporte de cobertura de los tests unitarios .NET.

Se genera en tu equipo (no requiere el SDK de .NET, corre en contenedor):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

El script ejecuta todos los proyectos de [`tests/`](../../../tests), genera el reporte con ReportGenerator y copia aquí:

- `Summary.md` — resumen en Markdown (se ve directo en GitHub)
- `Summary.txt` — resumen en texto
- `frontend-coverage.txt` — resumen de cobertura del front-end (Jest)

El HTML navegable completo queda en `coverage/report/index.html` en la raíz del repositorio (excluido de git por tamaño).

> Objetivo del requerimiento: ≥ 70 %. Alcance medido: capas Domain y Application de cada microservicio + Contracts compartidos (ver [`coverage.runsettings`](../../../coverage.runsettings)).
