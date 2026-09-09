# Examen Seguro ITSQMET — Cliente

Rama destinada a la aplicación que se instala en los equipos usados durante los Exámenes Complexivos de ITSQMET.

## Objetivo

Conectar el equipo del examen con el servidor institucional durante una sesión previamente informada al estudiante.

## Alcance inicial

- Windows 10/11 de 64 bits.
- Registro del dispositivo.
- Inicio y finalización de sesión.
- Heartbeat con el servidor.
- Registro de incidencias técnicas durante la sesión.
- Sincronización diferida cuando se pierde temporalmente la conexión.

## Privacidad

La aplicación se limita a la sesión de examen y no recopila actividad anterior o posterior a ella.

## Stack previsto

- C# / .NET 10.
- Servicio de Windows y componente de sesión.
- SQLite local para cola temporal.
- HTTPS/SignalR con el servidor.
- Neon/PostgreSQL en el backend central.
