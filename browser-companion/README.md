# Complementos de navegador

El agente nativo puede detectar qué aplicación está en primer plano, pero conocer la URL de Chrome, Edge, Brave o Firefox de forma estable requiere un complemento del navegador.

Los complementos de esta carpeta:
- escuchan solo navegaciones de nivel superior;
- eliminan `query string` y fragmentos antes de enviar la URL;
- no leen el DOM, formularios, portapapeles, contraseñas ni pulsaciones de teclado;
- envían eventos únicamente al agente local en `127.0.0.1:43119`;
- el agente descarta el evento cuando no existe una sesión activa.

En equipos administrados por ITSQMET deben desplegarse mediante políticas empresariales del navegador. En equipos externos requieren instalación/autorización explícita.
