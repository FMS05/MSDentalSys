# Calendario interactivo — FASE 1

Implementación de consulta sobre las citas existentes, continuada desde los
cambios parciales del repositorio. No incluye Cubículos ni fases 2/3.

## Auditoría del trabajo interrumpido

El estado inicial tenía cambios en `CitasController.cs` e `Index.cshtml`, además
de un DTO, el script y 57 casos de integración sin seguimiento de Git. Se
conservaron el endpoint y el DTO tras revisar permisos, proyección, filtros,
intersección de intervalos, precisión y duración histórica.

Se completó la distribución local de FullCalendar y el CSS delimitado. Se
corrigió el script para que Filtrar conserve el mes/semana alcanzado con los
botones de navegación, se detecten respuestas HTML/redirecciones de sesión y
los enlaces de eventos permanezcan en el mismo origen. La vista carga
FullCalendar solamente en modo Calendario y explica la diferencia entre el
campo Fecha en Calendario y en Lista. Se ampliaron las pruebas HTTP a 83 casos.

## Comportamiento

- `Citas/Index` abre Calendario; `?vista=lista` muestra la lista existente.
  Los enlaces conservan fecha, estado, paciente y odontólogo.
- Mes y Semana en español, anterior/siguiente/Hoy y navegación a Details.
- Estado, paciente y odontólogo se combinan. Fecha sitúa el calendario en su
  período; en Lista conserva el filtro de inicio por día existente.
- Los cinco estados son visibles. Cancelada lleva la indicación «No ocupa
  agenda». Los espacios vacíos no se presentan como garantía de disponibilidad.
- Pantalla de carga, mensaje vacío, error y Reintentar. El contenido de los
  eventos se crea como texto, nunca interpolando nombres en HTML.
- Solo consulta: sin selección de franjas, arrastre, recepción de elementos
  externos ni cambio de duración. Crear y Reagendar mantienen sus flujos.

## Contrato HTTP

`GET /Citas/Calendario?start=2030-02-01&end=2030-03-01`

| Parámetro | Obligatorio | Semántica |
|---|---|---|
| `start` | Sí | Inicio inclusivo del intervalo visible, ISO 8601 |
| `end` | Sí | Final exclusivo; posterior a start; máximo 62 días |
| `estado` | No | Pendiente, Confirmada, Atendida, Cancelada o No asistió |
| `pacienteId` | No | Identificador entero del paciente, incluido histórico inactivo |
| `odontologoId` | No | Identificador del profesional para roles administrativos |

Respuesta 200: arreglo JSON (vacío si no hay coincidencias), con DTO específico:

```json
[
  {
    "id": 123,
    "title": "Ana Ejemplo · Consulta",
    "start": "2030-02-01T10:00:00",
    "end": "2030-02-01T10:45:00",
    "duracionConocida": true,
    "estado": "Confirmada",
    "odontologoId": "doctor-id",
    "odontologoNombre": "Doctor Ejemplo",
    "url": "/Citas/Details/123"
  }
]
```

No se serializan entidades EF, observaciones, datos clínicos, cédulas,
teléfonos, direcciones, correos ni propiedades internas de Identity. Se
envía `Cache-Control: no-store, no-cache`. POST no es admitido (405).

Fechas ausentes/inválidas, un intervalo invertido/vacío/superior a 62 días o
un `pacienteId` no convertible a entero devuelven 400. Un estado desconocido
se ignora, igual que en el listado existente. IDs de paciente/profesional
sin coincidencias producen un arreglo vacío. El error de validación usa
`{ "error": "..." }`.

## Autorización

La acción hereda `[Authorize(Roles = "Administrador,Odontologo,Recepcionista")]`.
Administrador y Recepcionista pueden consultar todos los profesionales o
filtrar uno. El selector incluye odontólogos activos con rol y profesionales
con citas históricas, aunque estén inactivos o ya no tengan ese rol.

Odontologo se restringe en servidor al `NameIdentifier` autenticado e ignora
el `odontologoId` solicitado. Esta restricción prevalece incluso si también
tiene rol Administrador; no se le muestra el selector. Sin identificador no
obtiene citas ajenas. Details conserva su autorización existente.

Una petición anónima sigue el desafío de autenticación de la aplicación
(redirección al login); los roles no admitidos se rechazan. El script trata
redirecciones, respuestas no JSON y fallos HTTP como errores, con reintento.

## Duración e intervalos

Para una duración conocida, `end = FechaHoraInicio + DuracionProgramadaMinutos`.
La lectura utiliza el snapshot de la cita, aunque cambie la duración del
servicio o subservicio. No se completa ni modifica información histórica.

Una cita con duración nula devuelve `end: null` y `duracionConocida: false`.
Se representa con un bloque visual de 15 minutos, borde discontinuo y
«Duración no registrada»; ese bloque no es su duración clínica. No se fuerza
un end en el cliente ni se utiliza ese tamaño para disponibilidad o conflictos.

Para duración conocida se usa intersección: inicio de cita menor que end y
fin de cita mayor que start. Con duración nula solo se incluye si su inicio
está en `[start,end)`. Así se incluyen citas que atraviesan medianoche, pero
se excluyen las que terminan exactamente en start o comienzan exactamente en end.

La consulta SQL proyecta solo los campos necesarios desde un día antes de
start hasta end; el CHECK existente limita la duración a 1440 minutos. La
intersección final se calcula sobre esa proyección acotada, compatible con
SQLite y SQL Server, sin cambiar esquema. La guarda de DateTime evita
desbordamientos para datos históricos extremos cuyo final no es representable.

## Fechas

El dominio guarda horas civiles (`DateTime`), sin zona horaria. Se preservan
sus componentes y hasta siete decimales de segundo; no se convierten a UTC.
Se aceptan fechas `yyyy-MM-dd` y fechas/hora ISO con minutos, segundos y
fracciones opcionales. Si el cliente envía `Z` u offset válido, se conservan
los componentes civiles expresados, sin desplazar horas.

El script usa `timeZone: 'local'` para presentar esos componentes y envía
fecha/hora civil sin sufijo; nunca usa `toISOString()`. El JSON también
devuelve las horas sin `Z` ni offset. El intervalo máximo permite las seis
semanas de una cuadrícula mensual. No se incorpora soporte de zonas por clínica.

## FullCalendar y archivos

FullCalendar **6.1.19**, paquetes oficiales npm core/daygrid/timegrid y locale
español. Los tarballs se comprobaron contra el SHA-512 publicado por npm.
Distribución MIT con avisos de FullCalendar y Preact conservados. La carpeta
local incluye manifiesto de procedencia, hashes SHA-256 y el inventario exacto
en [README del proveedor](../src/MSDentalSys.Web/wwwroot/lib/fullcalendar/README.md).
No depende de CDN en ejecución y no incluye plugins Premium ni interaction.

Archivos modificados: `src/MSDentalSys.Web/Controllers/CitasController.cs` y
`src/MSDentalSys.Web/Views/Citas/Index.cshtml`.

Archivos nuevos:

- `src/MSDentalSys.Web/Models/ViewModels/CitaCalendarioEventoViewModel.cs`.
- `src/MSDentalSys.Web/wwwroot/js/citas-calendario.js`.
- `src/MSDentalSys.Web/wwwroot/css/citas-calendario.css`, con selectores propios
  prefijados por `.citas-calendar-page`.
- `src/MSDentalSys.Web/wwwroot/lib/fullcalendar/` (inventario enlazado arriba).
- `tests/MSDentalSys.Tests/Integration/CitasCalendarioIntegrationTests.cs`.
- `tests/client/citas-calendario.test.cjs`.
- Este documento.

No se modifican Cita, ApplicationDbContext, migraciones, esquema,
HasScheduleConflictAsync, creación, reagendamiento, concurrencia,
Citas/Create.cshtml, Citas/Reschedule.cshtml ni el formulario de Pacientes.
Se comprobó que desde Details hasta el final del controller el contenido
permanece idéntico a HEAD.

## Validación del 2026-10-16

- `dotnet build MSDentalSys.sln --no-restore`: correcto, 0 errores y 0 advertencias.
- `dotnet test MSDentalSys.sln --no-build --logger "trx;LogFileName=calendario-fase1-regresion.trx" --results-directory TestResults/calendario-fase1 --logger "console;verbosity=minimal"`:
  **625 aprobadas, 0 fallidas, 1 omitida; 626 total**.
- Los 83 casos de integración del calendario están incluidos en las 625
  aprobadas. La ejecución focalizada también dio 83/0/0.
- Cliente JavaScript: **12 aprobadas, 0 fallidas, 0 omitidas**, ejecutadas con
  `node:test.run` sobre el archivo final. Reproducible con
  `node --test tests/client/citas-calendario.test.cjs`, sin paquetes npm.
  Son adicionales a la suite dotnet; usan un doble del DOM y de FullCalendar,
  no sustituyen la comprobación visual en un navegador real.
- `MSDENTALSYS_H8_SQLSERVER` **no está configurado**. No se ejecutaron los
  escenarios H8 de SQL Server; xUnit registra su teoría como una prueba omitida,
  no como aprobada. Las pruebas relacionales estándar usan SQLite en memoria.
- Se necesitó ejecutar las pruebas fuera del sandbox por acceso denegado a
  Windows EventLog; no fue un fallo de las aserciones funcionales.
- TRX local: `TestResults/calendario-fase1/calendario-fase1-regresion.trx` (ignorado por Git).

Los tests HTTP cubren roles, filtros combinados, historial, contrato sin datos
adicionales, snapshot, duración nula sin escritura, límites/exclusividad,
medianoche, formatos/offsets/precisión, errores, GET/caché, alternancia,
Details, escape de nombres y entrega HTTP de recursos locales versionados.

Los tests del cliente comprueban parámetros civiles, filtros, alternancia,
preservación del período al filtrar, duración nula, texto seguro, URLs,
Details, errores de sesión, librería ausente, reintento y respuestas obsoletas.

## Revisión final de Git

`git diff --check` sin errores. `git diff --stat` (archivos ya rastreados):

```text
 src/MSDentalSys.Web/Controllers/CitasController.cs | 119 ++++++++++++++++++++-
 src/MSDentalSys.Web/Views/Citas/Index.cshtml       |  83 ++++++++++++--
 2 files changed, 190 insertions(+), 12 deletions(-)
```

`git status` muestra esos dos archivos modificados y **18 archivos nuevos**
sin seguimiento, incluidos los doce archivos de la distribución local.
Los archivos nuevos no forman parte de `git diff --stat` hasta incorporarse
al índice; se revisaron también su contenido, licencias y hashes. No se
hicieron commits ni cambios de staging. No hay cambios fuera del alcance.

## Comprobaciones manuales pendientes

1. Con Administrador y Recepcionista, abrir Citas: deben aparecer calendario
   en español y selector de odontólogo, con historial inactivo seleccionable.
2. Alternar Mes/Semana, usar anterior/siguiente/Hoy y aplicar filtros: el
   período navegado se conserva cuando no se cambia Fecha.
3. Combinar estado, paciente y odontólogo; alternar Lista/Calendario y
   comprobar filtros, nombres y resultados. Fecha navega en Calendario y
   filtra por día en Lista.
4. Con Odontologo, verificar que solo aparecen sus citas, no hay selector y
   cambiar manualmente `odontologoId` no permite ver citas ajenas.
5. Abrir una cita para ir a Details. Intentar arrastrar/redimensionar:
   no debe haber edición ni solicitudes de escritura.
6. Comprobar una cita con snapshot y otra histórica nula: esta última debe
   indicar duración no registrada; revisar también Cancelada y medianoche.
7. Revisar legibilidad en escritorio y móvil, teclado/foco, citas simultáneas
   y el desplegable de más eventos en mes.
8. En herramientas de red, confirmar recursos FullCalendar locales y GET con
   start/end civiles; simular error de red/sesión y comprobar Reintentar.

Limitaciones: pendiente validación visual con sesiones reales del navegador
y ejecución H8 contra SQL Server de pruebas. El calendario es una consulta
de citas existentes; no determina disponibilidad y no escribe cambios.
