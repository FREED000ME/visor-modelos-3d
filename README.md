# Armado Paso a Paso (APK)

App Android que convierte fotos de un manual de armado en una animación 3D paso a paso.

- `app/src/main/assets/index.html`: la app completa (WebGL propio, sin dependencias).
- `MainActivity.java`: WebView con cámara y galería nativas, guardado en Descargas, voz (TextToSpeech) y llamadas a la API de Claude.
- Cada push a la rama `armado-apk` compila el APK en GitHub Actions y lo publica como Release `armado-v1.0.N`.

La interpretación con IA usa la clave de API de Claude del usuario (console.anthropic.com), guardada solo en el teléfono.
