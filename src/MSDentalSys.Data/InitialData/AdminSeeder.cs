using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MSDentalSys.Data.Models;

namespace MSDentalSys.Data.InitialData
{
    public static class AdminSeeder
    {
        private const string AdminEmail = "admin@msdentalsys.local";
        private const string AdminRole = "Administrador";

        public static async Task SeedAsync(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            var adminUser = await userManager.FindByEmailAsync(AdminEmail);

            if (adminUser is null)
            {
                var password = configuration["SeedAdmin:Password"];

                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException(
                        "No se puede crear el administrador inicial porque no está configurada " +
                        "la contraseña 'SeedAdmin:Password'. Configúrela mediante .NET User Secrets.");
                }

                adminUser = new ApplicationUser
                {
                    UserName = AdminEmail,
                    Email = AdminEmail,
                    Nombre = "Administrador",
                    Apellido = "Sistema",
                    Estado = true,
                    FechaCreacion = DateTime.Now
                };

                var createResult = await userManager.CreateAsync(adminUser, password);

                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException(
                        $"No se pudo crear el administrador inicial: {errors}");
                }
            }

            if (!await userManager.IsInRoleAsync(adminUser, AdminRole))
            {
                var roleResult = await userManager.AddToRoleAsync(adminUser, AdminRole);

                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(", ", roleResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException(
                        $"No se pudo asignar el rol '{AdminRole}' al administrador inicial: {errors}");
                }
            }
        }
    }
}
