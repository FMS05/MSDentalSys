using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MSDentalSys.Web.Models.ViewModels
{
    public class UsuarioEditViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "Ingresa un correo electrónico válido.")]
        [StringLength(120, ErrorMessage = "El correo no puede superar los 120 caracteres.")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = string.Empty;

        [BindNever, ValidateNever]
        public bool EsAdministradorInicial { get; set; }

        [BindNever, ValidateNever]
        public bool PuedeEditarDatosAdministrativos { get; set; }

        [BindNever, ValidateNever]
        public bool PuedeEditarRol { get; set; }

        [BindNever, ValidateNever]
        public string? FotoPerfil { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(60, ErrorMessage = "El nombre no puede superar los 60 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El apellido es obligatorio.")]
        [StringLength(60, ErrorMessage = "El apellido no puede superar los 60 caracteres.")]
        [Display(Name = "Apellido")]
        public string Apellido { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "La cédula no puede superar los 20 caracteres.")]
        [Display(Name = "Cédula")]
        public string? Cedula { get; set; }

        [StringLength(20, ErrorMessage = "El teléfono no puede superar los 20 caracteres.")]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }

        [Display(Name = "Foto de perfil")]
        public IFormFile? FotoArchivo { get; set; }

        [Display(Name = "Eliminar la foto actual")]
        public bool EliminarFoto { get; set; }

        [Required(ErrorMessage = "Selecciona un rol.")]
        [Display(Name = "Rol")]
        public string Rol { get; set; } = string.Empty;
    }
}
