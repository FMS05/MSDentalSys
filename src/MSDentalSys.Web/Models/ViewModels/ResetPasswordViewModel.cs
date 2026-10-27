using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MSDentalSys.Web.Models.ViewModels;

public class ResetPasswordViewModel
{
    [BindNever]
    public string Id { get; set; } = string.Empty;

    [BindNever]
    public string NombreCompleto { get; set; } = string.Empty;

    [BindNever]
    public string Email { get; set; } = string.Empty;

    [BindNever]
    public bool Estado { get; set; }

    [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nueva contraseña")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirma la nueva contraseña.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Confirmar nueva contraseña")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
