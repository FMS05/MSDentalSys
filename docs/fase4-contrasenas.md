# Fase 4 — cambio y restablecimiento de contraseñas

La fase incorpora dos operaciones distintas sobre ASP.NET Core Identity. No
agrega entidades, tablas, migraciones, paquetes ni roles, y conserva la política
de contraseñas y la validación periódica de sesiones existentes.

## Autorización y datos del formulario

| Operación | Actor | Objetivo permitido |
| --- | --- | --- |
| Cambiar mi contraseña | Usuario autenticado: Administrador, Odontologo o Recepcionista | Exclusivamente la cuenta obtenida mediante `GetUserAsync(User)` |
| Restablecer contraseña | Administrador | Odontologo o Recepcionista, activo o inactivo, sin pertenencia a Administrador |

El restablecimiento rechaza a **cualquier Administrador**, incluido el propio
actor y cualquier cuenta que combine Administrador con otro rol. GET y POST
consultan los roles reales del objetivo. La protección no depende del enlace
visible en Details ni del correo del administrador inicial.
Además, compara el identificador real del objetivo con el del actor y prohíbe
el restablecimiento propio, incluso si la cookie conserva temporalmente un rol
anterior a un cambio de permisos.

La cuenta propia debe existir y estar activa. Una cuenta inexistente o inactiva
no puede continuar este flujo autenticado. `UserId` enviado por formulario no
selecciona la cuenta que cambia su propia contraseña. En el restablecimiento,
el identificador selecciona un objetivo que vuelve a autorizarse en el servidor;
nombre, correo, rol y estado se obtienen de Identity.

## Credenciales, errores y formulario

El cambio propio recibe contraseña actual, nueva y confirmación. Rechaza la
igualdad exacta con comparación ordinal de los valores de la petición, sin
recortar ni normalizar contraseñas. El restablecimiento solo recibe nueva y
confirmación; no intenta averiguar la contraseña anterior.

Identity conserva la validación definitiva de complejidad: longitud mínima 8,
dígito, mayúscula, minúscula y carácter no alfanumérico. Los ViewModels agregan
solo obligatoriedad, confirmación y metadatos de presentación.

Las operaciones POST requieren antiforgery. Los errores de Identity se traducen
por código a mensajes comprensibles; no se muestra un error técnico arbitrario.
Al volver a presentar un formulario se vacían tanto las propiedades password
como sus valores en ModelState, conservando los mensajes de validación.
La contraseña actual usa `autocomplete="current-password"`; las nuevas usan
`autocomplete="new-password"`, siempre con `type="password"`.

Las contraseñas y tokens de restablecimiento no se guardan en Session o TempData,
no se registran, no se incorporan a URLs y no se muestran en HTML. El token de
restablecimiento solo existe como variable local durante la petición que lo
genera y consume. El token antiforgery estándar sí forma parte del formulario.

## APIs de Identity y sesiones

El cambio propio usa `ChangePasswordAsync` y, tras éxito, `RefreshSignInAsync`.
El restablecimiento genera un token con `GeneratePasswordResetTokenAsync` y lo
consume inmediatamente con `ResetPasswordAsync`. Identity actualiza el
SecurityStamp al guardar la nueva contraseña; no hace falta una llamada
adicional a `UpdateSecurityStampAsync`. Una contraseña actual incorrecta produce
`PasswordMismatch` sin incrementar `AccessFailedCount`.
[Fuente oficial UserManager, versión 9.0.20](https://github.com/dotnet/aspnetcore/blob/v9.0.20/src/Identity/Extensions.Core/src/UserManager.cs).

`RefreshSignInAsync` conserva las propiedades de autenticación, incluido
RememberMe, y comprueba que se renueva la misma cuenta autenticada.
[Fuente oficial SignInManager, versión 9.0.20](https://github.com/dotnet/aspnetcore/blob/v9.0.20/src/Identity/Core/src/SignInManager.cs).

El intervalo existente de SecurityStamp continúa siendo **un minuto**: las
cookies anteriores se rechazan en una petición posterior a ese intervalo; no
hay cierre instantáneo de todos los navegadores. El restablecimiento no renueva
ni cierra la sesión del objetivo y conserva la sesión del Administrador actor.
Un objetivo inactivo sigue inactivo, y la confirmación informa expresamente de
ello. Ninguna operación de contraseña asigna `Estado`.

## Concurrencia

Se conserva la comprobación optimista que ofrece el almacén EF de Identity con
`ConcurrencyStamp`. Un cambio concurrente mediante Identity que se confirme
antes de la escritura de contraseña impide sobrescribir sus valores. Esto
incluye cambio de rol, cambio de estado, otra contraseña y eliminación del
usuario. El almacén convierte `DbUpdateConcurrencyException` en el resultado
`ConcurrencyFailure`. No se reintenta automáticamente una operación de
credenciales: se vuelve a consultar la cuenta y se requiere una nueva petición.
El restablecimiento descarta el estado rastreado fallido; el cambio propio usa
una consulta sin seguimiento para comprobar si la cuenta aún existe y está
activa, sin realizar más escrituras con la entidad fallida.
[Fuente oficial UserStore, versión 9.0.20](https://github.com/dotnet/aspnetcore/blob/v9.0.20/src/Identity/EntityFrameworkCore/src/UserStore.cs).

La garantía corresponde a las operaciones de usuarios del proyecto que pasan
por Identity. No se atribuye aislamiento Serializable, ni protección de cambios
manuales externos que eludan las APIs y su sello de concurrencia. Las pruebas
SQLite de un ganador intercalado no equivalen a probar bloqueo o aislamiento
de SQL Server.

## Protección de frecuencia del cambio propio

| Parámetro | Valor |
| --- | --- |
| Mecanismo | Middleware integrado de limitación de frecuencia de ASP.NET Core |
| Política | `ChangePassword` |
| Endpoint | Solo POST `/Account/ChangePassword` |
| Partición | Claim `ClaimTypes.NameIdentifier` de la identidad autenticada por el servidor; no se usa un campo del formulario |
| Algoritmo | Ventana fija |
| Límite | 5 peticiones por partición |
| Ventana | 1 minuto |
| Cola | 0; no se dejan peticiones pendientes |
| Exceso | HTTP 429, cabecera `Retry-After` en segundos y mensaje en español |

La respuesta es `text/plain; charset=utf-8`: «Has realizado demasiados intentos
de cambio de contraseña. Espera un minuto y vuelve a intentarlo.» `Retry-After`
usa el tiempo restante comunicado por el limitador, redondeado hacia arriba y
con mínimo de un segundo.

Se cuentan las peticiones al POST, incluidas las que terminan en error de
validación. GET no consume cupo. La autenticación y autorización preceden a la
limitación para obtener la partición del servidor. El Login mantiene su bloqueo
de cinco fallos durante 60 segundos y no usa esta política.

El contador vive en memoria en cada instancia del proceso. Reiniciar la
aplicación reinicia el contador; un despliegue con varias instancias requiere
evaluar un límite compartido en su infraestructura. La ventana fija permite
ráfagas en el límite entre dos ventanas.

## Verificación y hallazgos pendientes

Las pruebas de permisos y formularios se complementan con cookies reales de
Identity para comprobar persistencia de RememberMe, continuidad de la sesión
actual, revocación de otra sesión y continuidad del Administrador actor. Se
reutiliza `IdentityCookieWebApplicationFactory` y su reloj controlado para
superar el intervalo del sello. La autenticación simulada por cabeceras no
demuestra esas propiedades.

Los resultados de ejecución se reportan en la entrega de la implementación;
este documento describe la política y no certifica una ejecución de pruebas.
La prueba H8 SQL Server opcional sigue dependiendo de su entorno específico y
no se modifica para ocultar una omisión.

**Hallazgo pendiente:** la cookie conserva `SecurePolicy=SameAsRequest`.
Revisar una política de HTTPS estricta requiere evaluar el entorno local y el
despliegue; no forma parte de esta fase.
