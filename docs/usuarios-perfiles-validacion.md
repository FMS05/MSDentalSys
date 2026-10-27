# Usuarios, perfiles y contraseñas: cierre de implementación

Validación del 25/10/2026 (America/Santo_Domingo). Se continuó desde los cambios
existentes después de revisar `git status` y el diff, sin restaurarlos ni
reiniciar la implementación. No se ejecutó `database update`, `git add`, commit
ni push. No se introdujeron nuevas decisiones funcionales o de esquema.

## Reglas implementadas

- Administrador consulta y administra usuarios. Odontologo y Recepcionista
  consultan las cuentas operativas, incluidas las inactivas, sin acceso a
  perfiles de Administradores, cuentas de roles mixtos con Administrador o
  cuentas sin rol operativo. Solo pueden editar su propio perfil.
- El servidor vuelve a determinar permisos y datos protegidos: identificador
  de ruta, roles, indicadores de edición, nombre/apellido/Cédula/rol cuando el
  actor no es Administrador, estado y propiedades internas de Identity. Los
  POST requieren antiforgery; ocultar botones no constituye la autorización.
- Correo, teléfono y foto se editan según los permisos del perfil. El correo
  se sincroniza con UserName y sus normalizados mediante Identity, en una
  transacción; la edición propia renueva la sesión. El correo del Administrador
  inicial permanece fijo en `admin@msdentalsys.local`, también frente a POST
  manipulados. Se conservan las protecciones de su rol y estado.
- Cédula obligatoria en altas administrativas y opcional al editar cuentas
  existentes. Admite 11 dígitos ASCII, con o sin guiones, recorta espacios
  exteriores y guarda `XXX-XXXXXXX-X`; no comprueba dígito verificador.
  Solo Administrador puede modificarla. La unicidad se limita a AspNetUsers,
  incluye usuarios inactivos y no impide coincidencias con Pacientes. La
  validación detecta también valores históricos sin guiones; el índice protege
  las escrituras concurrentes del valor normalizado.
- Se reutiliza `FotoPerfil` como clave interna. Fotos JPEG/PNG de hasta 2 MB,
  con validación de extensión, MIME, firma y tamaño real, se almacenan en
  `App_Data/ProfilePhotos`, fuera de wwwroot y excluidas de Git. El endpoint
  Photo aplica la misma autorización que Details y responde con no-store y
  nosniff. Las rutas/URLs enviadas como FotoPerfil no se aceptan. Se limpia la
  foto nueva al revertir una operación y la anterior al sustituirla/eliminarla.
  La lectura permite borrado concurrente para evitar sharing violations en
  Windows al reemplazar una foto que se está sirviendo.
- Cambio de contraseña propia con contraseña actual, nueva distinta y
  confirmación, mediante Identity. Conserva la sesión actual; las anteriores
  quedan sujetas al intervalo existente del SecurityStamp de un minuto.
  Límite de cinco POST por usuario/minuto, sin cola, con 429 y Retry-After.
- Reset administrativo solo para Odontologo/Recepcionista, activos o inactivos;
  excluye cualquier Administrador, roles mixtos con Administrador y autorreset.
  Genera y consume el token en el servidor, conserva el estado de la cuenta y
  la sesión del actor. Los errores no repueblan contraseñas. Concurrencia y
  cookies reales se cubren en las pruebas. Detalle en [fase 4](fase4-contrasenas.md).

## Migración generada, no aplicada

`20261026002901_AddCedulaToApplicationUser`, con Designer y snapshot actualizado:

- Agrega únicamente `AspNetUsers.Cedula`, `nvarchar(20) NULL`, sin default ni backfill.
- Crea `IX_AspNetUsers_Cedula`, único, con filtro `[Cedula] IS NOT NULL`.
- Down elimina únicamente ese índice y esa columna.

Se revisó también el SQL generado offline. EF confirmó que no hay cambios de
modelo pendientes. Ambos comandos usaron entorno Testing y una cadena ficticia
de metadatos, sin consultar ni modificar la BD operativa. EF CLI 9.0.18 emitió
el aviso de ser anterior al runtime 9.0.20; ambos comandos terminaron con código 0.

## Resultados de verificación

| Comprobación | Resultado |
| --- | --- |
| Pruebas dirigidas de perfiles, permisos, Cédula, fotos y contraseñas | 207 aprobadas, 0 fallidas |
| Suite .NET completa, Release | 800 aprobadas, 0 fallidas, 1 omitida; 801 casos |
| Nuevos casos HTTP/multipart de fotos, incluidos en la suite completa | 14 aprobados |
| JavaScript: calendario y pacientes | 38 aprobadas, 0 fallidas, 0 omitidas |
| Build Release de toda la solución | 0 errores, 0 advertencias |
| EF has-pending-model-changes | Sin cambios pendientes |
| Script SQL de la migración | Generado y revisado, no ejecutado |
| git diff --check | Sin errores |
| Índice de Git | Sin cambios preparados |

La omisión corresponde a `H8_SqlServer_DosSolicitudesSoloUnaReserva`: no está
configurada `MSDENTALSYS_H8_SQLSERVER`. SQLite verifica persistencia, índice,
rollback y concurrencia optimista; no certifica bloqueo/aislamiento de SQL Server.

El intento inicial dirigido bajo sandbox encontró restricciones de Windows
EventLog/DataProtection y no se utilizó como validación final. La repetición
autorizada fuera del sandbox pasó, al igual que la suite completa. No se
deshabilitaron pruebas ni protecciones para resolver esos errores de entorno.

Comandos principales:

```powershell
dotnet build MSDentalSys.sln --configuration Release --no-restore
dotnet test MSDentalSys.sln --configuration Release --no-build --no-restore --logger 'trx;LogFileName=usuarios-suite-completa.trx' --logger 'console;verbosity=quiet' --results-directory TestResults/usuarios-continuacion
& 'C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VisualStudio/NodeJs/node.exe' --test tests/client/citas-calendario.test.cjs tests/client/pacientes-form.test.cjs
git diff --check
git status --short --untracked-files=all
```

Node utilizado: v24.17.0. Evidencia local en
`TestResults/usuarios-continuacion/usuarios-targeted-permisos.trx` y
`TestResults/usuarios-continuacion/usuarios-suite-completa.trx`.
El SQL offline está en `TestResults/usuarios-continuacion/cedula-migration.sql.tmp`.
Estos resultados temporales están excluidos de Git.

## Archivos modificados y nuevos

Estado final: 20 modificados y 19 nuevos sin seguimiento; ninguno preparado.
`M` identifica modificado y `??` identifica nuevo. La lista incluye el trabajo
que ya existía al retomar la ejecución.

```text
 M .gitignore
 M README.md
 M docs/pruebas.md
 M src/MSDentalSys.Data/Context/ApplicationDbContext.cs
 M src/MSDentalSys.Data/Migrations/ApplicationDbContextModelSnapshot.cs
 M src/MSDentalSys.Data/Models/ApplicationUser.cs
 M src/MSDentalSys.Web/Controllers/AccountController.cs
 M src/MSDentalSys.Web/Controllers/UsuariosController.cs
 M src/MSDentalSys.Web/Models/ViewModels/UsuarioCreateViewModel.cs
 M src/MSDentalSys.Web/Models/ViewModels/UsuarioDetailsViewModel.cs
 M src/MSDentalSys.Web/Models/ViewModels/UsuarioEditViewModel.cs
 M src/MSDentalSys.Web/Models/ViewModels/UsuarioListItemViewModel.cs
 M src/MSDentalSys.Web/Program.cs
 M src/MSDentalSys.Web/Views/Shared/_Layout.cshtml
 M src/MSDentalSys.Web/Views/Usuarios/Create.cshtml
 M src/MSDentalSys.Web/Views/Usuarios/Details.cshtml
 M src/MSDentalSys.Web/Views/Usuarios/Edit.cshtml
 M src/MSDentalSys.Web/Views/Usuarios/Index.cshtml
 M tests/MSDentalSys.Tests/Controllers/UsuariosControllerTests.cs
 M tests/MSDentalSys.Tests/Integration/AuthorizationIntegrationTests.cs
?? docs/fase4-contrasenas.md
?? docs/usuarios-perfiles-validacion.md
?? src/MSDentalSys.Data/Migrations/20261026002901_AddCedulaToApplicationUser.Designer.cs
?? src/MSDentalSys.Data/Migrations/20261026002901_AddCedulaToApplicationUser.cs
?? src/MSDentalSys.Web/Models/ViewModels/ChangePasswordViewModel.cs
?? src/MSDentalSys.Web/Models/ViewModels/ResetPasswordViewModel.cs
?? src/MSDentalSys.Web/Security/ChangePasswordRateLimit.cs
?? src/MSDentalSys.Web/Security/IdentityPasswordErrors.cs
?? src/MSDentalSys.Web/Security/UserProfilePhotoStore.cs
?? src/MSDentalSys.Web/Views/Account/ChangePassword.cshtml
?? src/MSDentalSys.Web/Views/Usuarios/ResetPassword.cshtml
?? tests/MSDentalSys.Tests/Context/UserCedulaSchemaTests.cs
?? tests/MSDentalSys.Tests/Controllers/AccountPasswordConcurrencyTests.cs
?? tests/MSDentalSys.Tests/Controllers/PasswordResetControllerTests.cs
?? tests/MSDentalSys.Tests/Integration/InitialAdminProfileTests.cs
?? tests/MSDentalSys.Tests/Integration/PasswordFlowsIntegrationTests.cs
?? tests/MSDentalSys.Tests/Integration/UserProfilePhotosIntegrationTests.cs
?? tests/MSDentalSys.Tests/Integration/UserProfilesIntegrationTests.cs
?? tests/MSDentalSys.Tests/Security/UserProfilePhotoStoreTests.cs
```

## Pendientes de validación manual

1. En un entorno de prueba autorizado, aplicar posteriormente la migración
   antes de arrancar esta versión; la aplicación ya consulta la columna Cedula.
   Esta entrega deja expresamente esa aplicación pendiente.
2. Revisar en navegador escritorio/móvil la navegación Usuarios/Mi perfil y
   los formularios según los tres roles: datos administrativos frente a propios,
   correo fijo inicial y mensajes de obligatoriedad/formato/duplicidad de Cédula.
3. Probar JPEG/PNG reales: visualización, reemplazo/eliminación, tamaños y
   permisos de acceso. La validación de firma no equivale a decodificación o
   saneamiento de píxeles. Comprobar permisos y persistencia del directorio
   privado en el entorno de despliegue.
4. Probar cambio propio y reset en navegadores/sesiones separados, RememberMe,
   nueva contraseña y revocación tras el intervalo de un minuto; revisar los
   mensajes de cuenta inactiva y límite de intentos.
5. Ejecutar H8 únicamente cuando exista un SQL Server aislado configurado.
   Las pruebas JavaScript usan dobles del DOM y no sustituyen revisión visual
   de calendario/pacientes con Bootstrap/jQuery en navegador.
