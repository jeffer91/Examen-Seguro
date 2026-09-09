# Examen Seguro ITSQMET — Administrador

Rama de administración del sistema de supervisión de Exámenes Complexivos de ITSQMET.

## Objetivo

El Administrador configura exámenes, usuarios, reglas de incidencias, aplicaciones/sitios que generan alerta, dispositivos y acceso de veedores. También consulta historial y reportes.

## Alcance inicial

- Dashboard general.
- Gestión de exámenes.
- Gestión de reglas de incidencias.
- Gestión de veedores.
- Consulta de estudiantes/dispositivos.
- Historial de alertas y evidencias.
- Configuración de conexión con Neon y API.

## Seguridad

No incluir claves reales en el repositorio. Las variables sensibles deben configurarse mediante variables de entorno.

## Stack

- React + TypeScript + Vite.
- Backend/API separado por configurar.
- Neon/PostgreSQL como base de datos.
