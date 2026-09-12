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

La integración verifica el markup y la entrega del script con limpieza, cancelación y guardas de respuestas obsoletas. No ejecuta JavaScript en un navegador ni simula la red con E2E; no se incorporó un framework de navegador. H8 sigue pendiente, sin intervalos ni cambios en HasScheduleConflictAsync. No se creó ni aplicó una migración.

## Clasificación de subservicios — Fase 1

Baseline: 380 pruebas. Resultado: 403 aprobadas, 0 fallidas y 0 omitidas; build con 0 errores y 0 advertencias. Se agregan 23 casos en SubserviciosClasificacionTests y SubserviciosClasificacionIntegrationTests. Los formularios válidos de pruebas anteriores incorporan Principal sin eliminar su cobertura.

Se comprueban Create con ambos valores, rechazo server-side de NULL/0/99, Edit en ambos sentidos y clasificación progresiva de históricos, rechazo de pérdida de clasificación, conservación de duración/código/padre/snapshot, CHECK SQLite con NULL/1/2/99 y seeder de 45 entradas sin clasificación. La integración HTTP verifica selector, Details y Edit históricos, persistencia válida y rechazo de valores numéricos o texto manipulados. La suite anterior conserva cobertura de autorizaciones, endpoint ParaCitas y H3/H4.

La migración AddClasificacionToSubservicios se generó y revisó junto con el snapshot: solo columna nullable y CHECK en Up, sin cambios de datos. No se aplicó a la BD operativa. EF CLI 9.0.18 emitió un aviso por ser anterior al runtime 9.0.20; no se actualizaron herramientas como parte de esta fase.

## Servicios definitivos — Fase 2A

Baseline 403 pruebas; 415 aprobadas, 0 fallidas, 0 omitidas. ServicioCatalogoTests agrega 12 casos: conciliación de los cinco IDs, siete altas, 12 activos, idempotencia, conservación de duración legacy/fechas y referencias de citas, tratamientos y subservicios, sin alterar código/clasificación/duración del procedimiento. También cubre identidad incompatible, ID ausente, duplicado activo/inactivo y servicio ajeno, verificando ausencia de cambios parciales.

Un interceptor provoca un fallo después de SaveChanges y antes del commit para comprobar rollback real en SQLite. Las pruebas HTTP comprueban autorización, antiforgery, mensajes de primera/segunda carga y deshabilitación efectiva del POST provisional. Las pruebas previas H3/H4 permanecen aprobadas. Build: 0 errores y 0 advertencias. No se ejecutó la conciliación en la BD de desarrollo; no hay migración ni carga de los 139 procedimientos.
