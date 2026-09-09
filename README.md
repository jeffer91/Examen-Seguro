# ITSQMET Examen Seguro — Administrador

Rama `admin` del sistema de supervisión de Exámenes Complexivos del ITSQMET.

Incluye:
- API ASP.NET Core + SignalR.
- Panel web React para Administrador.
- Configuración de exámenes, estudiantes, equipos, asignaciones y reglas.
- Consulta de sesiones, incidencias y capturas.
- Persistencia central en Neon/PostgreSQL.

## Seguridad y alcance
El sistema supervisa únicamente sesiones de examen previamente informadas. No incorpora keylogging ni lectura del contenido de conversaciones privadas. La navegación se registra mediante el complemento de navegador del cliente y las capturas se generan únicamente ante incidencias configuradas.

## Variables del servidor
- `DATABASE_URL`: cadena PostgreSQL de Neon.
- `ADMIN_API_KEY`: clave del panel Administrador.
- `VIEWER_API_KEY`: clave del panel Veedor.
- `AGENT_API_KEY`: clave de los clientes instalados.

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
