# ITSQMET Examen Seguro — Veedor

Rama `veedor` para supervisión en tiempo real.

El veedor puede:
- seleccionar un examen activo;
- ver estudiantes conectados y último heartbeat;
- recibir incidencias en vivo;
- revisar aplicación, dominio/URL y severidad;
- abrir capturas asociadas a incidencias.

No modifica reglas ni configuraciones.

## Variables
- `VITE_API_URL`: URL pública del backend.

La clave del veedor se introduce al iniciar sesión y se guarda solo en `sessionStorage`.
