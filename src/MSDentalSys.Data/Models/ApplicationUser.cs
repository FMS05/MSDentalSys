using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string Nombre { get; set; } = string.Empty;

        public string Apellido { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Cedula { get; set; }

        public string? FotoPerfil { get; set; }

        public bool Estado { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
    }
}
