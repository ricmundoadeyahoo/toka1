<#
.SYNOPSIS
  Prueba de humo end-to-end del sistema Toka a través del API Gateway (compatible con Windows PowerShell 5.1 y PowerShell 7).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -SkipAi
#>
param(
    [string]$Gateway = "http://localhost:5000",
    [string]$AdminEmail = "admin@toka.local",
    [string]$AdminPassword = "Admin123!",
    [switch]$SkipAi
)

$ErrorActionPreference = "Stop"
$script:failures = 0

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "  [OK] $text" -ForegroundColor Green }
function Write-Fail($text) { Write-Host "  [FALLO] $text" -ForegroundColor Red; $script:failures++ }

function Invoke-Api {
    param([string]$Method = "GET", [string]$Path, $Body = $null, [string]$Token = $null, [int]$TimeoutSec = 60)
    $headers = @{ "X-Correlation-ID" = "smoke-" + [guid]::NewGuid().ToString("N").Substring(0, 12) }
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$Gateway$Path"; Headers = $headers; TimeoutSec = $TimeoutSec }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 10
        $params["Body"] = [System.Text.Encoding]::UTF8.GetBytes($json)
        $params["ContentType"] = "application/json; charset=utf-8"
    }
    return Invoke-RestMethod @params
}

function Get-StatusCode($errorRecord) {
    try { return [int]$errorRecord.Exception.Response.StatusCode } catch { return -1 }
}

# ---------------------------------------------------------------------------------
Write-Step "1. Salud del gateway y descubrimiento OIDC"
try {
    Invoke-RestMethod "$Gateway/health/live" -TimeoutSec 10 | Out-Null
    Write-Ok "Gateway vivo"
    $oidc = Invoke-Api -Path "/.well-known/openid-configuration"
    Write-Ok "Issuer: $($oidc.issuer) · JWKS: $($oidc.jwks_uri)"
    $jwks = Invoke-Api -Path "/.well-known/jwks.json"
    Write-Ok "JWKS publica $($jwks.keys.Count) clave(s) RS256 (kid=$($jwks.keys[0].kid))"
} catch { Write-Fail "Gateway/Auth no responde: $($_.Exception.Message)"; exit 1 }

# ---------------------------------------------------------------------------------
Write-Step "2. Login del administrador (Auth → REST → Role Service)"
$admin = Invoke-Api -Method POST -Path "/api/auth/login" -Body @{ email = $AdminEmail; password = $AdminPassword }
$adminToken = $admin.accessToken
Write-Ok "Token emitido (expira en $($admin.expiresIn)s) · roles: $($admin.user.roles -join ', ')"

# ---------------------------------------------------------------------------------
Write-Step "3. Registro de usuario (evento asíncrono RabbitMQ → User + Role + Audit)"
$suffix = Get-Random -Maximum 999999
$email = "qa.$suffix@toka.com"
$password = "Prueba#2026"
$reg = Invoke-Api -Method POST -Path "/api/auth/register" -Body @{ email = $email; password = $password; fullName = "Usuario QA $suffix"; department = "Calidad" }
$newUserId = $reg.userId
Write-Ok "Registrado $email ($newUserId)"

$profile = $null
for ($i = 0; $i -lt 15 -and -not $profile; $i++) {
    Start-Sleep -Seconds 1
    try { $profile = Invoke-Api -Path "/api/users/$newUserId" -Token $adminToken } catch { }
}
if ($profile) { Write-Ok "User Service creó el perfil vía evento: $($profile.fullName) / $($profile.department)" } else { Write-Fail "El perfil no se creó (revisar RabbitMQ / user-service)" }

$roles = Invoke-Api -Path "/api/roles/users/$newUserId" -Token $adminToken
if ($roles.roles -contains "User") { Write-Ok "Role Service asignó rol por defecto: $($roles.roles -join ', ')" } else { Write-Fail "Rol por defecto no asignado" }

# ---------------------------------------------------------------------------------
Write-Step "4. Autorización distribuida (JWT validado localmente en cada servicio)"
$userLogin = Invoke-Api -Method POST -Path "/api/auth/login" -Body @{ email = $email; password = $password }
try {
    Invoke-Api -Path "/api/users" -Token $userLogin.accessToken | Out-Null
    Write-Fail "Un usuario sin users:read pudo listar usuarios"
} catch {
    if ((Get-StatusCode $_) -eq 403) { Write-Ok "Usuario estándar recibe 403 en /api/users" } else { Write-Fail "Se esperaba 403: $($_.Exception.Message)" }
}
$me = Invoke-Api -Path "/api/users/me" -Token $userLogin.accessToken
Write-Ok "El usuario sí puede ver su propio perfil: $($me.email)"

# ---------------------------------------------------------------------------------
Write-Step "5. Validación de entrada (OWASP) y bloqueo por fuerza bruta"
try {
    Invoke-Api -Method POST -Path "/api/auth/register" -Body @{ email = "x$suffix@toka.com"; password = "debil"; fullName = "<script>alert(1)</script>" } | Out-Null
    Write-Fail "Se aceptó un registro inválido"
} catch { if ((Get-StatusCode $_) -eq 400) { Write-Ok "Registro inválido rechazado con 400" } else { Write-Fail "Código inesperado" } }

for ($i = 1; $i -le 5; $i++) {
    try { Invoke-Api -Method POST -Path "/api/auth/login" -Body @{ email = $email; password = "Incorrecta#$i" } | Out-Null } catch { }
}
try {
    Invoke-Api -Method POST -Path "/api/auth/login" -Body @{ email = $email; password = $password } | Out-Null
    Write-Fail "La cuenta no se bloqueó tras 5 intentos"
} catch {
    $code = Get-StatusCode $_
    if ($code -eq 403) { Write-Ok "Cuenta bloqueada temporalmente tras 5 intentos (403)" }
    elseif ($code -eq 429) { Write-Ok "Rate limit por IP activo (429) — protección anti fuerza bruta" }
    else { Write-Fail "Código inesperado $code" }
}

# ---------------------------------------------------------------------------------
Write-Step "6. Gestión de roles y caché (Role Service + Redis)"
$assigned = Invoke-Api -Method POST -Path "/api/roles/users/$newUserId/assign" -Body @{ roleName = "Auditor" } -Token $adminToken
Write-Ok "Roles ahora: $($assigned.roles -join ', ') · permisos: $($assigned.permissions -join ', ')"

# ---------------------------------------------------------------------------------
Write-Step "7. Auditoría en MongoDB (consumidor de todos los eventos)"
Start-Sleep -Seconds 2
$audit = Invoke-Api -Path "/api/audit?subjectId=$newUserId&pageSize=50" -Token $adminToken
$types = $audit.items | ForEach-Object { $_.eventType } | Sort-Object -Unique
Write-Ok "$($audit.totalCount) eventos del usuario: $($types -join ', ')"
$summary = Invoke-Api -Path "/api/audit/summary?hours=1" -Token $adminToken
Write-Ok "Última hora: $($summary.total) eventos, $($summary.failures) fallos"

# ---------------------------------------------------------------------------------
if (-not $SkipAi) {
    Write-Step "8. Agente de IA (DeepSeek + RAG con Qdrant + tools)"
    $status = $null
    for ($i = 0; $i -lt 60; $i++) {
        try { $status = Invoke-Api -Path "/api/ai/knowledge/status" -Token $adminToken } catch { }
        if ($status -and $status.chunks -gt 0) { break }
        if ($i -eq 0) { Write-Host "  Esperando indexación RAG (el primer arranque descarga el modelo de embeddings)..." -ForegroundColor Yellow }
        Start-Sleep -Seconds 10
    }
    if (-not $status -or $status.chunks -eq 0) {
        Write-Fail "La base de conocimiento no está indexada. Revisa: docker compose logs ollama-init ai-service"
    } else {
        Write-Ok "Base indexada: $($status.chunks) chunks · chat=$($status.chatModel)@$($status.chatProvider) · embeddings=$($status.embeddingModel)@$($status.embeddingProvider)"
        foreach ($q in @("¿Cuántos intentos fallidos se permiten antes de bloquear una cuenta?", "¿Cuántos usuarios activos hay?")) {
            try {
                $answer = Invoke-Api -Method POST -Path "/api/ai/ask" -Body @{ question = $q; useCache = $false } -Token $adminToken -TimeoutSec 300
                Write-Ok "P: $q"
                Write-Host "     R: $($answer.answer)"
                Write-Host ("     modelo={0} latencia={1}ms tokens={2}/{3} (cache hit {4}) costo=`${5} calidad={6} tools=[{7}]" -f `
                    $answer.metrics.model, $answer.metrics.latencyMs, $answer.metrics.promptTokens, $answer.metrics.completionTokens, `
                    $answer.metrics.cachedPromptTokens, $answer.metrics.estimatedCostUsd, $answer.quality.score, ($answer.toolsUsed -join ","))
            } catch {
                $code = Get-StatusCode $_
                Write-Fail "Consulta IA falló ($code): $($_.Exception.Message)"
                if ($code -eq 503) { Write-Host "     Pista: revisa LLM_API_KEY y el saldo de DeepSeek en .env; luego: docker compose up -d ai-service" -ForegroundColor Yellow }
            }
        }
        $metrics = Invoke-Api -Path "/api/ai/metrics/summary" -Token $adminToken
        Write-Ok "Métricas agregadas: $($metrics.queries) consultas · p95=$($metrics.p95LatencyMs)ms · tokens/consulta=$($metrics.avgTokensPerQuery)"
    }
}

# ---------------------------------------------------------------------------------
Write-Step "9. Logout y revocación distribuida"
Invoke-Api -Method POST -Path "/api/auth/logout" -Body @{ refreshToken = $admin.refreshToken } -Token $adminToken | Out-Null
try {
    Invoke-Api -Path "/api/users/me" -Token $adminToken | Out-Null
    Write-Fail "El token revocado sigue siendo aceptado"
} catch { if ((Get-StatusCode $_) -eq 401) { Write-Ok "Token revocado rechazado por User Service (401)" } else { Write-Fail "Código inesperado" } }

Write-Host ""
if ($script:failures -eq 0) { Write-Host "PRUEBA DE HUMO EXITOSA" -ForegroundColor Green; exit 0 }
Write-Host "PRUEBA DE HUMO CON $($script:failures) FALLO(S)" -ForegroundColor Red
exit 1
