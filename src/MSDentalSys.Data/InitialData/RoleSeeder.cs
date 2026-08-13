using Microsoft.AspNetCore.Identity;

namespace MSDentalSys.Data.InitialData
{
    public static class RoleSeeder
    {
        private static readonly string[] Roles =
        [
            "Administrador",
            "Odontologo",
            "Recepcionista"
        ];

        public static async Task SeedAsync(RoleManager<IdentityRole> roleManager)
        {
            foreach (var roleName in Roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(roleName));

                    if (!result.Succeeded)
                    {
                        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                        throw new InvalidOperationException(
                            $"No se pudo crear el rol '{roleName}': {errors}");
                    }
                }
            }
        }
    }
}
