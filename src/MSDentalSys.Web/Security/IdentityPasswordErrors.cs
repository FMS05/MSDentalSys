using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MSDentalSys.Web.Security;

public static class IdentityPasswordErrors
{
    public static void AddErrors(ModelStateDictionary modelState, IdentityResult result, PasswordOptions options)
    {
        foreach (var error in result.Errors)
        {
            // No reflejar Description: un proveedor podría incluir datos de la petición.
            var (key, message) = error.Code switch
            {
                "PasswordMismatch" => ("CurrentPassword", "La contraseña actual no es correcta."),
                "PasswordTooShort" => ("NewPassword", $"La nueva contraseña debe tener al menos {options.RequiredLength} caracteres."),
                "PasswordRequiresDigit" => ("NewPassword", "La nueva contraseña debe incluir un número."),
                "PasswordRequiresLower" => ("NewPassword", "La nueva contraseña debe incluir una letra minúscula."),
                "PasswordRequiresUpper" => ("NewPassword", "La nueva contraseña debe incluir una letra mayúscula."),
                "PasswordRequiresNonAlphanumeric" => ("NewPassword", "La nueva contraseña debe incluir un carácter especial."),
                "PasswordRequiresUniqueChars" => ("NewPassword", $"La nueva contraseña debe incluir al menos {options.RequiredUniqueChars} caracteres distintos."),
                "ConcurrencyFailure" => (string.Empty, "La cuenta cambió durante la operación. Recarga la página e intenta nuevamente."),
                _ => (string.Empty, "No se pudo actualizar la contraseña. Recarga la página e intenta nuevamente.")
            };
            modelState.AddModelError(key, message);
        }
    }

    public static void ClearAttemptedValues(ModelStateDictionary modelState)
    {
        // Conservar los errores de validación, pero nunca los valores enviados al formulario.
        foreach (var key in modelState.Keys.ToArray())
            modelState.SetModelValue(key, null, null);
    }
}
