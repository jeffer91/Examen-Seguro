# Examen Seguro ITSQMET — Veedor

Rama destinada al panel de veeduría de los Exámenes Complexivos de ITSQMET.

## Objetivo

El Veedor consulta únicamente la jornada asignada: estudiantes conectados, estado de cada sesión, incidencias, capturas generadas durante el examen e historial cronológico. No modifica reglas globales ni usuarios.

## Alcance inicial

- Vista de examen en vivo.
- Lista de estudiantes y dispositivos.
- Estados: conectado, sin señal, finalizado.
- Alertas por incidencias.
- Consulta de capturas asociadas a incidencias.
- Historial por estudiante.
- Filtros por estado y nivel de alerta.

## Seguridad y privacidad

La supervisión se limita a la sesión de examen previamente informada. El panel no está diseñado para acceder a actividad anterior o posterior al examen.

## Stack previsto

- React + TypeScript.
- Consumo de API del sistema.
- Actualizaciones en tiempo real mediante SignalR/WebSocket.
