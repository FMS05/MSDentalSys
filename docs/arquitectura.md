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
| Usuarios | `UsuariosController` | `UsuarioCreateViewModel`, `UsuarioEditViewModel`, `UsuarioDetailsViewModel`, `UsuarioListItemViewModel`, `UsuariosIndexViewModel` | Administración de usuarios, roles permitidos y estado activo. |
| Seguros | `SegurosController` | `SeguroFormViewModel` | Catálogo de seguros médicos y su activación. |
| Atenciones odontológicas | `AtencionesController` | `AtencionOdontologicaCreateViewModel` | Creación desde una cita y consulta de la atención con sus registros clínicos. |
| Diagnósticos | `DiagnosticosController` | `DiagnosticoCreateViewModel` | Registro de diagnósticos asociados a una atención. |
| Tratamientos | `TratamientosController` | `TratamientoCreateViewModel` | Registro de tratamientos asociados a servicios y actualización de sus estados. |
| Evoluciones clínicas | `EvolucionesClinicasController` | `EvolucionClinicaCreateViewModel` | Registro de evoluciones asociadas a una atención. |

`HomeController` proporciona soporte general de inicio, privacidad y errores; la vista de error utiliza `ErrorViewModel`. El catálogo de seguros registra nombres y estado, sin modelar coberturas, pólizas, reclamaciones ni facturación.

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

## Reglas de aplicación relevantes

- Pacientes, servicios, usuarios y seguros utilizan activación y desactivación lógica.
- El odontólogo está restringido a sus citas y atenciones asignadas, incluidos diagnósticos, tratamientos y evoluciones. La actualización directa de una cita por este rol solo permite marcar `No asistió`.
- `AtencionesController` crea la atención conservando el paciente y el odontólogo de la cita y cambia su estado a `Atendida` dentro de una transacción. La actualización administrativa de estados no permite establecer `Atendida` directamente. `Cancelada` y `Atendida` son estados finales.
- La cédula es obligatoria desde los 18 años y opcional para menores. Sin `FechaNacimiento` no se infiere mayoría de edad. El servidor valida también la cédula voluntaria, acepta once dígitos o `XXX-XXXXXXX-X`, normaliza a ese formato y comprueba unicidad considerando ambas representaciones, incluidos registros históricos sin guiones.

## Persistencia y relaciones principales

`src/MSDentalSys.Data/Context/ApplicationDbContext.cs` hereda de `IdentityDbContext<ApplicationUser>`. Declara los siguientes `DbSet`: `Pacientes`, `AntecedentesClinicos`, `ServiciosOdontologicos`, `Citas`, `AtencionesOdontologicas`, `Diagnosticos`, `EvolucionesClinicas`, `Tratamientos` y `Seguros`. Identity administra las tablas de usuarios, roles y sus asociaciones a través del mismo contexto.

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

El flujo del equipo es: integrar cambios del repositorio → cambiar el modelo cuando corresponda → generar y revisar la migración → compartir modelo, migración, `.Designer.cs` y `ApplicationDbContextModelSnapshot.cs` mediante Git → los demás integrantes actualizan el repositorio y aplican las migraciones recibidas a su SQL Server local. No deben generar otra migración para un cambio ya recibido.

Cada base registra las migraciones aplicadas en `__EFMigrationsHistory`, mediante `MigrationId` y `ProductVersion`. El snapshot es la referencia del modelo para generar cambios, no el historial de una base local. La aplicación no aplica migraciones automáticamente. Las migraciones compartidas representan la evolución del esquema; los datos operativos y el archivo físico de la base permanecen en cada entorno. Los secretos se configuran localmente y no se comparten por Git.

El procedimiento y los comandos están en [Flujo de migraciones para el equipo](../README.md#flujo-de-migraciones-para-el-equipo).

### Datos iniciales

`Program.cs` ejecuta los seeders de `src/MSDentalSys.Data/InitialData` al iniciar Web, salvo en el entorno `Testing`. El esquema debe estar preparado previamente.

| Seeder | Responsabilidad |
|---|---|
| `RoleSeeder` | Crea los tres roles si no existen, mediante `RoleManager`. |
| `AdminSeeder` | Crea el administrador inicial si falta y asegura su rol mediante `UserManager`. Obtiene la contraseña inicial de configuración; no cambia la de un usuario ya existente. |
| `SeguroSeeder` | Incorpora de forma idempotente los nombres del catálogo inicial de seguros y conserva registros manuales. |

La procedencia del catálogo se documenta en [seguros.md](seguros.md).

## Componentes cliente

`src/MSDentalSys.Web/wwwroot/js/pacientes-form.js` se comparte entre Create y Edit mediante `Views/Pacientes/_PacienteForm.cshtml`. Gestiona el formato automático de cédula, su obligatoriedad según `FechaNacimiento`, la selección de seguro y la presentación del campo embarazo. Integra el apoyo de validación cliente; la validación definitiva permanece en el servidor.

Las vistas `Views/Citas/Create.cshtml` y `Views/Citas/Index.cshtml` incluyen JavaScript para autocomplete de pacientes y selección del filtro, respectivamente, mediante `BuscarPacientes`. `Views/Account/Login.cshtml` permite mostrar u ocultar la contraseña.

El layout utiliza Bootstrap y jQuery, y los formularios incorporan jQuery Validation/Unobtrusive mediante el parcial de validación. `wwwroot/js/site.js` está incluido en el layout, pero actualmente solo contiene comentarios de plantilla.

## Estrategia de pruebas

La solución contiene **212 pruebas** con xUnit. Su organización en `tests/MSDentalSys.Tests` es:

| Ubicación | Alcance |
|---|---|
| `Controllers/` | Reglas administrativas y clínicas, validación, persistencia y autenticación; utiliza contextos SQLite en memoria cuando requiere datos. |
| `Integration/` | Integración HTTP, autorización, HTML de formularios y opciones reales de Identity mediante `CustomWebApplicationFactory`. |
| `Context/` | Configuración de `ApplicationDbContextFactory`, prioridades y errores, sin abrir conexiones SQL Server. |
| `InitialData/` | Comportamiento de `SeguroSeeder`, idempotencia y conservación de datos manuales. |
| `InfrastructureTests.cs` | Comprobación básica de la infraestructura xUnit. |

`CustomWebApplicationFactory` utiliza `WebApplicationFactory`, fuerza `Testing`, reemplaza SQL Server por SQLite en memoria y prepara el esquema con `EnsureCreated`. La autenticación HTTP usa claims controlados y los seeders de arranque se omiten. Las pruebas de Login ejercitan servicios Identity con persistencia aislada.

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

POST Create verifica en BD servicio activo, subservicio activo, pertenencia exacta y duración entre 1 y 1440 minutos, además de paciente, odontólogo y conflicto H4. Copia la duración del catálogo a DuracionProgramadaMinutos; el ViewModel no admite duración del cliente. Un POST inválido reconstruye las opciones del servicio y conserva únicamente selecciones válidas. Las citas históricas pueden conservar NULL; Details muestra «No registrado».

El script dedicado citas-subservicios.js mantiene separado el selector dependiente del autocomplete existente: limpia la selección al cambiar servicio, cancela con AbortController y comprueba tanto la petición vigente como el servicio actual antes de aplicar resultados o errores. Index conserva sus seis columnas; el procedimiento y snapshot se consultan en Details.

Reagendar solo cambia fecha/hora y conserva servicio, subservicio y snapshot incluso después de modificar el catálogo. No hay nueva migración. H3/H4 permanecen intactos: HasScheduleConflictAsync sigue usando mismo odontólogo + mismo inicio + estado distinto de Cancelada. H8 y la detección de intervalos siguen pendientes.

El seeder provisional histórico requiere sus 10 padres originales y conserva sus reglas transaccionales. Tras el cierre de implantación no tiene botón ni acción LoadInitialCatalog; permanece únicamente como infraestructura legacy. No se ejecuta en Program.

El catálogo provisional legacy contiene exactamente 45 subservicios. Sus duraciones son parámetros operativos de MSDentalSys, no información oficial clínica. CodigoCatalogo es una identidad técnica nullable, única cuando está presente y no editable desde el formulario. Permite reconocer entradas renombradas y no recrearlas. La carga vincula registros coincidentes existentes sin modificar nombre, descripción, duración o estado; conserva registros manuales. Los códigos existentes no deben renumerarse en cambios futuros del catálogo. La carga controlada debe ejecutarse por un administrador a la vez.

La migración AddSubserviciosOdontologicos debe aplicarse mediante el procedimiento habitual de despliegue antes de utilizar el catálogo; el seeder no ejecuta migraciones ni EnsureCreated. No existe componente de precios, costos, tarifas ni facturación.

### Clasificación de subservicios — Fase 1

ClasificacionSubservicio se define en Data/Models con Principal = 1 y Complementario = 2. SubservicioOdontologico.Clasificacion es nullable y se convierte a entero; CK_Subservicios_Clasificacion permite únicamente NULL, 1 o 2. AddClasificacionToSubservicios agrega columna y CHECK sin default, backfill ni cambios a relaciones, índices o catálogo.

El ViewModel requiere clasificación y SubserviciosController valida explícitamente los dos valores permitidos tanto en Create como en Edit. Edit GET permite históricos NULL; guardar exige clasificarlos. El selector compartido en _Fields sirve a Create/Edit y Details muestra «Sin clasificar» para NULL. Index conserva sus cinco columnas para no ensanchar la tabla compartida con Details de Servicios; la clasificación se consulta en Details del subservicio.

El seeder provisional permanece intacto y crea sus registros con NULL; no se asigna clasificación masivamente. Activación, autorización y selección para Citas conservan su comportamiento, sin filtros por clasificación. No cambian duración, códigos, FK compuesta, snapshot de citas, Reagendar ni H3/H4. Esta fase no carga procedimientos; H8 sigue pendiente.

### Servicios definitivos — Fase 2A

ServicioCatalogoSeeder contiene las 12 categorías objetivo. La acción temporal Servicios/PrepareCatalog fue retirada al cerrar la implantación; el seeder permanece interno. No hay ejecución en startup ni migración. Valida los IDs 1–5 contra sus nombres históricos o definitivos permitidos; no usa coincidencias aproximadas. Para los siete restantes admite el nombre objetivo con diferencias de mayúsculas o espacios exteriores. Rechaza coincidencias múltiples incluso inactivas, IDs históricos ausentes/reutilizados y cualquier servicio ajeno al conjunto objetivo. Este último caso requiere revisión explícita, sin eliminar datos.

Tras validar todo, conserva IDs, fechas, descripciones y duración legacy; normaliza nombres y activa los objetivos, creando solo los ausentes con los defaults de la entidad. La segunda ejecución devuelve que el catálogo ya está actualizado. Se utiliza transacción Serializable desde la lectura hasta el commit para impedir duplicados entre cargas concurrentes; un conflicto de BD aborta con mensaje controlado. Ante cualquier fallo se hace rollback y se limpia el tracking. No se modifican relaciones, subservicios, citas ni tratamientos.

La conciliación conserva 1 Periodoncia, 2 Odontología general, 3 Endodoncia, 4 Cirugía oral y 5 Rehabilitación oral / Prótesis. Completa con Odontología estética, Implantología, Ortodoncia, Odontopediatría, Odontología preventiva, Odontología digital y Odontología para pacientes con necesidades especiales. Esta operación prepara solamente servicios; la Fase 2B incorpora el cargador separado de procedimientos.

### Procedimientos definitivos — Fase 2B

Data/InitialData/SubservicioCatalogo.cs contiene 139 entradas inmutables con códigos explícitos permanentes, padre, nombre, descripción, clasificación y minutos. No se generan códigos a partir de la posición. SubservicioCatalogoSeeder valida 139 códigos únicos y completos, 85 Principal/54 Complementario, nombres/descripciones con límites del modelo, pares padre/nombre únicos y duración 1–1440 antes de iniciar la carga.

La transacción Serializable abarca lectura, validación, conciliación y commit. Comprueba los 12 padres activos de Fase 2A, incluidos sus IDs históricos; valida subservicios 1–5, códigos y colisiones de nombre mediante consultas con la collation de BD, incluidos inactivos. IDs 1/3 solo aceptan su identidad histórica aprobada o su código definitivo en el mismo ID. IDs 2/4/5 deben conservar nombre, padre, duración, clasificación NULL y código NULL; solo se inactivan. Registros ajenos al conjunto aprobado provocan aborto para evitar mezclar catálogos. Ante fallo se revierte todo y se limpia el tracking.

Para códigos ya conocidos se exige padre y nombre compatibles: no se trasladan códigos ni se cambian IDs. La política conservadora rechaza diferencias de nombre; descripción, clasificación, minutos y estado pueden reconciliarse. Las dos conversiones históricas autorizadas sí cambian nombre y asignan código. Se conservan fechas e IDs; no hay actualizaciones de Cita ni Tratamiento. El nombre mostrado en citas históricas refleja el nuevo nombre del catálogo en IDs 1/3; su snapshot de duración permanece intacto.

El catálogo definitivo ya está implantado. Se retiraron las acciones PrepareCatalog, PrepareDefinitiveCatalog y LoadInitialCatalog, sus botones y textos auxiliares. ServicioCatalogoSeeder, SubservicioCatalogoSeeder y SubservicioCatalogo permanecen internos para pruebas, reconstrucción controlada y futura instalación. La gestión cotidiana utiliza el CRUD normal; no hay endpoint alternativo de carga ni ampliación de permisos. No se ejecuta desde Program, no hay migración, SQL manual ni eliminación física. Las duraciones son bloques operativos de agenda, no tiempos clínicos obligatorios. Las relaciones, selección Servicio → Subservicio, Reagendar y H3/H4 no cambian; H8 continúa pendiente.
