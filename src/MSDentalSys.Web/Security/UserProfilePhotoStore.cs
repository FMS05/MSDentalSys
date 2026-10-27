using System.Text.RegularExpressions;

namespace MSDentalSys.Web.Security;

/// <summary>Almacena claves internas de fotos; nunca interpreta rutas o URLs del formulario.</summary>
public sealed class UserProfilePhotoStore
{
    public const long MaxFileSize = 2 * 1024 * 1024;
    private const string InvalidPhotoMessage = "Selecciona una imagen JPEG o PNG válida.";
    private static readonly Regex OwnedFileName = new(@"\A[a-f0-9]{32}\.(?:jpg|png)\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly string directory;

    public UserProfilePhotoStore(IWebHostEnvironment environment)
    {
        directory = Path.Combine(environment.ContentRootPath, "App_Data", "ProfilePhotos");
    }

    public async Task<(string? FileName, string? Error)> ValidateAndSaveAsync(
        IFormFile photo, CancellationToken cancellationToken = default)
    {
        if (photo.Length <= 0) return (null, InvalidPhotoMessage);
        if (photo.Length > MaxFileSize) return (null, "La foto no puede superar los 2 MB.");

        var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (extension == ".jpeg") extension = ".jpg";
        var contentType = ContentType(extension);
        if (contentType is null || !string.Equals(photo.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            return (null, InvalidPhotoMessage);

        await using var source = photo.OpenReadStream();
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            // No confiar únicamente en el tamaño declarado por el cliente.
            if (content.Length + read > MaxFileSize) return (null, "La foto no puede superar los 2 MB.");
            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = content.ToArray();
        if (!HasSignature(bytes, extension)) return (null, InvalidPhotoMessage);

        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, fileName);
        var created = false;
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);
            created = true;
            await destination.WriteAsync(bytes, cancellationToken);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
        return (fileName, null);
    }

    public async Task<(byte[] Content, string ContentType)?> TryReadAsync(
        string? fileName, CancellationToken cancellationToken = default)
    {
        if (!IsOwned(fileName)) return null;
        try
        {
            await using var source = new FileStream(Path.Combine(directory, fileName!), FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous);
            if (source.Length <= 0 || source.Length > MaxFileSize) return null;
            var content = new byte[(int)source.Length];
            await source.ReadExactlyAsync(content, cancellationToken);
            var extension = Path.GetExtension(fileName!);
            return HasSignature(content, extension) ? (content, ContentType(extension)!) : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void DeleteOwned(string? fileName)
    {
        if (!IsOwned(fileName)) return;
        try { File.Delete(Path.Combine(directory, fileName!)); }
        catch (DirectoryNotFoundException) { }
    }

    private static bool IsOwned(string? fileName) => fileName is not null && OwnedFileName.IsMatch(fileName);

    private static string? ContentType(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        _ => null
    };

    // Se verifican firmas de archivo, no se atribuye decodificación o saneamiento de píxeles.
    private static bool HasSignature(ReadOnlySpan<byte> content, string extension) => extension switch
    {
        ".jpg" => content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".png" => content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false
    };
}
