# MSDentalSys

## Descripción

MSDentalSys es un sistema web de gestión clínica odontológica desarrollado para apoyar las operaciones administrativas y clínicas básicas de una clínica dental.

## Objetivo del sistema

El sistema busca centralizar la gestión de pacientes, citas, servicios odontológicos y usuarios internos, además del control de acceso y la consulta de información operativa.

## Tecnologías utilizadas

- ASP.NET Core MVC
- .NET 9
- Entity Framework Core 9
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

Puede gestionar pacientes, citas, servicios y usuarios, además de consultar las estadísticas generales del sistema. También puede acceder a los módulos clínicos y gestionar la información de atención odontológica.

### Recepcionista

Puede consultar pacientes, registrar y gestionar administrativamente citas, consultar servicios y acceder a las estadísticas generales. No puede administrar usuarios.

### Odontologo

Puede consultar pacientes y servicios, consultar sus citas y actualizar los estados clínicos permitidos de una cita. También puede registrar atenciones, diagnósticos, tratamientos y evoluciones clínicas únicamente para sus atenciones asignadas. El Dashboard filtra sus estadísticas de citas por odontólogo, mientras que el total de pacientes activos es global. No puede administrar usuarios ni crear o reagendar citas administrativamente.

## Módulos implementados

- Autenticación y cierre de sesión.
- Dashboard dinámico.
- Pacientes.
- Citas.
- Servicios odontológicos.
- Administración de usuarios.
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
- La cédula es opcional.
- La cédula es única cuando está informada.

### Citas

Los estados utilizados son `Pendiente`, `Confirmada`, `Atendida`, `Cancelada` y `No asistió`.

`Cancelada` y `Atendida` son estados finales. El sistema evita conflictos de horario para un mismo odontólogo y una cita cancelada no bloquea ese horario.

### Usuarios

- Al crear usuarios se permiten los roles `Odontologo` y `Recepcionista`.
- El administrador inicial está protegido frente a desactivación y cambio de rol.
- La activación y desactivación de usuarios es lógica.

### Servicios

- Los servicios pueden activarse y desactivarse lógicamente.
- Cada servicio puede registrar una duración estimada en minutos.

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

Las cadenas de conexión, contraseñas y secretos no forman parte de esta documentación.

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

`ApplicationDbContextFactory` se utiliza para operaciones de Entity Framework Core en tiempo de diseno y actualmente esta configurada para SQL Server local. Quienes utilicen otra instancia pueden necesitar revisar esa configuracion antes de ejecutar comandos de migracion. Las migraciones no forman parte de la instalacion normal.

## Ejecución

Se requiere una conexión SQL Server correctamente configurada para ejecutar la aplicación normalmente.

```powershell
dotnet restore
dotnet build .\MSDentalSys.sln
dotnet run --project .\src\MSDentalSys.Web\MSDentalSys.Web.csproj
```

## Pruebas automatizadas

La solución cuenta con pruebas para los módulos administrativos y clínicos, Login/autenticación, autorización HTTP e infraestructura.

Estado actual: **123 pruebas correctas**.

Las pruebas de datos utilizan SQLite InMemory y no utilizan `MSDentalSysDB`. Las pruebas HTTP usan `WebApplicationFactory` en el entorno `Testing`, con una base SQLite aislada y un esquema de autenticación exclusivo para Tests.

```powershell
dotnet test .\MSDentalSys.sln
```

## Seguridad

- ASP.NET Core Identity gestiona usuarios y contraseñas.
- La autorización se define mediante `[Authorize]` y roles.
- Las acciones POST utilizan protección antiforgery cuando corresponde.
- Las desactivaciones son lógicas.
- El administrador inicial está protegido por reglas específicas del sistema.
- La autorización clínica valida el rol y, para el odontólogo, la asignación de la atención odontológica.
- Los módulos clínicos no realizan eliminación física de atenciones, diagnósticos, tratamientos ni evoluciones.

## Estado actual del proyecto

Los módulos administrativos y clínicos indicados en esta documentación están implementados y validados mediante pruebas automatizadas. El proyecto se encuentra en una etapa avanzada y de cierre técnico; aún pueden existir mejoras funcionales, de usabilidad, documentación y despliegue antes de considerarlo completamente finalizado.

## Autor / contexto académico

Proyecto desarrollado como parte del monográfico para optar por el título de Licenciatura en Informática en la Universidad Autónoma de Santo Domingo (UASD).
