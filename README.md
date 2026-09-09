# ITSQMET Examen Seguro

Sistema institucional para supervisar sesiones de Examen Complexivo en equipos Windows.

## Ramas principales

- `admin`: API central y panel del Administrador. Configura exámenes, estudiantes, equipos, asignaciones y reglas de incidencia.
- `veedor`: panel de supervisión en tiempo real para veedores, con estado de estudiantes, alertas e imágenes de evidencia.
- `cliente`: agente Windows, servicio de vigilancia del agente, complementos de Chrome/Edge/Brave/Firefox y paquete de instalación.

## Arquitectura

```text
Cliente Windows
      |
      | HTTPS + heartbeat + eventos
      v
API ASP.NET Core + SignalR
      |
      +---- Neon / PostgreSQL
      |
      +---- Panel Administrador
      |
      +---- Panel Veedor en tiempo real
```

## Principios de funcionamiento

- No bloquea páginas o aplicaciones en el MVP: registra y genera incidencias según reglas.
- Las capturas se toman únicamente ante incidencias configuradas.
- La navegación se registra durante una sesión activa mediante complementos de navegador; se eliminan query strings y fragmentos de la URL.
- No se registran pulsaciones de teclado, contraseñas ni contenido de formularios.
- La supervisión solo puede iniciar para una asignación armada que tenga constancia de información/consentimiento institucional registrada.
- El cliente genera heartbeat y el servicio Windows reporta si el agente deja de ejecutarse durante una sesión.

## Estado

MVP técnico creado para Windows 10/11 x64. La compilación del cliente Windows y las compilaciones de los paneles se validan con GitHub Actions.
