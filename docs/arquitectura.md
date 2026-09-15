# Arquitectura de MSDentalSys

## Visión general

MSDentalSys es una aplicación web que integra la gestión administrativa y clínica de una clínica dental. Utiliza .NET 9, ASP.NET Core MVC, Razor Views, Entity Framework Core 9, SQL Server y ASP.NET Core Identity. La interfaz combina HTML, CSS y JavaScript con Bootstrap, jQuery y sus componentes de validación.

La lógica de aplicación se coordina en los controladores MVC, que acceden a `ApplicationDbContext` o a los servicios de Identity. Las vistas Razor generan HTML en el servidor; algunos endpoints devuelven JSON para interacciones del navegador.

## Estructura de la solución

| Proyecto | Responsabilidad |
|---|---|
| `src/MSDentalSys.Data` | Entidades persistentes, `ApplicationDbContext`, almacenamiento EF de Identity, relaciones, índices, migraciones, `ApplicationDbContextFactory` y seeders. |
| `src/MSDentalSys.Web` | Controllers, ViewModels, Razor Views, recursos cliente y coordinación de reglas de aplicación. `Program.cs` configura servicios, persistencia, autenticación, autorización, middleware y rutas. |
| `tests/MSDentalSys.Tests` | Pruebas de controladores, integración HTTP, configuración del contexto y datos iniciales. |

Las referencias entre proyectos son `Web → Data`, `Tests → Web` y `Tests → Data`. Los proyectos de aplicación no dependen del proyecto de pruebas.

```text
MSDentalSys/
├── MSDentalSys.sln
├── global.json
├── src/
│   ├── MSDentalSys.Data/
│   │   ├── Context/
│   │   ├── InitialData/
│   │   ├── Migrations/
│   │   └── Models/
│   └── MSDentalSys.Web/
│       ├── Controllers/
│       ├── Models/ViewModels/
│       ├── Views/
│       ├── wwwroot/
│       ├── Program.cs
│       └── appsettings*.json
├── tests/
│   └── MSDentalSys.Tests/
│       ├── Context/
│       ├── Controllers/
│       ├── InitialData/
│       ├── Integration/
│       └── InfrastructureTests.cs
└── docs/
    └── prototipos/
```

`docs/prototipos/` conserva el prototipo visual histórico y no forma parte de la aplicación ejecutable.

### Dependencias y restauración

`global.json` fija el SDK 9.0.316 y los tres proyectos usan `net9.0`. Las referencias directas de EF Core, Identity, configuración, SQLite y Mvc.Testing están fijadas a 9.0.20; Tests utiliza xUnit 2.9.3, xunit.runner.visualstudio 3.0.2 y Microsoft.NET.Test.Sdk 17.13.0. Los recursos cliente versionados incluyen Bootstrap 5.3.3, jQuery 3.7.1, jQuery Validation 1.21.0 y Unobtrusive Validation 4.0.0.

Data, Web y Tests habilitan `RestorePackagesWithLockFile` y versionan cada `packages.lock.json`, que registra la resolución de dependencias. Se puede comprobar su coherencia mediante `dotnet restore --locked-mode` desde la raíz. El repositorio no habilita globalmente `RestoreLockedMode`: generar/utilizar un lock file no equivale a exigir ese modo. Una actualización intencional debe revisar conjuntamente los `.csproj` y sus lock files. La herramienta global `dotnet-ef` se administra por separado.

## Flujo MVC

Una solicitud del navegador pasa por el middleware y el enrutamiento, la autenticación y la autorización, hasta llegar a la acción del Controller. MVC vincula los datos recibidos al modelo; el controlador comprueba la validación y las reglas de aplicación antes de consultar o guardar mediante `ApplicationDbContext` o servicios Identity. La persistencia utiliza EF Core sobre SQL Server.

Para responder, el Controller puede entregar un ViewModel o una entidad a una Razor View, que genera el HTML enviado al navegador. Las vistas de formularios suelen utilizar ViewModels; algunas vistas de listado y detalle reciben entidades directamente. Las operaciones también pueden devolver redirecciones o errores HTTP.

`CitasController.BuscarPacientes` es un ejemplo de respuesta JSON: consulta pacientes activos para el autocomplete de citas, buscando por nombre, apellido o cédula y limitando los resultados a diez.

## Módulos funcionales

| Módulo | Controller | ViewModels principales | Responsabilidad |
|---|---|---|---|
| Autenticación | `AccountController` | `LoginViewModel` | Login, Logout y AccessDenied. |
| Dashboard | `DashboardController` | `DashboardViewModel` | Indicadores de pacientes, citas y atenciones; filtrado por odontólogo cuando corresponde. |
| Pacientes | `PacientesController` | `PacienteFormViewModel` | Registro, edición, consulta, antecedentes clínicos y estado del paciente. |
| Citas | `CitasController` | `CitaFormViewModel`, `ReagendarCitaViewModel`, `ActualizarEstadoCitaViewModel` | Agenda, búsqueda de pacientes, reagendamiento y estados. |
| Servicios | `ServiciosController` | `ServicioFormViewModel`, `ServiciosIndexViewModel` | Catálogo de servicios odontológicos y su activación. |
| Subservicios | `SubserviciosController` | `SubservicioFormViewModel` | Procedimientos por servicio, duración estimada, clasificación, código de catálogo y selección para citas. |
| Usuarios | `UsuariosController` | `UsuarioCreateViewModel`, `UsuarioEditViewModel`, `UsuarioDetailsViewModel`, `UsuarioListItemViewModel`, `UsuariosIndexViewModel` | Administración de usuarios, roles permitidos y estado activo. |
| Seguros | `SegurosController` | `SeguroFormViewModel` | Catálogo de seguros médicos y su activación. |
| Atenciones odontológicas | `AtencionesController` | `AtencionOdontologicaCreateViewModel` | Creación desde una cita y consulta de la atención con sus registros clínicos. |
| Diagnósticos | `DiagnosticosController` | `DiagnosticoCreateViewModel` | Registro de diagnósticos asociados a una atención. |
| Tratamientos | `TratamientosController` | `TratamientoCreateViewModel` | Registro de tratamientos asociados a servicios y actualización de sus estados. |
| Evoluciones clínicas | `EvolucionesClinicasController` | `EvolucionClinicaCreateViewModel` | Registro de evoluciones asociadas a una atención. |

`HomeController.Index` redirige `/` a `/Account/Login` para anónimos y a `/Dashboard` para autenticados. Login GET también redirige al usuario autenticado a Dashboard. `/Home/Privacy` fue retirado y devuelve 404. `/Home/Error` conserva la respuesta controlada con `ErrorViewModel`, sin caché ni instrucciones técnicas de plantilla; `Program.cs` lo configura como manejador de excepciones fuera de Development. `HomeFlowTests` verifica este flujo. El catálogo de seguros registra nombres y estado, sin modelar coberturas, pólizas, reclamaciones ni facturación.

## Seguridad y autorización

`ApplicationUser` hereda de `IdentityUser` y agrega datos personales y el campo `Estado`. ASP.NET Core Identity utiliza `UserManager<ApplicationUser>` para usuarios y roles asignados, `SignInManager<ApplicationUser>` para inicio y cierre de sesión, y `RoleManager<IdentityRole>` para la creación de roles iniciales.

`Program.cs` registra Identity con almacenamiento EF, configura las cookies y establece las rutas `/Account/Login` y `/Account/AccessDenied`. También dispone `UseAuthentication` antes de `UseAuthorization`. Los controladores aplican `[Authorize]` y restricciones por rol; las operaciones sensibles de formulario utilizan `[ValidateAntiForgeryToken]`.

| Rol | Alcance principal |
|---|---|
| `Administrador` | Gestión administrativa, usuarios, seguros y acceso a los módulos clínicos conforme a sus reglas. |
| `Recepcionista` | Consulta, creación y edición de pacientes, gestión administrativa de citas y consulta de servicios; sin administración de usuarios o seguros ni acceso a módulos clínicos. |
| `Odontologo` | Consulta de pacientes y servicios, acceso a sus citas y a los registros clínicos de sus atenciones asignadas. |

`AccountController` rechaza el Login de usuarios inactivos antes de comprobar la contraseña mediante Identity. La política de bloqueo se configura en `Program.cs`: cinco intentos fallidos y duración de 60 segundos, habilitada para usuarios nuevos. `PasswordSignInAsync` se invoca con `lockoutOnFailure: true`. `AccessDenied` presenta la denegación de acceso y Logout utiliza `SignOutAsync`.

`UsuariosController` protege al administrador inicial frente a desactivación y cambio de rol. El Dashboard requiere autenticación: filtra citas y atenciones para el odontólogo, mientras el total de pacientes activos es global.

La política de contraseña de `Program.cs` requiere longitud mínima de ocho caracteres, dígito, mayúscula, minúscula y carácter no alfanumérico; `RequireConfirmedAccount` es false. Create de usuarios valida correo obligatorio, formato y máximo de 120 caracteres mediante `UsuarioCreateViewModel`; tras validar, aplica Trim, comprueba duplicidad y asigna Email/UserName. Edit conserva el correo leído de BD.

### SecurityStamp y atomicidad de usuarios

`UsuariosController` renueva `SecurityStamp` al pasar efectivamente de activo a inactivo y ante un cambio efectivo de rol. La edición únicamente de datos personales conserva el stamp. `SecurityStampValidatorOptions.ValidationInterval` es un minuto: la cookie se revalida en una petición posterior al intervalo, no se revoca instantáneamente en cada request. Reactivar usa `UpdateAsync` y conserva el stamp renovado; no recupera cookies anteriores. `SessionRevocationTests` ejercita cookies reales de Identity y un reloj controlado.

Create agrupa creación del usuario y asignación de rol en una transacción del contexto compartido con Identity. Edit agrupa actualización del usuario, adición/eliminación de roles y renovación del stamp cuando corresponde. Solo se confirma al completar todas las operaciones. Un fallo intermedio, incluido un `IdentityResult` fallido, revierte la unidad no confirmada y limpia `ChangeTracker`; las excepciones no traducidas se propagan. No se atribuye aislamiento Serializable a estas transacciones.

Si Identity falla al activar/desactivar, se conserva `TempData["ErrorMessage"]` y se redirige a Details. El éxito mantiene `SuccessMessage` y la redirección a Index; un ID inexistente devuelve NotFound. El cierre y sus seis casos se registran en [pruebas](pruebas.md#cierre-final-de-validación).

## Reglas de aplicación relevantes

- Pacientes, servicios, usuarios y seguros utilizan activación y desactivación lógica.
- Solo Administrador activa/desactiva pacientes; Administrador y Recepcionista crean/editan la admisión y sus siete campos de antecedentes básicos. Odontologo los consulta en Details, sin acceso administrativo a Create/Edit.
- El odontólogo está restringido a sus citas y atenciones asignadas, incluidos diagnósticos, tratamientos y evoluciones. La actualización directa de una cita por este rol solo permite marcar `No asistió`.
- `AtencionesController` crea la atención conservando el paciente y el odontólogo de la cita y cambia su estado a `Atendida` dentro de una transacción. La actualización administrativa de estados no permite establecer `Atendida` directamente. `Cancelada` y `Atendida` son estados finales.
- La cédula es obligatoria desde los 18 años y opcional para menores. Sin `FechaNacimiento` no se infiere mayoría de edad. El servidor valida también la cédula voluntaria, acepta once dígitos o `XXX-XXXXXXX-X`, normaliza a ese formato y comprueba unicidad considerando ambas representaciones, incluidos registros históricos sin guiones.
- Se rechaza `FechaNacimiento` futura. Sexo admite exactamente `Femenino`, `Masculino`, `Otro` o ausencia; valores no canónicos se rechazan. Embarazo se normaliza a NULL cuando Sexo no corresponde a `Femenino`.
- El seguro es opcional: sin `TieneSeguro`, se fuerza SeguroId a NULL. Una nueva asociación exige seguro activo; Edit puede conservar el seguro actual aunque esté inactivo.
- Al reconstruir un formulario inválido de Atenciones, una cita inexistente devuelve NotFound y una cita ajena al odontólogo devuelve Forbid antes de exponer sus datos en ViewData.

### Concurrencia H3/H4/H8

- **H3:** `ExecuteUpdateAsync` condiciona los cambios al estado leído. Se aplica a estados de citas, reserva de una cita Pendiente/Confirmada para crear atención y estados de tratamientos. Reagendar compara además el inicio original. Se exige una fila afectada; cero filas comunica conflicto. La reserva de atención y su inserción pertenecen a la misma transacción y se revierten juntas si fallan.
- **H4:** el índice filtrado `UX_Citas_Odontologo_FechaHoraInicio_NoCancelada` impide inicios idénticos para un odontólogo en citas no canceladas, incluso ante escrituras concurrentes. No sustituye la comprobación de intervalos.
- **H8:** Create y Reagendar comprueban intervalos y escriben dentro de una misma transacción Serializable. Se detalla al final; conserva H3 y H4. `EstadoConcurrencyTests`, `CitasScheduleTests`, `CitasOverlapTests` y la teoría SQL Server opcional comprueban alcances distintos.

### Consultas y unicidad

H11: `UsuariosController.Index` consulta usuarios sin tracking y obtiene sus roles mediante una consulta agrupada, acotada a los IDs seleccionados, sin `GetRolesAsync` por cada fila. Mantiene filtros, orden y representación de un rol o «Sin rol». H12: `PacientesController.Index` usa `AsNoTracking` y no carga antecedentes clínicos; Details/Edit conservan sus Includes. Son cambios de consulta, sin porcentajes de mejora medidos.

H15: Seguros Create/Edit comprueban previamente duplicados y capturan únicamente la infracción esperada de unicidad de `Seguro.Nombre`: índice `IX_Seguros_Nombre` con SQL Server 2601/2627 o la infracción correspondiente en SQLite. Desvinculan la entidad rechazada y devuelven el formulario con error de nombre; excepciones ajenas se propagan. La unicidad incluye seguros inactivos y Edit excluye el propio ID.

## Persistencia y relaciones principales

`src/MSDentalSys.Data/Context/ApplicationDbContext.cs` hereda de `IdentityDbContext<ApplicationUser>`. Declara los siguientes `DbSet`: `Pacientes`, `AntecedentesClinicos`, `ServiciosOdontologicos`, `SubserviciosOdontologicos` (`DbSet<SubservicioOdontologico>`), `Citas`, `AtencionesOdontologicas`, `Diagnosticos`, `EvolucionesClinicas`, `Tratamientos` y `Seguros`. Identity administra las tablas de usuarios, roles y sus asociaciones a través del mismo contexto.

Las entidades se encuentran en `src/MSDentalSys.Data/Models`. `OnModelCreating` configura mediante Fluent API las relaciones, índices y comportamientos de eliminación, además de conservar la configuración base de Identity.

| Origen → destino | Cardinalidad |
|---|---|
| `Paciente` → `AntecedenteClinico` | 1 → 0..1 |
| `Paciente` → `Cita` | 1 → N |
| `Paciente` → `AtencionOdontologica` | 1 → N |
| `ApplicationUser` como odontólogo → `Cita` | 1 → N |
| `ApplicationUser` como odontólogo → `AtencionOdontologica` | 1 → N |
| `ServicioOdontologico` → `Cita` | 1 → N |
| `ServicioOdontologico` → `Tratamiento` | 1 → N |
| `ServicioOdontologico` → `SubservicioOdontologico` | 1 → N |
| `SubservicioOdontologico` → `Cita` | 1 → N; asociación opcional en citas históricas |
| `Cita` → `AtencionOdontologica` | 1 → 0..1 |
| `AtencionOdontologica` → `Diagnostico` | 1 → N |
| `AtencionOdontologica` → `Tratamiento` | 1 → N |
| `AtencionOdontologica` → `EvolucionClinica` | 1 → N |
| `Seguro` → `Paciente` | 1 → N |
| Usuarios ↔ roles | N ↔ N mediante Identity |

N representa cero o más registros relacionados. `Paciente.SeguroId` es opcional. `AtencionOdontologica.CitaId` también es nullable en el modelo, aunque el flujo web actual crea atenciones desde citas. Cada `AntecedenteClinico` requiere un paciente; un paciente puede no tener antecedente. La pertenencia al rol `Odontologo` se valida en la aplicación, mientras las claves foráneas referencian `ApplicationUser`.

El índice `IX_Pacientes_Cedula` es único y está filtrado por `[Cedula] IS NOT NULL`; `Seguro.Nombre` tiene un índice único. Las relaciones uno a uno limitan a un antecedente por paciente y a una atención por cita asociada.

El borrado se restringe en las relaciones de citas, pacientes, odontólogos, servicios y seguros configuradas con `DeleteBehavior.Restrict`. Se configura cascada desde paciente hacia antecedente y desde atención hacia diagnósticos, tratamientos y evoluciones. Estas reglas de integridad del esquema no equivalen a ofrecer eliminación física en los módulos clínicos.

## Configuración y migraciones compartidas

Web obtiene `ConnectionStrings:DefaultConnection` mediante la configuración estándar de ASP.NET Core y registra SQL Server con el ensamblado de migraciones de `MSDentalSys.Data`.

`ApplicationDbContextFactory`, en `src/MSDentalSys.Data/Context`, crea el contexto para EF CLI. Lee la configuración Web sin referencia de proyecto `Data → Web`. Busca el proyecto desde el directorio actual y las ubicaciones de ejecución y ensamblado, ascendiendo por el repositorio; admite `--contentRoot` para indicar la carpeta Web. Requiere el proyecto fuente y lee su `UserSecretsId` del `.csproj`, sin duplicarlo.

En la fábrica, el entorno procede de `--environment`, `DOTNET_ENVIRONMENT` o `ASPNETCORE_ENVIRONMENT`, en ese orden; por defecto es `Production`. La configuración se carga en prioridad creciente:

1. `appsettings.json`.
2. `appsettings.{Environment}.json`.
3. User Secrets de Web, solo en `Development`.
4. Variables de entorno.
5. Argumentos.

`ConnectionStrings__DefaultConnection` permite sobrescribir la conexión. Los comandos de desarrollo indican `-- --environment Development` para cargar User Secrets; EF CLI no depende de `launchSettings.json`. La fábrica configura el contexto y el ensamblado de migraciones, sin abrir conexiones, aplicar migraciones ni ejecutar seeders.

Las migraciones actuales en `src/MSDentalSys.Data/Migrations` son:

- `20260809222101_InitialCreate`.
- `20260823022042_AddSeguros`.
- `20260823224821_AddSeguroToPaciente`.
- `20260909233805_AddUniqueAppointmentScheduleIndex`.
- `20260911231453_AddSubserviciosOdontologicos`.
- `20260912225000_AddClasificacionToSubservicios`.

El flujo del equipo es: integrar cambios del repositorio → cambiar el modelo cuando corresponda → generar y revisar la migración → compartir modelo, migración, `.Designer.cs` y `ApplicationDbContextModelSnapshot.cs` mediante Git → los demás integrantes actualizan el repositorio y aplican las migraciones recibidas a su SQL Server local. No deben generar otra migración para un cambio ya recibido.

Cada base registra las migraciones aplicadas en `__EFMigrationsHistory`, mediante `MigrationId` y `ProductVersion`. El snapshot es la referencia del modelo para generar cambios, no el historial de una base local. La aplicación no aplica migraciones automáticamente. Las migraciones compartidas representan la evolución del esquema; los datos operativos y el archivo físico de la base permanecen en cada entorno. Los secretos se configuran localmente y no se comparten por Git.

El procedimiento y los comandos están en [Flujo de migraciones para el equipo](../README.md#flujo-de-migraciones-para-el-equipo).

### Datos iniciales

`Program.cs` ejecuta únicamente `RoleSeeder`, `AdminSeeder` y `SeguroSeeder` al iniciar Web, salvo en el entorno `Testing`. El esquema debe estar preparado previamente.

| Seeder | Responsabilidad |
|---|---|
| `RoleSeeder` | Crea los tres roles si no existen, mediante `RoleManager`. |
| `AdminSeeder` | Crea el administrador inicial si falta y asegura su rol mediante `UserManager`. Obtiene la contraseña inicial de configuración; no cambia la de un usuario ya existente. |
| `SeguroSeeder` | Incorpora de forma idempotente los nombres del catálogo inicial de seguros y conserva registros manuales. |

La procedencia del catálogo se documenta en [seguros.md](seguros.md).

Las migraciones actuales crean/evolucionan el esquema y no insertan el catálogo odontológico definitivo. Los seeders de ese catálogo no se ejecutan en startup. `ServicioCatalogoSeeder` exige los servicios históricos con IDs 1–5; `SubservicioCatalogoSeeder` requiere los doce servicios preparados y los procedimientos históricos previstos. Son cargadores de conciliación sobre la historia del proyecto, no instaladores genéricos desde cero. Una BD nueva migrada no recibe automáticamente ese catálogo y no hay un procedimiento genérico de instalación del catálogo implementado que esta documentación pueda ofrecer.

Se distinguen el catálogo fuente versionado, el registro histórico de implantación en la BD utilizada durante aquella etapa y el estado de cada instalación. Git permite inspeccionar fuente, esquema y pruebas; no permite certificar los datos o migraciones aplicadas de una BD operativa. El registro histórico no identifica suficientemente esa instancia y fecha para verificarlas solo desde el repositorio.

## Componentes cliente

`src/MSDentalSys.Web/wwwroot/js/pacientes-form.js` se comparte entre Create y Edit mediante `Views/Pacientes/_PacienteForm.cshtml`. Gestiona el formato automático de cédula, su obligatoriedad según `FechaNacimiento`, la selección de seguro y la presentación del campo embarazo. Integra el apoyo de validación cliente; la validación definitiva permanece en el servidor.

`Views/Citas/Create.cshtml` utiliza `BuscarPacientes`, que devuelve únicamente pacientes activos. `Views/Citas/Index.cshtml` utiliza `BuscarPacientesParaFiltro`, que incluye activos e inactivos para consulta histórica. Ambos buscan por nombre, apellido o cédula y limitan a diez resultados. Conservan debounce de 300 ms, AbortController, invalidación inmediata y guardas contra respuestas obsoletas; al editar el texto se invalida el ID (0 en Create y vacío en Index). Seleccionar un paciente o hacer clic fuera cancela lo pendiente. `Views/Account/Login.cshtml` permite mostrar u ocultar la contraseña.

El layout utiliza Bootstrap y jQuery, y los formularios incorporan jQuery Validation/Unobtrusive mediante el parcial de validación. `wwwroot/js/site.js` está incluido en el layout, pero actualmente solo contiene comentarios de plantilla.

## Estrategia de pruebas

El estado final validado es **542 pruebas aprobadas, 0 fallidas y 1 teoría H8 SQL Server omitida** en la ejecución estándar sin `MSDENTALSYS_H8_SQLSERVER`. Su organización en `tests/MSDentalSys.Tests` es:

| Ubicación | Alcance |
|---|---|
| `Controllers/` | Reglas administrativas y clínicas, validación, persistencia y autenticación; utiliza contextos SQLite en memoria cuando requiere datos. |
| `Integration/` | Integración HTTP, autorización, HTML de formularios y opciones reales de Identity mediante `CustomWebApplicationFactory`. |
| `Context/` | Configuración de `ApplicationDbContextFactory`, prioridades y errores, sin abrir conexiones SQL Server. |
| `InitialData/` | Comportamiento de `SeguroSeeder`, idempotencia y conservación de datos manuales. |
| `InfrastructureTests.cs` | Comprobación básica de la infraestructura xUnit. |

`CustomWebApplicationFactory` utiliza `WebApplicationFactory`, fuerza `Testing`, reemplaza SQL Server por SQLite en memoria y prepara el esquema con `EnsureCreated`. Usa claims controlados para autorización. `IdentityCookieWebApplicationFactory` conserva las cookies reales de ASP.NET Core Identity y controla el reloj para verificar revocación de sesiones. Ambas usan datos aislados y omiten los tres seeders de arranque. Las pruebas de Login ejercitan servicios Identity con persistencia aislada; H8 incorpora además una teoría SQL Server opcional.

Las pruebas HTTP de vistas verifican HTML, pero no ejecutan JavaScript. La cobertura y las comprobaciones manuales se detallan en [pruebas.md](pruebas.md).

## Diagrama general

```text
Navegador: HTML / CSS / JavaScript
                 ↕ HTTP: HTML / JSON
MSDentalSys.Web
  Middleware: routing → autenticación → autorización
                 ↓
  Controllers → Razor Views → HTML al navegador
      │          ViewModels / entidades como modelos de vista
      ├── Servicios Identity
      │        │
      ↓        ↓
MSDentalSys.Data
  ApplicationDbContext
  Entidades + almacenamiento Identity
                 ↕ EF Core
             SQL Server

EF CLI → ApplicationDbContextFactory → contexto / migraciones
Inicio Web → RoleSeeder / AdminSeeder / SeguroSeeder (salvo Testing)
MSDentalSys.Tests → Web / Data
  Pruebas aisladas: SQLite InMemory / WebApplicationFactory
```

## Subservicios odontológicos — Fase A

ServicioOdontologico tiene una relación 1:N con SubservicioOdontologico. Cada procedimiento registra Nombre (100), Descripcion opcional (300), DuracionEstimadaMinutos obligatoria entre 1 y 1440, Estado y FechaCreacion. El Administrador administra; Recepcionista y Odontologo consultan. No se ofrece eliminación física ni cambio de servicio padre al editar. Desactivar el padre conserva los estados individuales de sus hijos; activar un hijo requiere padre activo.

Cita conserva ServicioOdontologicoId y agrega SubservicioOdontologicoId y DuracionProgramadaMinutos nullable, sin valores por defecto ni backfill. Una FK compuesta garantiza la pertenencia del subservicio al servicio; las eliminaciones son Restrict. Los CHECK validan duraciones y el índice único (ServicioOdontologicoId, Nombre) incluye inactivos. Las comparaciones de nombres conservan la collation del proveedor y Trim del formulario.

En Fase A estas columnas prepararon el esquema y permitieron citas sin subservicio ni duración. ServicioOdontologico es el agrupador; su duración permanece como propiedad/columna legacy por compatibilidad, sin binding en ServicioFormViewModel ni visualización o edición en Servicios. Edit conserva el valor histórico y Create deja el campo legacy nulo. La duración operativa pertenece al procedimiento SubservicioOdontologico y se copia al snapshot de Cita al crearla. Tratamiento continúa asociado al servicio principal.

### Integración de citas — Fase B

Las nuevas citas requieren SubservicioOdontologicoId en CitaFormViewModel. GET Create carga odontólogos y servicios activos, sin consultar todos los subservicios. GET /Subservicios/ParaCitas?servicioOdontologicoId={id}, autorizado para Administrador y Recepcionista, devuelve únicamente ID, nombre y duración de hijos activos ordenados por nombre; devuelve 404 si el servicio no existe o está inactivo.

POST Create verifica en BD servicio activo, subservicio activo, pertenencia exacta y duración entre 1 y 1440 minutos, además de paciente y odontólogo activos. Copia la duración del catálogo a DuracionProgramadaMinutos; el ViewModel no admite duración del cliente. Actualmente comprueba intervalos H8 y conserva la protección H4. Un POST inválido reconstruye las opciones del servicio y conserva únicamente selecciones válidas. Las citas históricas pueden conservar NULL; Details muestra «No registrado».

El script dedicado citas-subservicios.js mantiene separado el selector dependiente del autocomplete existente: limpia la selección al cambiar servicio, cancela con AbortController y comprueba tanto la petición vigente como el servicio actual antes de aplicar resultados o errores. Index conserva sus seis columnas; el procedimiento y snapshot se consultan en Details.

Reagendar solo cambia fecha/hora y conserva servicio, subservicio y snapshot incluso después de modificar el catálogo. La Fase B no agregó migración y conservó H3/H4: entonces HasScheduleConflictAsync utilizaba mismo odontólogo + mismo inicio + estado distinto de Cancelada. En aquella fase H8 estaba pendiente; posteriormente se implementó y cerró la detección por intervalos descrita al final.

El seeder provisional histórico requiere sus 10 padres originales y conserva sus reglas transaccionales. Tras el cierre de implantación no tiene botón ni acción LoadInitialCatalog; permanece únicamente como infraestructura legacy. No se ejecuta en Program.

El catálogo provisional legacy contiene exactamente 45 subservicios. Sus duraciones son parámetros operativos de MSDentalSys, no información oficial clínica. CodigoCatalogo es una identidad técnica nullable, única cuando está presente y no editable desde el formulario. Permite reconocer entradas renombradas y no recrearlas. La carga vincula registros coincidentes existentes sin modificar nombre, descripción, duración o estado; conserva registros manuales. Los códigos existentes no deben renumerarse en cambios futuros del catálogo. La carga controlada debe ejecutarse por un administrador a la vez.

La migración AddSubserviciosOdontologicos debe aplicarse mediante el procedimiento habitual de despliegue antes de utilizar el catálogo; el seeder no ejecuta migraciones ni EnsureCreated. No existe componente de precios, costos, tarifas ni facturación.

### Clasificación de subservicios — Fase 1

ClasificacionSubservicio se define en Data/Models con Principal = 1 y Complementario = 2. SubservicioOdontologico.Clasificacion es nullable y se convierte a entero; CK_Subservicios_Clasificacion permite únicamente NULL, 1 o 2. AddClasificacionToSubservicios agrega columna y CHECK sin default, backfill ni cambios a relaciones, índices o catálogo.

El ViewModel requiere clasificación y SubserviciosController valida explícitamente los dos valores permitidos tanto en Create como en Edit. Edit GET permite históricos NULL; guardar exige clasificarlos. El selector compartido en _Fields sirve a Create/Edit y Details muestra «Sin clasificar» para NULL. Index conserva sus cinco columnas para no ensanchar la tabla compartida con Details de Servicios; la clasificación se consulta en Details del subservicio.

El seeder provisional crea sus registros con clasificación NULL. Activación, autorización y selección para Citas conservan su comportamiento, sin filtros por clasificación. La Fase 1 no cambió duración, códigos, FK compuesta, snapshot de citas, Reagendar ni H3/H4, ni cargó procedimientos. En esa fase H8 seguía pendiente; posteriormente quedó implementado y cerrado.

### Servicios definitivos — Fase 2A

ServicioCatalogoSeeder contiene las 12 categorías objetivo. La acción temporal Servicios/PrepareCatalog fue retirada al cerrar la implantación; el seeder permanece interno. No hay ejecución en startup ni migración. Valida los IDs 1–5 contra sus nombres históricos o definitivos permitidos; no usa coincidencias aproximadas. Para los siete restantes admite el nombre objetivo con diferencias de mayúsculas o espacios exteriores. Rechaza coincidencias múltiples incluso inactivas, IDs históricos ausentes/reutilizados y cualquier servicio ajeno al conjunto objetivo. Este último caso requiere revisión explícita, sin eliminar datos.

Tras validar todo, conserva IDs, fechas, descripciones y duración legacy; normaliza nombres y activa los objetivos, creando solo los ausentes con los defaults de la entidad. Una repetición sin cambios devuelve false. La transacción Serializable abarca lectura y escritura hasta el commit. Las validaciones propias del catálogo tienen mensajes explícitos; las excepciones de persistencia no se traducen indiscriminadamente a mensajes de catálogo y se propagan tras el rollback correspondiente y la limpieza del tracking. No se modifican relaciones, subservicios, citas ni tratamientos.

La conciliación conserva 1 Periodoncia, 2 Odontología general, 3 Endodoncia, 4 Cirugía oral y 5 Rehabilitación oral / Prótesis. Completa con Odontología estética, Implantología, Ortodoncia, Odontopediatría, Odontología preventiva, Odontología digital y Odontología para pacientes con necesidades especiales. Esta operación prepara solamente servicios; la Fase 2B incorpora el cargador separado de procedimientos.

### Procedimientos definitivos — Fase 2B

Data/InitialData/SubservicioCatalogo.cs contiene 139 entradas inmutables con códigos explícitos permanentes, padre, nombre, descripción, clasificación y minutos. No se generan códigos a partir de la posición. SubservicioCatalogoSeeder valida 139 códigos únicos y completos, 85 Principal/54 Complementario, nombres/descripciones con límites del modelo, pares padre/nombre únicos y duración 1–1440 antes de iniciar la carga.

La transacción Serializable abarca lectura, validación, conciliación y commit. Comprueba los 12 padres activos de Fase 2A, incluidos sus IDs históricos; valida subservicios 1–5, códigos y colisiones de nombre mediante consultas con la collation de BD, incluidos inactivos. IDs 1/3 aceptan su identidad histórica prevista con código/clasificación NULL antes de la conversión, o su código definitivo en el mismo ID sujeto a validación de padre y nombre. IDs legacy 2/4/5 deben conservar nombre, padre, duración y código NULL; se inactivan y conservan su clasificación existente, que no se exige NULL. Registros ajenos al conjunto previsto provocan aborto. Las validaciones generan `CatalogoValidationException` con mensajes explícitos; las excepciones de persistencia se propagan tras revertir la transacción no confirmada y limpiar el tracking.

Para códigos ya conocidos se exige padre y nombre compatibles: no se trasladan códigos ni se cambian IDs. La política conservadora rechaza diferencias de nombre; descripción, clasificación, minutos y estado pueden reconciliarse. Las dos conversiones históricas autorizadas sí cambian nombre y asignan código. Se conservan fechas e IDs; no hay actualizaciones de Cita ni Tratamiento. El nombre mostrado en citas históricas refleja el nuevo nombre del catálogo en IDs 1/3; su snapshot de duración permanece intacto.

El cierre histórico registró la implantación del catálogo en la BD utilizada durante esa etapa; no certifica el estado de otras instalaciones. Se retiraron PrepareCatalog, PrepareDefinitiveCatalog y LoadInitialCatalog, sus botones y textos auxiliares. Los cargadores permanecen internos para pruebas y conciliación controlada con las precondiciones indicadas en Datos iniciales. La gestión cotidiana utiliza el CRUD normal; no hay endpoint alternativo de carga ni ampliación de permisos. Aquel cierre no agregó migración ni ejecutó carga desde Program, SQL manual o eliminación física. Las duraciones son bloques operativos de agenda, no tiempos clínicos obligatorios. Entonces H8 continuaba pendiente; quedó implementado y cerrado posteriormente.

## H8 — reserva por intervalos

`CitasController.HasScheduleConflictAsync` recibe odontólogo, inicio, duración nullable e ID a excluir. Create usa la duración validada del subservicio de BD (1–1440 minutos); Reagendar usa exclusivamente el snapshot existente. El candidato se limita a inicios desde `nuevoInicio - 1440 minutos` hasta antes de `nuevoFin`, acotando DateTime.MinValue. Se proyectan inicio y snapshot dentro de Serializable y se comparan ticks para evitar desbordar el fin de citas existentes. El fin nuevo se valida antes de abrir la transacción. El CHECK existente limita la duración máxima; no se crea migración.

Cada operación mantiene la lectura de conflictos y su INSERT/UPDATE en la misma transacción Serializable hasta commit. Reagendar conserva la lectura inicial y la condición H3 por ID, estado e inicio originales: cero filas revierte y comunica concurrencia. Se excluye la propia cita. El índice H4 filtrado permanece intacto como protección adicional de inicio exacto. Las validaciones de formulario y catálogo preceden la transacción.

NULL en cualquiera de las duraciones implica comprobar solamente inicio idéntico; no se modifica información histórica. Cancelada no ocupa agenda; los demás estados conservan sus reglas. SQL Server 1205 se maneja como contención, sin retry: el servidor ya revirtió la transacción víctima. Create descarta la entidad Added rechazada y reconstruye las opciones. Solo errores del índice H4 se convierten en su conflicto específico; otros errores se propagan. Las pruebas SQLite no demuestran bloqueos/rangos de SQL Server: la prueba real es opcional y aislada.

## Matriz de cierre H1–H16

La correspondencia se reconstruye desde implementación, pruebas, referencias H existentes e historial Git; no todos los commits nombran explícitamente su H. Los hashes identifican el cambio verificable y no implican que el orden numérico sea el orden de commits. «Cerrado» se refiere al alcance implementado; los resultados actuales e históricos están en [pruebas](pruebas.md).

| ID | Problema/objetivo y solución implementada | Evidencia / pruebas | Estado |
|---|---|---|---|
| H1 | Evitar revelar una cita ajena al reconstruir Atenciones: NotFound/Forbid antes de ViewData. | `52bdcbb`; `AtencionesControllerTests` | Cerrado |
| H2 | Revocar sesiones tras desactivación/cambio de rol: SecurityStamp y validación cada minuto. | `250b957`; `SessionRevocationTests`, `IdentityConfigurationTests`, `UsuariosControllerTests` | Cerrado |
| H3 | Evitar sobrescrituras concurrentes: actualizaciones condicionadas y comprobación de filas en citas, atenciones y tratamientos. | `77e2920`; `EstadoConcurrencyTests` | Cerrado |
| H4 | Evitar doble reserva del mismo inicio: índice único filtrado y manejo específico del conflicto. | `de6db31`; migración de agenda, `CitasScheduleTests` | Cerrado |
| H5 | Validar nacimiento y sexo, normalizar embarazo y preservar reglas de admisión. | `ceb0c59`; referencia H5 en `PacientesControllerTests` | Cerrado |
| H6 | Rechazar correo inválido antes de crear usuario y normalizar espacios exteriores. | `66ca327`; `UsuarioCreateViewModel`, `UsuariosControllerTests` | Cerrado |
| H7 | Evitar usuarios/roles parcialmente guardados: transacciones en Create/Edit y rollback. | `0fac10a`; referencia H7 y fallos intermedios en `UsuariosControllerTests` | Cerrado |
| H8 | Evitar solapamientos: intervalos, snapshots y transacción Serializable. | `022648d`; `CitasOverlapTests`, `CitasSqlServerOverlapTests` opcional | Cerrado |
| H9 | Evitar respuestas obsoletas de autocomplete: cancelación, debounce y guardas de vigencia. | `49779ce`; vistas Citas Create/Index y registro manual en `pruebas.md` | Cerrado |
| H10 | Formalizar antecedentes de admisión y permisos sin ampliar acceso clínico. | `7dadda6`; `PacientesAdmisionTests`, `AuthorizationIntegrationTests` | Cerrado |
| H11 | Evitar consulta de roles por usuario: carga agrupada y acotada a los IDs del listado. | `a055522`; `UsuariosControllerTests` de Index | Cerrado |
| H12 | Evitar cargar antecedentes en listado: Pacientes Index sin Include y sin tracking. | `5945525`; `PacientesControllerTests` de Index | Cerrado |
| H13 | Consultar citas históricas de pacientes inactivos: `BuscarPacientesParaFiltro` separado de Create. | `f88a9dd`; `CitasControllerTests`, vista Index | Cerrado |
| H14 | Corregir inicio y retirar plantilla: Login/Dashboard, Privacy 404 y Error controlado. | `23b463b`; `HomeFlowTests` | Cerrado |
| H15 | Controlar duplicados concurrentes de seguros sin ocultar errores ajenos. | `e578d76`; `SegurosControllerTests` | Cerrado |
| H16 | Fijar dependencias y registrar su resolución en los tres lock files. | `b954964`; `.csproj`, `packages.lock.json`; validación mediante `dotnet restore --locked-mode` | Cerrado |
