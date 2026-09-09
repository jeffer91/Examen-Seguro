# ITSQMET Examen Seguro — Cliente Windows

Rama `cliente` del agente que se instala en equipos Windows 10/11 donde se rinde el Examen Complexivo.

## Componentes
- `src/Itsqmet.ExamAgent`: agente de la sesión del usuario. Registra cambios de aplicación, navegación ocurrida durante una sesión activa, carpetas abiertas y genera capturas únicamente cuando una regla configura una incidencia.
- `src/Itsqmet.ExamService`: servicio Windows que vigila al agente, reporta su cierre e intenta volver a iniciarlo.
- `browser-companion`: complemento opcional para Chromium (Chrome/Edge/Brave) y Firefox. Aporta eventos de navegación en tiempo real.
- `installer`: scripts de instalación/desinstalación para laboratorio.

## Navegación
El agente dispone de dos fuentes complementarias:
1. Complemento del navegador, cuando está instalado.
2. Lectura temporal de las bases locales de historial de Chrome, Edge, Brave y Firefox, limitada a visitas posteriores al inicio de la sesión de examen.

Los parámetros de consulta y fragmentos de las URL no se envían al servidor. El sistema no registra teclas, contraseñas ni contenido de formularios. La navegación privada/incógnita puede no quedar disponible en el historial del navegador; la apertura y cambio hacia el proceso del navegador continúa registrándose.

## Evidencias y conexión
- Capturas únicamente ante incidencias configuradas.
- Heartbeat cada 5 segundos durante una sesión activa.
- Cola local de eventos si el servidor o Internet no responden.
- Reenvío automático de la cola cuando se recupera la conexión.
- Detección del cierre del agente y del servicio durante una sesión activa.

## Inicio de supervisión
El software puede permanecer instalado e inactivo. Solo comienza a registrar cuando el Administrador habilita una asignación cuya supervisión haya sido previamente informada y registrada.

## Configuración
`C:\ProgramData\ITSQMET\ExamenSeguro\client.json`

```json
{
  "serverUrl": "https://servidor.example/",
  "agentKey": "CLAVE_DE_AGENTE",
  "deviceCode": "GUID-DEL-EQUIPO",
  "pollSeconds": 3
}
```

## Compilación
La acción `Build Windows Client` genera el artefacto para Windows x64 con Agent, Service, instalador y complementos de navegador.
