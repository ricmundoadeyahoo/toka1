# Política de contraseñas y sesiones

## Requisitos de contraseña
Las contraseñas deben tener entre 8 y 128 caracteres e incluir al menos una letra mayúscula, una letra minúscula, un dígito y un carácter especial. Se recomienda usar frases de contraseña largas y un gestor de contraseñas. Las contraseñas se almacenan con BCrypt; nadie, ni siquiera un administrador, puede ver la contraseña de otro usuario.

## Bloqueo por intentos fallidos
Después de 5 intentos fallidos de inicio de sesión, la cuenta se bloquea temporalmente durante 15 minutos. Cada intento fallido se registra en la auditoría con el evento identity.login.failed y el bloqueo con identity.account.locked. Si tu cuenta está bloqueada, espera 15 minutos o contacta a un administrador.

## Duración de las sesiones
El token de acceso (JWT) dura 15 minutos. La aplicación lo renueva automáticamente con un refresh token que dura 7 días y es de un solo uso (rotación). Al cerrar sesión, el token de acceso se revoca de inmediato en todos los microservicios y el refresh token se invalida.

## Recuperación de contraseña
Actualmente la recuperación de contraseña se realiza con apoyo de un administrador a través de la mesa de ayuda. Nunca compartas tu contraseña por correo, chat o teléfono; el equipo de soporte jamás te la pedirá.

## Límite de intentos por red
Además del bloqueo por cuenta, el sistema limita a 10 solicitudes por minuto por dirección IP los endpoints de inicio de sesión, registro y renovación de tokens, para mitigar ataques de fuerza bruta.
