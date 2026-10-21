# Política de control de accesos

## Propósito
Esta política define cómo se otorgan, revisan y revocan los accesos al sistema de gestión de usuarios de Toka. Aplica a colaboradores, contratistas y cuentas de servicio.

## Principio de mínimo privilegio
Cada persona recibe únicamente los permisos necesarios para su función. Los permisos se agrupan en roles. Ningún usuario debe tener el rol Admin "por si acaso"; las asignaciones de Admin se revisan cada trimestre.

## Roles del sistema
- **Admin**: acceso total. Puede gestionar usuarios, roles, permisos, consultar la auditoría y administrar el asistente de IA (reindexar conocimiento, ver métricas y ejecutar evaluaciones).
- **Auditor**: acceso de solo lectura a usuarios, roles y bitácora de auditoría. Puede usar el asistente de IA. No puede modificar datos.
- **User**: rol por defecto que se asigna automáticamente al registrarse. Puede consultar y editar su propio perfil y usar el asistente de IA.

Los roles de sistema no pueden eliminarse. Siempre debe existir al menos un administrador: el sistema impide revocar el rol Admin al último administrador.

## Permisos
Los permisos tienen el formato recurso:acción. Los disponibles son users:read, users:write, roles:read, roles:manage, audit:read, ai:use y ai:admin. Un administrador puede crear roles personalizados combinando estos permisos.

## Solicitud de accesos
Para solicitar un rol adicional, el colaborador abre un ticket en la mesa de ayuda indicando el rol y la justificación de negocio. Su jefe directo aprueba y un administrador realiza la asignación desde la pantalla de Roles. Toda asignación y revocación queda registrada en la auditoría con el evento roles.assignment.granted o roles.assignment.revoked.

## Revocación
Cuando un colaborador deja la empresa, Recursos Humanos notifica a un administrador, quien desactiva la cuenta desde la pantalla de Usuarios. La desactivación bloquea de inmediato nuevos inicios de sesión; los tokens ya emitidos expiran en un máximo de 15 minutos.
