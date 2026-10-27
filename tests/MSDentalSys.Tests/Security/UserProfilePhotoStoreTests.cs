using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using MSDentalSys.Web.Security;
using Xunit;

namespace MSDentalSys.Tests.Security;

public sealed class UserProfilePhotoStoreTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jX1sAAAAASUVORK5CYII=");
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    [Theory]
    [InlineData("foto.png", "image/png", ".png")]
    [InlineData("foto.jpg", "image/jpeg", ".jpg")]
    [InlineData("FOTO.JPEG", "image/jpeg", ".jpg")]
    public async Task Guardar_ValidaFirmaYGeneraClavePrivada_LeerConservaContenido(
        string originalName, string mimeType, string extension)
    {
        using var directory = new PhotoDirectory();
        var bytes = extension == ".png" ? Png : JpegSignature;
        var saved = await directory.Store.ValidateAndSaveAsync(Photo(bytes, originalName, mimeType));
        Assert.Null(saved.Error);
        Assert.Matches(@"\A[a-f0-9]{32}" + System.Text.RegularExpressions.Regex.Escape(extension) + @"\z", saved.FileName!);
        Assert.NotEqual(originalName, saved.FileName);
        Assert.True(File.Exists(Path.Combine(directory.PhotosPath, saved.FileName!)));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "wwwroot")));
        var read = await directory.Store.TryReadAsync(saved.FileName);
        Assert.NotNull(read);
        Assert.Equal(bytes, read.Value.Content);
        Assert.Equal(mimeType, read.Value.ContentType);
        directory.Store.DeleteOwned(saved.FileName);
        Assert.Null(await directory.Store.TryReadAsync(saved.FileName));
        directory.Store.DeleteOwned(saved.FileName);
    }

    [Theory]
    [InlineData("foto.svg", "image/svg+xml", true)]
    [InlineData("foto.gif", "image/gif", true)]
    [InlineData("foto.html", "text/html", true)]
    [InlineData("foto.png", "image/jpeg", true)]
    [InlineData("foto.jpg", "image/png", false)]
    [InlineData("foto.png", "image/png", false)]
    [InlineData("foto.jpg", "image/jpeg", true)]
    [InlineData("foto.png.exe", "image/png", true)]
    public async Task Guardar_ExtensionMimeOFirmaIncompatibles_NoEscribe(string name, string mimeType, bool pngBytes)
    {
        using var directory = new PhotoDirectory();
        var saved = await directory.Store.ValidateAndSaveAsync(Photo(pngBytes ? Png : JpegSignature, name, mimeType));
        Assert.Null(saved.FileName);
        Assert.NotNull(saved.Error);
        Assert.False(Directory.Exists(directory.PhotosPath));
    }

    [Fact]
    public async Task Guardar_ArchivoVacioOSinFirma_NoEscribe()
    {
        using var directory = new PhotoDirectory();
        foreach (var bytes in new[] { Array.Empty<byte>(), "<script>alert(1)</script>"u8.ToArray() })
        {
            var saved = await directory.Store.ValidateAndSaveAsync(Photo(bytes, "foto.png", "image/png"));
            Assert.Null(saved.FileName);
            Assert.NotNull(saved.Error);
        }
        Assert.False(Directory.Exists(directory.PhotosPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guardar_SuperaDosMb_InclusoLongitudDeclaradaManipulada_NoEscribe(bool forgedLength)
    {
        using var directory = new PhotoDirectory();
        var bytes = new byte[UserProfilePhotoStore.MaxFileSize + 1];
        Png.CopyTo(bytes, 0);
        var saved = await directory.Store.ValidateAndSaveAsync(new DeclaredLengthPhoto(bytes, forgedLength ? 12 : bytes.Length));
        Assert.Null(saved.FileName);
        Assert.Contains("2 MB", saved.Error);
        Assert.False(Directory.Exists(directory.PhotosPath));
    }

    [Fact]
    public async Task Guardar_MismoNombreOriginal_GeneraDosClavesDistintas()
    {
        using var directory = new PhotoDirectory();
        var first = await directory.Store.ValidateAndSaveAsync(Photo(Png, "foto.png", "image/png"));
        var second = await directory.Store.ValidateAndSaveAsync(Photo(Png, "foto.png", "image/png"));
        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Equal(2, Directory.GetFiles(directory.PhotosPath).Length);
    }

    [Fact]
    public async Task LeerYEliminar_RutasUrlsYClavesAjenas_NoAccedenAArchivos()
    {
        using var directory = new PhotoDirectory();
        Directory.CreateDirectory(directory.PhotosPath);
        var sentinel = Path.Combine(directory.Path, "sentinel.png");
        await File.WriteAllBytesAsync(sentinel, Png);
        var legacy = Path.Combine(directory.PhotosPath, "legacy.png");
        await File.WriteAllBytesAsync(legacy, Png);
        string?[] invalidKeys =
        [
            null, "", "../sentinel.png", "..\\sentinel.png", sentinel,
            "https://example.test/foto.png", "legacy.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png:extra",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png/../sentinel.png"
        ];
        foreach (var key in invalidKeys)
        {
            Assert.Null(await directory.Store.TryReadAsync(key));
            directory.Store.DeleteOwned(key);
            Assert.True(File.Exists(sentinel));
            Assert.True(File.Exists(legacy));
        }
    }

    [Fact]
    public async Task Leer_ClaveAusenteOCorrupta_NoDevuelveContenido()
    {
        using var directory = new PhotoDirectory();
        const string key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";
        Assert.Null(await directory.Store.TryReadAsync(key));
        directory.Store.DeleteOwned(key);
        Directory.CreateDirectory(directory.PhotosPath);
        await File.WriteAllTextAsync(Path.Combine(directory.PhotosPath, key), "contenido inesperado");
        Assert.Null(await directory.Store.TryReadAsync(key));
    }

    private static IFormFile Photo(byte[] content, string name, string mimeType) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "Foto", name)
        {
            Headers = new HeaderDictionary(), ContentType = mimeType
        };

    private sealed class PhotoDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "MSDentalSys-PhotoTests", Guid.NewGuid().ToString("N"));
        public string PhotosPath => System.IO.Path.Combine(Path, "App_Data", "ProfilePhotos");
        public UserProfilePhotoStore Store { get; }
        public PhotoDirectory() => Store = new UserProfilePhotoStore(new TestEnvironment { ContentRootPath = Path });
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MSDentalSys-PhotoTests"))
                + System.IO.Path.DirectorySeparatorChar;
            var target = System.IO.Path.GetFullPath(Path);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PhotoTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DeclaredLengthPhoto(byte[] bytes, long declaredLength) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => declaredLength;
        public string Name => "Foto";
        public string FileName => "foto.png";
        public Stream OpenReadStream() => new MemoryStream(bytes);
        public void CopyTo(Stream target) => target.Write(bytes);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            target.WriteAsync(bytes, cancellationToken).AsTask();
    }
}
