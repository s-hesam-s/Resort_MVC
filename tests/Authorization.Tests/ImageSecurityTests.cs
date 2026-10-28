using System.Linq.Expressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using Resort.Application.Common.Interfaces;
using Resort.Application.Common.Utility;
using Resort.Application.Services.Implementation;
using Resort.Domain.Entities;
using Xunit;

namespace Authorization.Tests;

public class ImageSecurityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "resort-image-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("image.svg")]
    [InlineData("image.html")]
    [InlineData("image.exe")]
    [InlineData("image.png")]
    public void ExecutableOrMismatchedImageIsRejected(string name) =>
        Assert.Throws<InvalidDataException>(() => VillaImageStorage.Validate(image(name, "<script>sample</script>"u8.ToArray())));

    [Fact]
    public void OversizedUploadIsRejectedBeforeReading()
    {
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(f => f.Length).Returns(VillaImageStorage.MaximumBytes + 1L);
        Assert.Throws<InvalidDataException>(() => VillaImageStorage.Validate(file.Object));
        file.Verify(f => f.OpenReadStream(), Times.Never);
    }

    [Theory]
    [InlineData("/images/VillaImage/../../outside.png")]
    [InlineData("/images/VillaImage/../outside.png")]
    [InlineData("C:/outside.png")]
    [InlineData("https://example.invalid/outside.png")]
    [InlineData("/images/VillaImage/file.png:alternate")]
    [InlineData("/images/elsewhere/file.png")]
    public void UnmanagedPathsCannotBecomeDeletionTargets(string value) =>
        Assert.Null(VillaImageStorage.ManagedPath(value, root));

    [Fact]
    public void ValidUploadGetsAnIndependentManagedName()
    {
        var url = VillaImageStorage.Save(image("../../client-name.PNG", png()), root);
        Assert.StartsWith("/images/VillaImage/", url);
        Assert.DoesNotContain("client-name", url);
        Assert.True(File.Exists(VillaImageStorage.ManagedPath(url, root)));
        VillaImageStorage.DeleteManaged(url, root);
        Assert.False(File.Exists(VillaImageStorage.ManagedPath(url, root)));
    }

    [Fact]
    public void UpdatingWithoutUploadPreservesStoredPathInsteadOfPostedPath()
    {
        var stored = new Villa { Id = 1, Name = "Stored", ImageUrl = "/images/VillaImage/stored.png" };
        var (service, repository, _) = createService(stored);
        Villa updated = null;
        repository.Setup(r => r.Update(It.IsAny<Villa>())).Callback<Villa>(v => updated = v);
        service.UpdateVilla(new Villa { Id = 1, Name = "Changed", ImageUrl = "../../outside.png", Price = 200 });
        Assert.Equal("Changed", updated.Name);
        Assert.Equal("/images/VillaImage/stored.png", updated.ImageUrl);
    }

    [Fact]
    public void FailedDatabaseSaveKeepsOldImageAndRemovesNewTaskFile()
    {
        var oldUrl = VillaImageStorage.Save(image("old.png", png()), root);
        var stored = new Villa { Id = 1, Name = "Stored", ImageUrl = oldUrl };
        var (service, repository, unit) = createService(stored);
        repository.Setup(r => r.Update(It.IsAny<Villa>()));
        unit.Setup(u => u.Save()).Throws(new InvalidOperationException("Simulated save failure"));
        Assert.Throws<InvalidOperationException>(() => service.UpdateVilla(new Villa
        { Id = 1, Name = "Changed", ImageUrl = "../../outside.png", Image = image("new.png", png()) }));
        Assert.True(File.Exists(VillaImageStorage.ManagedPath(oldUrl, root)));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "images", "VillaImage")));
    }

    private (VillaService service, Mock<IVillaRepository> repository, Mock<IUnitOfWork> unit) createService(Villa stored)
    {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var repository = new Mock<IVillaRepository>(MockBehavior.Strict);
        repository.Setup(r => r.Get(It.IsAny<Expression<Func<Villa, bool>>>(), null, false)).Returns(stored);
        unit.SetupGet(u => u.Villa).Returns(repository.Object);
        unit.Setup(u => u.Save());
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.WebRootPath).Returns(root);
        return (new VillaService(unit.Object, environment.Object), repository, unit);
    }

    private static byte[] png() => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
    private static IFormFile image(string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Image", name);

    public void Dispose()
    {
        var absolute = Path.GetFullPath(root);
        var allowed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(absolute))
            Directory.Delete(absolute, recursive: true);
    }
}
