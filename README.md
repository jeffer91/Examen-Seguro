# ITSQMET Examen Seguro — Cliente Windows

Rama `cliente` del agente que se instala en los equipos donde se rinde el Examen Complexivo.

## Componentes
- `src/Itsqmet.ExamAgent`: agente en la sesión del usuario. Registra cambios de aplicación, recibe navegación de los complementos de navegador y toma capturas únicamente cuando una regla configurada genera una incidencia.
- `src/Itsqmet.ExamService`: servicio Windows de supervisión del estado del agente.
- `browser-companion`: complemento para Chromium (Chrome/Edge/Brave) y Firefox. Registra navegación durante la sesión activa sin leer el contenido de formularios ni pulsaciones de teclado.
- `installer`: scripts de instalación/desinstalación para laboratorio.

## Inicio de la supervisión
El software puede estar instalado y permanecer inactivo. Solo comienza a registrar una sesión cuando el Administrador arma una asignación que tenga consentimiento informado registrado. No existe keylogger ni captura continua de pantalla.

## Configuración
`C:\ProgramData\ITSQMET\ExamenSeguro\client.json`

```json
{
  "serverUrl": "https://servidor.example/",
  "agentKey": "CLAVE_DE_AGENTE",
  "deviceCode": "GUID-DEL-EQUIPO"
}
```

## Compilación
La acción `Build Windows Client` genera un artefacto para Windows x64.
