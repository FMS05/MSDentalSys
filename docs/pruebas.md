# Estrategia de pruebas automatizadas

## Tecnologías y aislamiento

Las pruebas utilizan xUnit sobre .NET 9. Las pruebas que requieren persistencia utilizan SQLite InMemory con un `ApplicationDbContext` aislado por escenario. La conexión se mantiene abierta durante cada prueba y se libera al finalizar.

Las pruebas de integración HTTP utilizan `Microsoft.AspNetCore.Mvc.Testing` mediante `WebApplicationFactory`. La factory fuerza el entorno `Testing`, reemplaza SQL Server por SQLite InMemory y crea el esquema con `EnsureCreated`.

El entorno `Testing` evita la ejecución de `RoleSeeder` y `AdminSeeder` de producción. La autenticación HTTP se simula exclusivamente en Tests mediante claims con `NameIdentifier`, `Name` y `Role` para los roles `Administrador`, `Odontologo` y `Recepcionista`.

## Desglose histórico previo (212 casos)

| Grupo | Pruebas |
|---|---:|
| Infraestructura | 1 |
| Login/autenticación | 15 |
| Configuración real de Identity | 1 |
| Atención odontológica | 13 |
| Pacientes | 40 |
| HTML de formulario de pacientes | 2 |
| Citas | 33 |
| Servicios | 7 |
| Usuarios | 9 |
| Dashboard | 8 |
| Diagnósticos | 9 |
| Tratamientos | 20 |
| Evoluciones clínicas | 12 |
| Integración HTTP/autorización | 24 |
| Seguros y SeguroSeeder | 13 |
| Configuración de ApplicationDbContextFactory | 5 |
| **Total** | **212** |

## Cobertura por grupo

- **Pacientes**: registro, cédula duplicada u opcional, seguros activos e históricos, embarazo condicionado y activación/desactivación.
- **Cédula (incluida en Pacientes)**: 14 casos nuevos verifican cumpleaños 18 mañana sin cédula, dos formatos completos normalizados, cinco entradas inválidas (incluida una voluntaria de menor), dos duplicados equivalentes en Create, dos ediciones de la cédula propia y dos duplicados en Edit, incluyendo registros históricos sin guiones. Se mantienen los casos existentes de menores, adultos y cumpleaños 18 hoy. El servidor rechaza letras, exceso, entradas incompletas y guiones incorrectos antes de persistir.
- **HTML de formulario de pacientes**: dos casos en `Integration/PacienteFormTests.cs` revisan Create/Edit mediante la Web real con SQLite en memoria: campo de texto, `inputmode="numeric"`, máximo visual 13, obligatoriedad no incondicional, elementos auxiliares y carga del script compartido. No ejecutan JavaScript. Este punto añade 16 casos: de 196 a 212.
- **Citas**: creación, autocomplete de pacientes activos, conflictos de horario, reagendamiento y estados finales.

  H9: los autocomplete de Create e Index invalidan inmediatamente y abortan las búsquedas previas mediante AbortController, antes del debounce de 300 ms. Solo la petición activa cuyo término coincide con el texto actual puede renderizar o limpiar por error; AbortError se ignora. Seleccionar un paciente o hacer clic fuera también cancela lo pendiente. Create conserva la invalidación de PacienteId a `0`; Index conserva pacienteId vacío y su guarda de submit.

  Validación manual completada satisfactoriamente en **Create e Index**, según confirmación del usuario. Se conserva la lista de escenarios para futuras regresiones:
  - Escribir «Mar», esperar que salga A, escribir «Maria» y resolver B antes de A: B debe permanecer visible. Verificar el aborto de A y, simulando una respuesta que aun así complete, la guarda de vigencia.
  - Vaciar el campo (también dejar solo espacios) con una petición pendiente: no se inicia otra búsqueda y la lista permanece cerrada.
  - Seleccionar un paciente con trabajo pendiente: se conservan nombre e ID y la lista no reaparece. Como escribir cierra las opciones inmediatamente, este intercalado puede necesitar respuestas controladas o una pausa del depurador.
  - Seleccionar y después editar el texto: Create deja `PacienteId = 0`; Index deja `pacienteId = ''`.
  - Hacer clic fuera mientras se busca: la lista no reaparece y nombre e ID no cambian.
  - Un aborto o error de A después de resultados de B no debe borrarlos ni mostrar errores técnicos. Comprobar además el mensaje vacío de Create, la lista oculta sin coincidencias de Index, Filtrar y Limpiar filtros.

  La infraestructura actual valida servidor y HTTP/Razor, pero no ejecuta JavaScript. La suite existente no demuestra estos intercalados; no se agregan pruebas que solo busquen fragmentos del código ni un framework nuevo.
- **Usuarios**: creación, roles, duplicidad de correo, cambio de rol y estados.
- **Servicios**: creación, edición, activación/desactivación y búsquedas.
- **Dashboard**: conteos generales y filtrado de citas para odontólogos.
- **Login/autenticación**: credenciales, usuarios inactivos, logout y `RememberMe`.
- **Bloqueo de cuentas (incluido en Login/autenticación)**: ocho casos nuevos en `AccountControllerTests.cs`: cuatro fallos sin bloqueo; quinto fallo con plazo de 60 segundos y contador reiniciado, parametrizado para los tres roles; contraseña correcta rechazada durante el bloqueo; acceso después de expirar; éxito antes del límite que reinicia el contador; e intentos durante el bloqueo que no prolongan el plazo. La prueba existente de cuenta inactiva también comprueba que no incrementa el contador. Se conserva la prueba de usuario inexistente.
- **Configuración real de Identity**: una prueba nueva en `Integration/IdentityConfigurationTests.cs` reutiliza `CustomWebApplicationFactory` y verifica las opciones de Web: cinco fallos, 60 segundos y bloqueo habilitado para usuarios nuevos. En conjunto, este punto agrega nueve casos y lleva la suite de 187 a 196 pruebas.
- **Atención odontológica**: creación desde una cita válida, prevención de atenciones duplicadas, conservación del paciente y odontólogo asignados y validación del odontólogo autorizado.
- **Diagnósticos**: creación asociada a una atención, múltiples diagnósticos por atención y validación del odontólogo asignado.
- **Tratamientos**: asociación con servicios activos, múltiples tratamientos por atención, estados `Planificado`, `En progreso` y `Completado`, transiciones válidas y restricciones del odontólogo asignado.
- **Evoluciones clínicas**: validación de fecha y descripción, múltiples evoluciones por atención y validación del odontólogo asignado.
- **Integración HTTP/autorización**: autenticación requerida, redirecciones, permisos por rol, edición de pacientes y acceso permitido o rechazado.
- **Seguros y SeguroSeeder**: catálogo administrativo, permisos, activación/desactivación, relación con pacientes, carga inicial idempotente y conservación de registros manuales.
- **Infraestructura**: funcionamiento básico de xUnit.
- **Configuración de ApplicationDbContextFactory**: cinco pruebas en `tests/MSDentalSys.Tests/Context/ApplicationDbContextFactoryTests.cs`. Validan la prioridad de los argumentos de conexión y que el contexto SQL Server mantiene la conexión cerrada, la lectura del archivo JSON del entorno, el rechazo claro de conexiones vacías o con espacios (dos casos) y el error ante un `contentRoot` inválido. No requieren SQL Server real.

## Base de datos y seguridad de las pruebas

No se utiliza `MSDentalSysDB`. Tampoco se ejecutan migraciones contra la base real ni `database update`.

Las pruebas de bloqueo utilizan Identity real con SQLite en memoria. La expiración se simula estableciendo `LockoutEnd` en el pasado mediante UserManager únicamente en la base de pruebas, sin esperar 60 segundos. El plazo se verifica entre las horas anterior y posterior al quinto intento más 60 segundos, sin igualdad exacta al milisegundo.

Las pruebas unitarias y de controlador utilizan bases SQLite en memoria. Las pruebas HTTP usan una base SQLite aislada durante la vida de la factory. La aplicación de pruebas se ejecuta en el entorno `Testing`, donde no se ejecutan `RoleSeeder`, `AdminSeeder` ni `SeguroSeeder` de producción. `SeguroSeeder` carga el catálogo inicial verificado de forma idempotente, conserva registros manuales y no elimina datos.

La autenticación de integración no utiliza usuarios reales ni User Secrets. El esquema de Tests emite claims controlados para simular cada rol y permitir verificar la autorización real de los controladores.

Las pruebas clínicas verifican además que el odontólogo solo pueda operar sobre la atención que le corresponde, que una atención pueda contener múltiples diagnósticos y evoluciones, y que los tratamientos respeten sus estados y transiciones permitidas.

## Comandos de validación

```powershell
dotnet build .\MSDentalSys.sln
dotnet test .\MSDentalSys.sln
```

Estado validado actualmente:

```text
212 pruebas correctas
0 fallidas
0 omitidas
```

## Alcance y limitaciones

### Comprobación manual de cédula en Create y Edit

Las pruebas manuales se realizaron correctamente en navegador, tanto en Create como en Edit. Se verificó:

- Cédula opcional para menores y obligatoria para adultos; cambio dinámico de FechaNacimiento, incluido quien cumple 18 hoy y quien cumple 18 mañana.
- Formato automático `00112345678` → `001-1234567-8`, escritura, borrado, edición en medio y pegado.
- Rechazo de exceso de dígitos, entrada incompleta y adulto sin cédula.
- Edit conservando la cédula propia y rechazo de cédula duplicada.

El servidor sigue siendo autoritativo. No se modifican masivamente cédulas históricas; la consulta contempla valores con y sin guiones. No se valida dígito verificador ni existencia oficial.

Las pruebas HTTP validan el pipeline de autenticación y autorización de rutas con `WebApplicationFactory`, incluyendo permisos por rol y acceso de usuarios anónimos. La autenticación se simula mediante claims controlados en el entorno `Testing`. No son pruebas de navegador y no utilizan Selenium, Playwright ni servicios externos. Tampoco constituyen pruebas de rendimiento ni cobertura total del sistema.

## Validación de Subservicios — Fase A

Base anterior: 313 pruebas. Resultado de esta fase: 350 aprobadas, 0 fallidas, 0 omitidas; 37 casos nuevos en Controllers/SubserviciosControllerTests.cs e Integration/SubserviciosIntegrationTests.cs.

Se comprueban Create/Edit, normalización, límites de duración, padres inexistentes/inactivos, duplicados incluso inactivos, nombres iguales en padres distintos, colisión concurrente real de UNIQUE en Create/Edit, rechazo de cambio de padre, conservación del snapshot al editar duración y activación/desactivación sin eliminar citas.

El esquema SQLite relacional comprueba citas con columnas nuevas nulas, asociaciones válidas, rechazo de pares servicio/subservicio incompatibles y CHECK de duración tanto en Cita como en Subservicio. Las pruebas del seeder verifican los 45 elementos, repetición, conservación de cambios manuales, registros inactivos y renombrados, y rollback por padres ausentes, ambiguos o inactivos. La integración HTTP verifica consultas de los tres roles, administración exclusiva, formularios Razor y antiforgery real.

Estas pruebas no activan ni demuestran H8: se conserva la detección de inicios exactos H4 y las pruebas previas H3. No se ejecutó database update sobre una BD operativa. Las duraciones del catálogo son parámetros operativos y no datos clínicos oficiales; no se introdujeron campos económicos.

## Validación de integración de citas — Fase B

Se agregan 21 casos en Controllers/CitasSubserviciosTests.cs e Integration/CitasSubserviciosIntegrationTests.cs. Se actualizan los datos de soporte de Create para incluir el subservicio obligatorio, conservando las pruebas anteriores H3/H4.

Validación final: 375 pruebas aprobadas, 0 fallidas y 0 omitidas; dotnet build con 0 errores y 0 advertencias. Build y pruebas se ejecutaron fuera del sandbox tras detectar restricciones de acceso a NuGet.Config y al registro de eventos de Windows.

Cobertura: permisos HTTP del endpoint (Administrador/Recepcionista, anónimo y otros roles), servicio inexistente/inactivo, filtrado de hijos activos, orden y contrato JSON mínimo; subservicio requerido, inexistente, inactivo o de otro servicio, duración fuera de rango y desactivación entre GET y POST. Se comprueba reconstrucción del formulario, snapshot desde BD, duración manipulada por HTTP ignorada, cambios posteriores del catálogo, nueva cita con nueva duración y conservación al reagendar. Details se verifica con procedimiento y con datos históricos NULL.

La integración verifica el markup y la entrega del script con limpieza, cancelación y guardas de respuestas obsoletas. No ejecuta JavaScript en un navegador ni simula la red con E2E; no se incorporó un framework de navegador. En esa fase H8 seguía pendiente, sin intervalos ni cambios en HasScheduleConflictAsync. No se creó ni aplicó una migración.

## Clasificación de subservicios — Fase 1

Baseline: 380 pruebas. Resultado: 403 aprobadas, 0 fallidas y 0 omitidas; build con 0 errores y 0 advertencias. Se agregan 23 casos en SubserviciosClasificacionTests y SubserviciosClasificacionIntegrationTests. Los formularios válidos de pruebas anteriores incorporan Principal sin eliminar su cobertura.

Se comprueban Create con ambos valores, rechazo server-side de NULL/0/99, Edit en ambos sentidos y clasificación progresiva de históricos, rechazo de pérdida de clasificación, conservación de duración/código/padre/snapshot, CHECK SQLite con NULL/1/2/99 y seeder de 45 entradas sin clasificación. La integración HTTP verifica selector, Details y Edit históricos, persistencia válida y rechazo de valores numéricos o texto manipulados. La suite anterior conserva cobertura de autorizaciones, endpoint ParaCitas y H3/H4.

La migración AddClasificacionToSubservicios se generó y revisó junto con el snapshot: solo columna nullable y CHECK en Up, sin cambios de datos. No se aplicó a la BD operativa. EF CLI 9.0.18 emitió un aviso por ser anterior al runtime 9.0.20; no se actualizaron herramientas como parte de esta fase.

## Servicios definitivos — Fase 2A

Baseline 403 pruebas; 415 aprobadas, 0 fallidas, 0 omitidas. ServicioCatalogoTests agrega 12 casos: conciliación de los cinco IDs, siete altas, 12 activos, idempotencia, conservación de duración legacy/fechas y referencias de citas, tratamientos y subservicios, sin alterar código/clasificación/duración del procedimiento. También cubre identidad incompatible, ID ausente, duplicado activo/inactivo y servicio ajeno, verificando ausencia de cambios parciales.

Un interceptor provoca un fallo después de SaveChanges y antes del commit para comprobar rollback real en SQLite. Las pruebas HTTP de autorización, antiforgery y mensajes de carga temporal pertenecían a la implantación y se sustituyeron por pruebas de ausencia de esas acciones al cerrar el proceso. Las pruebas previas H3/H4 permanecen aprobadas. Build: 0 errores y 0 advertencias. No se ejecutó la conciliación en la BD de desarrollo; no hay migración ni carga de los 139 procedimientos.

## Procedimientos definitivos — Fase 2B

Los resultados por fase siguientes son históricos; la validación del cierre se indica al final.

Baseline 415; resultado 444 pruebas aprobadas, 0 fallidas y 0 omitidas. Se agregan 29 casos en SubservicioCatalogoTests. Build con 0 errores y 0 advertencias. No se ejecuta la carga en la BD de desarrollo.

La fuente se contrasta mediante SHA-256 de una representación canónica calculada independientemente desde la especificación aprobada, incluidos todos los nombres, padres, códigos, descripciones, clasificaciones y duraciones. Doce casos verifican primer/último código y ambas clasificaciones por servicio. La carga relacional verifica cada campo de los 139 procedimientos, 142 filas totales, IDs 1/3 reutilizados, tres legados preservados/inactivos, 85/54, repetición sin cambios de IDs/fechas y conservación de citas/snapshots. Se prueba reconciliación de configuración de un código ya conocido sin alterar citas.

Se cubren padre/código incompatible, nombre duplicado, protección UNIQUE de códigos, identidad histórica incompatible, servicio inactivo y registros ajenos; un fallo después de SaveChanges y antes de commit comprueba rollback completo. HTTP mantiene consultas por los roles actuales, Details legacy y ParaCitas para los 12 servicios con duración correcta y exclusión de inactivos; los datos se preparan internamente en SQLite de pruebas. Las comprobaciones de endpoints temporales se sustituyeron por pruebas de ausencia de rutas. Las pruebas anteriores H3/H4 siguen aprobadas. No se modifica Citas ni se implementa H8.

## Cierre de implantación del catálogo

El catálogo definitivo ya está implantado. Se retiraron los controles y las acciones web PrepareCatalog, PrepareDefinitiveCatalog y LoadInitialCatalog. Los seeders definitivos y el provisional legacy permanecen internos; no se ejecutan contra la BD real en este cierre. La operación cotidiana se realiza mediante el CRUD habitual, conservando permisos.

Baseline 447; resultado 444 pruebas aprobadas, 0 fallidas y 0 omitidas. Se retiraron nueve casos exclusivos de endpoints temporales y se agregaron seis en CatalogoCierreTests: cuatro verifican ausencia de las tres acciones en MVC y respuestas GET 404 / POST 405 para Administrador, Recepcionista, Odontólogo y anónimo; dos verifican Index sin controles/textos técnicos y con Nuevo servicio/Nuevo subservicio. Se conserva la cobertura interna de conciliación, exactitud del catálogo (139, 85/54), idempotencia, rollback y legados. Las consultas HTTP ahora preparan sus datos directamente con el cargador interno en SQLite aislado.

Build: 0 errores y 0 advertencias; git diff --check sin errores. CRUD, selección de citas, snapshots y H3/H4 conservan sus pruebas. No hay cambios de catálogo, datos, esquema, migración, SQL manual ni H8.
# H8 — intervalos y concurrencia

`CitasOverlapTests` cubre Create/Reagendar, contención parcial/total, contigüidad, otro odontólogo, todos los estados, NULL en cualquiera o ambas duraciones, medianoche, 1/1440 minutos, límites DateTime, reconstrucción del formulario y exclusión propia con snapshot preservado. Se conserva la suite H3/H4. Las simulaciones SQLite de ganador previo confirman antes de comenzar la transacción compartida; no se presentan como evidencia de concurrencia SQL Server.

`CitasSqlServerOverlapTests` prepara Create/Create, Create/Reagendar y Reagendar/Reagendar con conexiones y contextos independientes y una barrera después de las consultas de rango Serializable. Exige una sola reserva y una respuesta controlada para la perdedora, preservando fechas/snapshots. No configura retries.

Por defecto esta teoría se omite. Para ejecutarla, establecer explícitamente `MSDENTALSYS_H8_SQLSERVER` con una conexión a `Initial Catalog=master` de un **servidor exclusivo de pruebas**, con permiso para crear/eliminar bases, y ejecutar `dotnet test --filter H8_SqlServer`. No se leen appsettings ni se utiliza MSDentalSysDB. El test genera una base `MSDentalSys_H8Tests_<GUID>`, crea el esquema con EnsureCreated y elimina únicamente esa base en finally. Una interrupción del proceso puede dejar esa base temporal pendiente de limpieza. No colocar credenciales en el repositorio.

La ejecución local sin esa variable verifica SQLite y omite la teoría SQL Server (tres combinaciones). Con configuración explícita se validaron los tres casos en SQL Server real: 497 pruebas normales y 3 casos SQL Server aprobados, 0 fallidos y 0 omitidos. Las sondas verifican disponibilidad y escritura en la misma transacción Serializable, commit posterior y deadlock 1205 controlado incluso con las envolturas de EF Core. Todas las bases temporales fueron eliminadas. No se utilizó MSDentalSysDB. Build: 0 errores y 0 advertencias.
