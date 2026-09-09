# ITSQMET Examen Seguro — Administrador

Rama `admin` del sistema de supervisión de Exámenes Complexivos del ITSQMET.

## Incluye
- API ASP.NET Core + SignalR.
- Panel web React para Administrador.
- Configuración de exámenes, estudiantes, equipos, asignaciones y reglas.
- Inicio y finalización individual de la supervisión.
- Cierre global de un examen, finalizando sesiones activas.
- Consulta de sesiones, incidencias, navegación, carpetas y capturas.
- Persistencia central en Neon/PostgreSQL.
- Esquema reproducible en `database/schema.sql`.

## Seguridad y alcance
El sistema supervisa únicamente sesiones previamente informadas. No incorpora keylogging ni lectura del contenido de formularios o conversaciones privadas. Las capturas se generan únicamente cuando una regla configura una incidencia.

El canal SignalR de supervisión solo admite roles `admin` y `veedor`. Las claves del servidor son obligatorias: no existen claves de desarrollo predeterminadas en ejecución.

## Variables del servidor
- `DATABASE_URL`: cadena PostgreSQL de Neon.
- `ADMIN_API_KEY`: clave del panel Administrador.
- `VIEWER_API_KEY`: clave del panel Veedor.
- `AGENT_API_KEY`: clave de los clientes instalados.
- `ALLOWED_ORIGINS`: orígenes web permitidos, separados por coma.

## Desarrollo
Servidor:
```bash
cd server
dotnet run
```

Panel:
```bash
cd web
npm install
npm run dev
```

Para producción configure valores aleatorios distintos para las tres claves y limite `ALLOWED_ORIGINS` exclusivamente a los dominios publicados de Administrador y Veedor.
