# Estrategia de pruebas automatizadas

## Tecnologías y aislamiento

Las pruebas utilizan xUnit sobre .NET 9. Las pruebas que requieren persistencia utilizan SQLite InMemory con un `ApplicationDbContext` aislado por escenario. La conexión se mantiene abierta durante cada prueba y se libera al finalizar.

Las pruebas de integración HTTP utilizan `Microsoft.AspNetCore.Mvc.Testing` mediante `WebApplicationFactory`. La factory fuerza el entorno `Testing`, reemplaza SQL Server por SQLite InMemory y crea el esquema con `EnsureCreated`.

El entorno `Testing` evita la ejecución de `RoleSeeder` y `AdminSeeder` de producción. La autenticación HTTP se simula exclusivamente en Tests mediante claims con `NameIdentifier`, `Name` y `Role` para los roles `Administrador`, `Odontologo` y `Recepcionista`.

## Grupos actuales

| Grupo | Pruebas |
|---|---:|
| Infraestructura | 1 |
| Login/autenticación | 7 |
| Atención odontológica | 9 |
| Pacientes | 5 |
| Citas | 9 |
| Servicios | 7 |
| Usuarios | 9 |
| Dashboard | 8 |
| Diagnósticos | 9 |
| Tratamientos | 17 |
| Evoluciones clínicas | 12 |
| Integración HTTP/autorización | 13 |
| **Total** | **123** |

## Cobertura por grupo

- **Pacientes**: registro, cédula duplicada u opcional y activación/desactivación.
- **Citas**: creación, conflictos de horario, reagendamiento y estados finales.
- **Usuarios**: creación, roles, duplicidad de correo, cambio de rol y estados.
- **Servicios**: creación, edición, activación/desactivación y búsquedas.
- **Dashboard**: conteos generales y filtrado de citas para odontólogos.
- **Login/autenticación**: credenciales, usuarios inactivos, logout y `RememberMe`.
- **Atención odontológica**: creación desde una cita válida, prevención de atenciones duplicadas, conservación del paciente y odontólogo asignados y validación del odontólogo autorizado.
- **Diagnósticos**: creación asociada a una atención, múltiples diagnósticos por atención y validación del odontólogo asignado.
- **Tratamientos**: asociación con servicios activos, múltiples tratamientos por atención, estados `Planificado`, `En progreso` y `Completado`, transiciones válidas y restricciones del odontólogo asignado.
- **Evoluciones clínicas**: validación de fecha y descripción, múltiples evoluciones por atención y validación del odontólogo asignado.
- **Integración HTTP/autorización**: autenticación requerida, redirecciones, permisos por rol y acceso permitido o rechazado.
- **Infraestructura**: funcionamiento básico de xUnit.

## Base de datos y seguridad de las pruebas

No se utiliza `MSDentalSysDB`. Tampoco se ejecutan migraciones contra la base real ni `database update`.

Las pruebas unitarias y de controlador utilizan bases SQLite en memoria. Las pruebas HTTP usan una base SQLite aislada durante la vida de la factory. La aplicación de pruebas se ejecuta en el entorno `Testing`, donde no se ejecutan los seeders de producción.

La autenticación de integración no utiliza usuarios reales ni User Secrets. El esquema de Tests emite claims controlados para simular cada rol y permitir verificar la autorización real de los controladores.

Las pruebas clínicas verifican además que el odontólogo solo pueda operar sobre la atención que le corresponde, que una atención pueda contener múltiples diagnósticos y evoluciones, y que los tratamientos respeten sus estados y transiciones permitidas.

## Comandos de validación

```powershell
dotnet build .\MSDentalSys.sln
dotnet test .\MSDentalSys.sln
```

Estado validado actualmente:

```text
123 pruebas correctas
0 fallidas
0 omitidas
```

## Alcance y limitaciones

Las pruebas HTTP validan el pipeline de autenticación y autorización de rutas con `WebApplicationFactory`, incluyendo permisos por rol y acceso de usuarios anónimos. La autenticación se simula mediante claims controlados en el entorno `Testing`. No son pruebas de navegador y no utilizan Selenium, Playwright ni servicios externos. Tampoco constituyen pruebas de rendimiento ni cobertura total del sistema.
