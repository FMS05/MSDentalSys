# FullCalendar local — FASE 1

Versión fijada: **6.1.19**, distribución estándar, licencia **MIT**.
Incorporada el 2026-10-16 desde los paquetes oficiales del registro npm.

## Procedencia y verificación

- Repositorio: https://github.com/fullcalendar/fullcalendar/tree/v6.1.19
- Documentación de distribución global v6: https://legacy.fullcalendar.io/v6/initialize-globals
- Licencias: https://fullcalendar.io/license
- `packages.json` registra nombre, versión, URL exacta del tarball, integridad
  SHA-512 publicada por npm y dependencias de cada paquete.
- Antes de extraer cada tarball se calculó su SHA-512 y se comprobó que
  coincidiera con `dist.integrity` del registro oficial. Los archivos se
  copiaron sin modificaciones. `SHA256SUMS` permite comprobar las copias locales.

| Paquete | Tarball oficial |
|---|---|
| `@fullcalendar/core@6.1.19` | https://registry.npmjs.org/@fullcalendar/core/-/core-6.1.19.tgz |
| `@fullcalendar/daygrid@6.1.19` | https://registry.npmjs.org/@fullcalendar/daygrid/-/daygrid-6.1.19.tgz |
| `@fullcalendar/timegrid@6.1.19` | https://registry.npmjs.org/@fullcalendar/timegrid/-/timegrid-6.1.19.tgz |

## Archivos incorporados

- `core/index.global.min.js`: núcleo y dependencias incluidas en el bundle oficial.
- `core/locales/es.global.min.js`: idioma español oficial.
- `daygrid/index.global.min.js`: vista mensual.
- `timegrid/index.global.min.js`: vista semanal por horas.
- `core/LICENSE.md`, `daygrid/LICENSE.md`, `timegrid/LICENSE.md`: avisos MIT originales.
- `core/PREACT-LICENSE.txt`: aviso MIT de Preact, dependencia incorporada en el
  bundle core. Procedencia: https://raw.githubusercontent.com/preactjs/preact/10.12.1/LICENSE
  (core declara `preact: ~10.12.1`).
- `packages.json`, `SHA256SUMS` y este documento: trazabilidad de la distribución.
- `.gitattributes`: conserva los bytes originales incluso con `core.autocrlf`
  habilitado, para que los hashes sigan siendo reproducibles tras un checkout.

Se cargan core, daygrid, timegrid y español, en ese orden, antes de
`wwwroot/js/citas-calendario.js`. Los bundles v6 incluyen los estilos de
FullCalendar y los insertan en la página; no requieren un CSS de proveedor
separado. Los ajustes propios están en `wwwroot/css/citas-calendario.css`.

No se cargan recursos desde un CDN en tiempo de ejecución. No se incorporan
plugins Premium, recursos/cubículos ni el plugin de interacción. La
configuración también desactiva explícitamente edición, arrastre, selección
y cambio de duración. La alternancia Lista usa la vista MVC existente.

Para verificar o actualizar, descargar cada versión explícita desde npm,
comparar el SHA-512 del tarball con `dist.integrity`, extraer únicamente los
archivos enumerados, conservar avisos y actualizar este documento, el
manifiesto y los SHA-256. No usar etiquetas flotantes `latest` ni actualizar
los componentes de forma independiente.
