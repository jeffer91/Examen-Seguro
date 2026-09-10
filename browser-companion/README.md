# Complementos de navegador

Durante una sesión de examen previamente informada, el cliente puede obtener navegación de dos formas:

1. **Complemento del navegador**: entrega el evento de navegación al agente local en tiempo casi real.
2. **Lectura local del historial creado desde el inicio de la sesión**: sirve como respaldo para Chrome, Edge, Brave y Firefox cuando el complemento no está disponible. No consulta visitas anteriores al inicio del examen.

Los complementos de esta carpeta:
- escuchan solo navegaciones de nivel superior;
- eliminan `query string` y fragmentos antes de enviar la URL;
- no leen el DOM, formularios, portapapeles, contraseñas ni pulsaciones de teclado;
- envían eventos únicamente al agente local en `127.0.0.1:43119`;
- usan una clave local aleatoria distinta de la clave del servidor;
- el agente valida tanto el origen de extensión como la clave local;
- el agente descarta el evento cuando no existe una sesión activa.

El instalador genera `bridge-config.js` con una clave propia de cada equipo. Los archivos del repositorio llevan la clave vacía y no deben distribuirse como configuración final.

En equipos administrados por ITSQMET, el complemento debe desplegarse mediante políticas empresariales/administradas del navegador. En equipos externos requiere instalación y autorización explícitas. El sistema no debe instalar extensiones de forma encubierta.
