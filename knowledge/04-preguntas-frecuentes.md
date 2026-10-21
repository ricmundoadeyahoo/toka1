# Preguntas frecuentes

## ¿Por qué no veo la pantalla de Usuarios?
Porque tu rol no incluye el permiso users:read. Solicita el rol Auditor o uno personalizado mediante la mesa de ayuda.

## Me asignaron un rol nuevo pero no veo los cambios
Los permisos viajan dentro del token de acceso. Cierra sesión y vuelve a entrar, o espera a que el token se renueve (máximo 15 minutos).

## ¿Qué significa que mi cuenta esté desactivada?
Un administrador desactivó tu cuenta, normalmente por baja o por seguridad. No podrás iniciar sesión hasta que un administrador la reactive.

## ¿El asistente de IA puede ver mi contraseña?
No. Las contraseñas solo existen como hash BCrypt en Auth Service y el asistente no tiene acceso a ese servicio. Además, el asistente solo puede consultar la información que tus propios permisos te permiten ver.

## ¿Dónde se ejecuta el modelo de IA?
El modelo de lenguaje es DeepSeek, un servicio en la nube al que se accede por una API compatible con OpenAI. Los documentos de la base de conocimiento se convierten en vectores (embeddings) con un modelo local, dentro de la infraestructura de la empresa. Cada respuesta muestra los tokens consumidos y su costo estimado en dólares; las preguntas repetidas sobre documentación se responden desde caché sin costo. No escribas contraseñas ni datos sensibles en el asistente: la pregunta se envía al proveedor del modelo.

## ¿Qué hago si el asistente responde lento?
La latencia depende del proveedor del modelo y de cuántas herramientas necesite consultar el agente. Las preguntas repetidas sobre documentación se responden desde caché. Si la lentitud persiste, reporta el Correlation ID al equipo técnico.
