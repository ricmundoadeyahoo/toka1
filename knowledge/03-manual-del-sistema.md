# Manual del sistema de gestión de usuarios

## Registro de una cuenta
Desde la pantalla de inicio de sesión, elige "Crear cuenta" e ingresa tu nombre completo, email, departamento (opcional) y una contraseña que cumpla la política. Al registrarte se crea tu credencial en Auth Service; en segundos, de forma asíncrona, User Service crea tu perfil y Role Service te asigna el rol User.

## Pantalla de Usuarios
Los usuarios con permiso users:read ven el listado con búsqueda por nombre o email y filtro por estado (Active o Deactivated). Los administradores (users:write) pueden editar nombre, departamento y puesto, así como desactivar o reactivar cuentas. Nadie puede desactivar su propia cuenta.

## Pantalla de Roles
Muestra los roles existentes con sus permisos. Un administrador puede crear roles personalizados, cambiar sus permisos (excepto los del rol Admin), eliminar roles que no tengan usuarios asignados, y asignar o revocar roles a un usuario indicando su id. Los cambios de rol se reflejan en el siguiente token del usuario (como máximo 15 minutos, o al volver a iniciar sesión).

## Pantalla de Auditoría
Disponible para quienes tienen audit:read. Muestra cada evento del sistema: inicios de sesión exitosos y fallidos, registros, cambios de perfil, desactivaciones, asignaciones de roles y consultas al asistente de IA. Se puede filtrar por tipo de evento, por resultado (éxito o fallo) y por usuario. Los registros se conservan 365 días.

## Asistente de IA
El asistente responde preguntas sobre las políticas y el manual del sistema usando búsqueda semántica (RAG) y puede consultar datos vivos —usuarios, roles y auditoría— respetando los permisos de quien pregunta. Cada respuesta muestra las fuentes citadas, la latencia, los tokens consumidos, el costo estimado y una calificación automática de calidad. El límite es de 10 consultas por minuto por usuario.

## Soporte
Para incidencias abre un ticket en la mesa de ayuda (extensión 4040) indicando el Correlation ID que aparece en los mensajes de error; permite al equipo técnico rastrear la petición en los logs centralizados.
