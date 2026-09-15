# MSDentalSys

## Descripción

MSDentalSys es un sistema web de gestión clínica odontológica desarrollado para apoyar las operaciones administrativas y clínicas básicas de una clínica dental.

## Objetivo del sistema

El sistema busca centralizar la gestión de pacientes, citas, servicios odontológicos y usuarios internos, además del control de acceso y la consulta de información operativa.

## Tecnologías utilizadas

- ASP.NET Core MVC
- .NET 9
- Entity Framework Core 9.0.20
- SQL Server
- ASP.NET Core Identity
- Razor Views
- HTML, CSS y JavaScript
- xUnit
- SQLite InMemory para pruebas aisladas
- Microsoft.AspNetCore.Mvc.Testing para pruebas de integración HTTP

## Arquitectura

La solución está organizada en tres proyectos:

- `MSDentalSys.Data`: contexto de Entity Framework Core, entidades, migraciones y datos iniciales.
- `MSDentalSys.Web`: aplicación ASP.NET Core MVC, controladores, ViewModels, vistas y recursos web.
- `MSDentalSys.Tests`: pruebas unitarias, de integración y de infraestructura.

```text
MSDentalSys/
├── MSDentalSys.sln
├── global.json
├── src/
│   ├── MSDentalSys.Data/
│   └── MSDentalSys.Web/
├── tests/
│   └── MSDentalSys.Tests/
└── docs/prototipos/
```

## Roles del sistema

Los roles definidos son `Administrador`, `Odontologo` y `Recepcionista`.

### Administrador

Puede gestionar completamente los pacientes, citas, servicios, usuarios y el catálogo de seguros médicos, además de consultar las estadísticas generales del sistema. También puede acceder a los módulos clínicos según los permisos establecidos.

### Recepcionista

Puede consultar, registrar y editar administrativamente los pacientes; también puede registrar y gestionar administrativamente citas, consultar servicios y acceder a las estadísticas generales. No puede administrar usuarios ni seguros médicos.

### Odontologo

Puede consultar pacientes y servicios, consultar sus citas y actualizar los estados clínicos permitidos de una cita. No puede crear ni editar pacientes ni administrar seguros médicos. También puede registrar atenciones, diagnósticos, tratamientos y evoluciones clínicas únicamente para sus atenciones asignadas. El Dashboard filtra sus estadísticas de citas por odontólogo, mientras que el total de pacientes activos es global. No puede administrar usuarios ni crear o reagendar citas administrativamente.

## Módulos implementados

- Autenticación y cierre de sesión.
- Dashboard dinámico.
- Pacientes.
- Citas.
- Servicios odontológicos.
- Subservicios odontológicos: duración estimada, clasificación y código de catálogo.
- Administración de usuarios.
- Seguros médicos.
- Atención odontológica.
- Diagnósticos.
- Tratamientos.
- Evoluciones clínicas.

El flujo clínico implementado es:

```text
Cita
  → Atención odontológica
      → Diagnósticos
      → Tratamientos
      → Evoluciones clínicas
```

## Reglas de negocio importantes

### Pacientes

- La activación y desactivación es lógica; el registro no se elimina físicamente.
- Solo el Administrador activa o desactiva pacientes. Administrador y Recepcionista gestionan su admisión, incluidos los antecedentes básicos; Odontologo puede consultarlos.
- Se rechaza una fecha de nacimiento futura. Sexo admite `Femenino`, `Masculino`, `Otro` o ausencia; Embarazo se normaliza a NULL cuando no corresponde a `Femenino`.
- Para pacientes de 18 años o más, la cédula es obligatoria.
- Para pacientes menores de 18 años, la cédula es opcional.
- La cédula contiene 11 dígitos y se guarda como `XXX-XXXXXXX-X`. Create/Edit agregan los guiones automáticamente; el servidor acepta también los 11 dígitos sin guiones, valida estructura y obligatoriedad por edad, normaliza y comprueba unicidad considerando ambas representaciones. Si no hay fecha de nacimiento, no se infiere mayoría de edad. No se verifica el dígito de control ni la existencia oficial.
- Si un menor informa cédula, se aplican las validaciones de formato y unicidad.
- Un paciente puede tener o no seguro médico; si tiene uno, debe seleccionarse un seguro válido del catálogo.
- Los seguros inactivos no se utilizan para nuevas asociaciones.
- Edit permite conservar el seguro histórico del paciente aunque esté inactivo.
- Los seguros no se eliminan físicamente; se administran mediante activación y desactivación.

### Citas

Los estados utilizados son `Pendiente`, `Confirmada`, `Atendida`, `Cancelada` y `No asistió`.

`Cancelada` y `Atendida` son estados finales. El sistema evita conflictos de horario para un mismo odontólogo y una cita cancelada no bloquea ese horario.

Create busca únicamente pacientes activos mediante `BuscarPacientes`; Index utiliza `BuscarPacientesParaFiltro`, que incluye activos e inactivos para consulta histórica. Una cita solo pasa a `Atendida` al registrar la atención; el odontólogo únicamente puede marcar directamente `No asistió` en sus citas.

### Usuarios

- Al crear usuarios se permiten los roles `Odontologo` y `Recepcionista`.
- El administrador inicial está protegido frente a desactivación y cambio de rol.
- La activación y desactivación de usuarios es lógica.
- Create valida el correo y elimina espacios exteriores antes de guardarlo como Email/UserName; Edit conserva el correo existente. La creación con su rol y la edición con sus cambios de roles/SecurityStamp son unidades transaccionales.

### Servicios

- Los servicios pueden activarse y desactivarse lógicamente.
- El servicio principal agrupa procedimientos y administra nombre/descripción. La duración se configura en cada subservicio y se copia como snapshot al crear la cita; la columna de duración del servicio se conserva únicamente como legacy, sin edición en la interfaz.

### Atención odontológica

- Una cita puede tener cero o una atención odontológica.
- Una atención conserva el paciente y el odontólogo asignados a la cita.
- Una atención puede registrar múltiples diagnósticos, tratamientos y evoluciones clínicas.
- Los diagnósticos, tratamientos y evoluciones no se eliminan físicamente desde los módulos clínicos.

### Tratamientos

- Solo se pueden asociar servicios odontológicos activos.
- Los estados permitidos son `Planificado`, `En progreso` y `Completado`.
- Un tratamiento completado no vuelve a un estado anterior.

## Base de datos

En ejecución normal, la aplicación utiliza SQL Server mediante Entity Framework Core. El acceso se centraliza en `ApplicationDbContext`. El proyecto `MSDentalSys.Data` contiene las migraciones existentes y `ApplicationDbContextFactory` permite crear el contexto para operaciones de design-time.

Los ejemplos de conexión no contienen credenciales; las contraseñas y secretos se configuran fuera del repositorio.

## Configuración

La configuración general se encuentra en `appsettings.json` y `appsettings.Development.json`. Los datos sensibles se gestionan mediante User Secrets cuando corresponde. No se incluyen valores secretos en el repositorio.

## Configuración local para desarrollo

La aplicación obtiene la conexión de base de datos mediante `ConnectionStrings:DefaultConnection`. `Program.cs` requiere ese valor para iniciar la aplicación. El repositorio puede incluir una conexión base sin credenciales, pero cada desarrollador puede sobrescribirla localmente mediante User Secrets. Los User Secrets no se almacenan ni se transfieren mediante Git.

Para configurar una conexión local, sustituye `TU_SERVIDOR` por el nombre de la instancia SQL Server instalada en tu equipo:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=TU_SERVIDOR;Database=MSDentalSysDB;Trusted_Connection=True;TrustServerCertificate=True;" --project .\src\MSDentalSys.Web\MSDentalSys.Web.csproj
dotnet user-secrets set "SeedAdmin:Password" "TU_CLAVE_SEGURA" --project .\src\MSDentalSys.Web\MSDentalSys.Web.csproj
```

Algunos nombres de servidor posibles son `.\SQLEXPRESS`, `localhost` y `(localdb)\MSSQLLocalDB`; no todos funcionaran automaticamente. Cada integrante debe utilizar el nombre de instancia SQL Server que tenga instalado. Si una rama o copia del proyecto no contiene una conexion base, debe configurarse `ConnectionStrings:DefaultConnection` mediante User Secrets antes de ejecutar la aplicacion.

## Flujo de migraciones para el equipo

Ejecuta estos comandos PowerShell desde la raíz de la solución. Se requiere un SDK compatible con `global.json` (9.0.316) y `dotnet-ef` 9. Comprueba la herramienta con `dotnet ef --version`; si falta, instala mediante `dotnet tool install --global dotnet-ef --version "9.0.*"`.

Web y EF CLI utilizan `ConnectionStrings:DefaultConnection`. La fábrica carga, en prioridad creciente, `appsettings.json`, `appsettings.{Environment}.json`, User Secrets de Web en Development, variables de entorno y argumentos. La variable `ConnectionStrings__DefaultConnection` permite sobrescribir la conexión. No necesitas editar la fábrica para cambiar de instancia.

El entorno procede de `--environment`, después `DOTNET_ENVIRONMENT`, después `ASPNETCORE_ENVIRONMENT`; por defecto es Production. EF CLI no depende de `launchSettings.json`: indica Development para cargar User Secrets. La fábrica busca el proyecto Web desde el directorio actual y las ubicaciones de ejecución/ensamblado, ascendiendo por el repositorio. Puedes indicar su carpeta mediante `--contentRoot RUTA_A_WEB` después de `--`. Este mecanismo requiere el proyecto fuente.

### Recibir y aplicar migraciones

Configura primero los User Secrets de conexión y `SeedAdmin:Password` con los comandos anteriores. Cada integrante conserva sus valores fuera de Git.

```powershell
git pull
dotnet ef database update --project .\src\MSDentalSys.Data --startup-project .\src\MSDentalSys.Web --context ApplicationDbContext -- --environment Development
dotnet run --project .\src\MSDentalSys.Web -- --environment Development
```

Aplicar migraciones existentes es necesario al preparar una base nueva y al recibir cambios de esquema. EF registra las aplicadas en `__EFMigrationsHistory`; no requiere una copia física de otra base. La aplicación no aplica migraciones automáticamente.

Al iniciar Web se ejecutan únicamente `RoleSeeder`, `AdminSeeder` y `SeguroSeeder`, salvo en Testing. El esquema debe existir primero. `SeedAdmin:Password` se necesita para crear el administrador si todavía no existe; no cambia su contraseña si ya existe. La fábrica de EF CLI no ejecuta seeders.

Las seis migraciones actuales crean/evolucionan el esquema, pero no insertan el catálogo odontológico definitivo. Una instalación nueva tampoco lo recibe mediante startup. Los cargadores definitivos internos requieren las identidades históricas del proyecto y no son instaladores genéricos desde cero. Véase [datos iniciales y alcance de instalación](docs/arquitectura.md#datos-iniciales).

### Generar una migración

Solo quien cambia el modelo genera una migración, después de integrar los cambios del equipo:

```powershell
dotnet ef migrations add NombreDescriptivoDelCambio --project .\src\MSDentalSys.Data --startup-project .\src\MSDentalSys.Web --context ApplicationDbContext --output-dir Migrations -- --environment Development
```

Revisa `Up()` y `Down()`, aplica la migración localmente con el comando anterior y verifica el resultado. Comparte en Git los cambios del modelo, el archivo de migración, su `.Designer.cs` y `ApplicationDbContextModelSnapshot.cs` juntos.

- No generes otra migración para un cambio que ya llegó mediante Git.
- No modifiques migraciones compartidas/aplicadas salvo una decisión consciente del equipo. Coordina cambios simultáneos y conflictos del snapshot.
- Las migraciones representan tablas, columnas, relaciones e índices. Pacientes, citas, diagnósticos, tratamientos y otros datos operativos no se comparten mediante migraciones.
- Los archivos físicos y respaldos SQL Server (`.mdf`, `.ldf`, `.ndf`, `.bak`) no se suben a Git.
- Los seeders mantienen datos iniciales controlados; no copian los datos operativos de otro integrante.

Para listar migraciones sin conectarse a SQL Server:

```powershell
dotnet ef migrations list --no-connect --project .\src\MSDentalSys.Data --startup-project .\src\MSDentalSys.Web --context ApplicationDbContext -- --environment Development
```

## Ejecución

Se requiere una conexión SQL Server correctamente configurada para ejecutar la aplicación normalmente.

Las dependencias directas están fijadas en los `.csproj`; EF Core e Identity utilizan 9.0.20. Data, Web y Tests versionan sus `packages.lock.json`. La restauración puede validarse con `dotnet restore --locked-mode`; ese modo no está habilitado globalmente. Las versiones y su mantenimiento se describen en [arquitectura](docs/arquitectura.md#dependencias-y-restauración).

```powershell
dotnet restore --locked-mode
dotnet build .\MSDentalSys.sln
dotnet run --project .\src\MSDentalSys.Web\MSDentalSys.Web.csproj
```

## Pruebas automatizadas

La solución cuenta con pruebas para los módulos administrativos y clínicos, Login/autenticación, autorización HTTP e infraestructura.

Estado final validado: **542 pruebas aprobadas, 0 fallidas y 1 teoría H8 SQL Server omitida** en la ejecución estándar cuando no está configurada `MSDENTALSYS_H8_SQLSERVER`. Los resultados de etapas anteriores se conservan en [pruebas](docs/pruebas.md).

La suite estándar utiliza SQLite InMemory y no utiliza `MSDentalSysDB`. Las pruebas HTTP usan `WebApplicationFactory` en `Testing`: claims controlados para autorización y cookies reales de Identity para revocación de sesiones, siempre con datos de pruebas aislados. H8 dispone además de una prueba SQL Server opcional con configuración explícita.

```powershell
dotnet test .\MSDentalSys.sln
```

## Seguridad

ASP.NET Core Identity bloquea temporalmente la cuenta durante 60 segundos al alcanzar cinco intentos fallidos consecutivos de inicio de sesión. La política aplica a todos los usuarios internos (Administrador, Odontologo y Recepcionista) con `LockoutEnabled` habilitado. Un acceso correcto antes del límite reinicia el contador; durante el bloqueo se rechaza incluso la contraseña correcta. Las cuentas nuevas creadas mediante UserManager tienen el bloqueo habilitado; esta configuración no corrige cuentas históricas que lo tengan deshabilitado.

La contraseña exige al menos ocho caracteres, dígito, mayúscula, minúscula y carácter no alfanumérico. `Program.cs` no exige cuenta confirmada. Una desactivación efectiva o un cambio efectivo de rol renueva `SecurityStamp`; las cookies se revalidan periódicamente cada minuto, no de forma instantánea en cada petición. Reactivar conserva el stamp renovado, sin restaurar las cookies anteriores. Véase [seguridad y autorización](docs/arquitectura.md#seguridad-y-autorización).

- ASP.NET Core Identity gestiona usuarios y contraseñas.
- La autorización se define mediante `[Authorize]` y roles.
- Las acciones POST utilizan protección antiforgery cuando corresponde.
- Las desactivaciones son lógicas.
- El administrador inicial está protegido por reglas específicas del sistema.
- La autorización clínica valida el rol y, para el odontólogo, la asignación de la atención odontológica.
- Los módulos clínicos no realizan eliminación física de atenciones, diagnósticos, tratamientos ni evoluciones.

## Estado actual del proyecto

El alcance funcional está cerrado. La [matriz H1–H16](docs/arquitectura.md#matriz-de-cierre-h1h16) identifica las correcciones y su evidencia; [pruebas](docs/pruebas.md) distingue el resultado final de los hitos históricos. Este cierre no certifica el contenido ni el despliegue de una BD concreta.

La raíz `/` redirige al anónimo a Login y al autenticado a Dashboard. `/Home/Privacy` fue retirado y devuelve 404; `/Home/Error` se conserva para manejo controlado de errores.

## Subservicios y catálogo odontológico

Desde el detalle de un servicio se consulta su catálogo de procedimientos. Solo el Administrador puede crear, editar nombre/descripción/duración/clasificación y activar o desactivar subservicios; el padre permanece fijo. Recepcionista y Odontologo tienen consulta.

Create y Edit requieren Principal o Complementario; los registros históricos pueden permanecer «Sin clasificar» hasta su edición. `CodigoCatalogo` es una identidad técnica nullable, única cuando existe y no editable en el formulario. La evolución del esquema y las fases anteriores se explican en [arquitectura](docs/arquitectura.md#subservicios-odontológicos--fase-a).

El seeder histórico conserva 45 procedimientos provisionales y sus datos originales. Desde Fase 2A su carga administrativa está deshabilitada y el botón fue retirado; no constituye el catálogo definitivo ni se ejecuta al arrancar.

Los cargadores internos de servicios y procedimientos permiten conciliación controlada sobre las identidades históricas previstas. No tienen endpoint web ni se ejecutan en startup. Sus precondiciones, transacciones y reglas legacy se detallan en arquitectura.

Cita tiene dos columnas nullable (SubservicioOdontologicoId y DuracionProgramadaMinutos), sin completar datos históricos. Desde Fase B, las nuevas citas requieren un subservicio activo del servicio activo seleccionado; su duración se copia desde BD como snapshot. Reagendar conserva servicio, subservicio y snapshot aunque cambie el catálogo. Details muestra «No registrado» para datos históricos nulos. H8 está implementado (véase la sección H8); H3/H4 se conservan. La duración del servicio principal permanece como legacy en entidad/BD y no se administra ni muestra en Servicios. El flujo de tratamientos no cambia. No existe componente económico.

### Catálogo fuente y registro histórico de implantación

SubservicioCatalogo define explícitamente 139 procedimientos de los 12 servicios: 85 Principal y 54 Complementario, con códigos permanentes MSCD-PROC-0001 a MSCD-PROC-0139. Los nombres y clasificación provienen del catálogo definido para la clínica; descripciones y duraciones son configuración funcional del sistema. Las duraciones corresponden a bloques operativos utilizados para la programación de citas y no representan tiempos clínicos obligatorios.

Partiendo de los cinco procedimientos históricos compatibles, `SubservicioCatalogoSeeder` reutiliza IDs 1 y 3 como MSCD-PROC-0001/0076 y crea 137 filas: 139 definitivos activos y tres legados inactivos, 142 en total. Los IDs legacy 2/4/5 conservan su clasificación existente, sin exigir NULL, y quedan inactivos. La conciliación rechaza registros ajenos y no mezcla el catálogo provisional de 45.

La documentación histórica registra una implantación en la BD utilizada durante esa etapa; no permite determinar el contenido actual de cualquier BD operativa. El catálogo fuente versionado y las pruebas de conciliación son verificables en Git; una instalación nueva requiere preparación adicional que los cargadores actuales no resuelven desde cero. Se retiraron PrepareCatalog, PrepareDefinitiveCatalog y LoadInitialCatalog; la operación cotidiana utiliza el CRUD normal. Aquel cierre no implementó H8: H8 se implementó y cerró posteriormente.

## H8 — prevención de solapamientos

Create y Reagendar comprueban intervalos `[inicio, fin)` por odontólogo: `nuevaInicio < existenteFin && existenteInicio < nuevaFin`. Horarios contiguos se permiten; todos los estados excepto Cancelada ocupan agenda. El conflicto muestra: «El horario seleccionado se superpone con otra cita del odontólogo. Selecciona una hora diferente.»

La consulta y escritura se ejecutan en una transacción Serializable. Se conservan H3, el índice H4 `UX_Citas_Odontologo_FechaHoraInicio_NoCancelada` y los snapshots. Una duración histórica NULL solo permite detectar conflicto por inicio idéntico; no se infiere duración ni se rellenan históricos. No hay migración ni capas adicionales. SQL Server 1205 devuelve un mensaje de agenda ocupada sin retry automático. La validación concurrente real requiere configuración explícita de un servidor de pruebas; véase [pruebas](docs/pruebas.md).

## Autor / contexto académico

Proyecto desarrollado como parte del monográfico para optar por el título de Licenciatura en Informática en la Universidad Autónoma de Santo Domingo (UASD).
