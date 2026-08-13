# Arquitectura de MSDentalSys

## Arquitectura general

MSDentalSys utiliza una arquitectura MVC organizada en proyectos separados por responsabilidad. La aplicación web consume la capa de datos y el proyecto de pruebas consume la aplicación y, cuando necesita probar directamente persistencia o entidades, también la capa de datos.

```text
MSDentalSys.Tests
        ↓
MSDentalSys.Web
        ↓
MSDentalSys.Data

MSDentalSys.Tests ───────→ MSDentalSys.Data
```

No existen referencias desde `MSDentalSys.Data` o `MSDentalSys.Web` hacia `MSDentalSys.Tests`.

## Responsabilidades por proyecto

### MSDentalSys.Data

Contiene la persistencia y el modelo de dominio relacionado con ella:

- `Context/ApplicationDbContext.cs`: contexto EF Core y configuración de relaciones, índices y restricciones.
- `Context/ApplicationDbContextFactory.cs`: creación del contexto para design-time.
- `Models/`: entidades persistentes, incluyendo pacientes, citas, servicios, usuarios y antecedentes clínicos.
- `Migrations/`: migraciones existentes de Entity Framework Core.
- `InitialData/`: seeders de roles y del administrador inicial.

### MSDentalSys.Web

Contiene la interfaz y la lógica de aplicación MVC:

- `Controllers/`: reciben solicitudes HTTP y coordinan las operaciones del sistema.
- `Models/ViewModels/`: modelos específicos para formularios y vistas; no sustituyen a las entidades persistentes.
- `Views/`: vistas Razor.
- `wwwroot/`: recursos estáticos.
- `Program.cs`: configuración de servicios, Identity, persistencia, middleware y rutas.
- `appsettings*.json`: configuración de la aplicación sin documentar aquí valores sensibles.

### MSDentalSys.Tests

Contiene las pruebas automatizadas:

- `Controllers/`: pruebas de las acciones de los controladores con contextos aislados.
- `Integration/`: pruebas HTTP con `WebApplicationFactory`, entorno `Testing` y autenticación de claims.
- `Data/` y `Models/`: espacios preparados para pruebas específicas de esas capas.
- `InfrastructureTests.cs`: validación mínima de la infraestructura xUnit.

## Separación de responsabilidades

- **Entidades persistentes**: representan los datos almacenados y sus relaciones en `MSDentalSys.Data.Models`.
- **ViewModels**: representan los datos que reciben formularios o que consumen vistas concretas.
- **Controllers**: aplican las reglas de la aplicación, validan solicitudes y producen resultados MVC.
- **Views**: presentan la información mediante Razor.
- **ApplicationDbContext**: conecta el modelo persistente con EF Core y configura relaciones, índices y restricciones.
- **Migraciones**: describen la evolución del esquema de base de datos.
- **Pruebas**: verifican comportamiento con SQLite InMemory y, para HTTP, con una factory aislada.

## Árbol detallado

```text
MSDentalSys/
├── MSDentalSys.sln
├── global.json
├── src/
│   ├── MSDentalSys.Data/
│   │   ├── Context/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   └── ApplicationDbContextFactory.cs
│   │   ├── InitialData/
│   │   │   ├── AdminSeeder.cs
│   │   │   └── RoleSeeder.cs
│   │   ├── Migrations/
│   │   ├── Models/
│   │   └── MSDentalSys.Data.csproj
│   └── MSDentalSys.Web/
│       ├── Controllers/
│       ├── Models/
│       │   └── ViewModels/
│       ├── Properties/
│       ├── Views/
│       ├── wwwroot/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── MSDentalSys.Web.csproj
├── tests/
│   └── MSDentalSys.Tests/
│       ├── Controllers/
│       ├── Data/
│       ├── Integration/
│       │   ├── AuthorizationIntegrationTests.cs
│       │   └── CustomWebApplicationFactory.cs
│       ├── Models/
│       ├── InfrastructureTests.cs
│       └── MSDentalSys.Tests.csproj
└── docs/prototipos/
```

La carpeta `docs/prototipos/` conserva el prototipo visual histórico y no forma parte de la infraestructura automatizada nueva.
