using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Mcp.Services.Storage;

namespace Ato.Copilot.Tests.Unit.Services;

public class LocalFileStorageProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorageProvider _sut;

    public LocalFileStorageProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"evidence-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _sut = new LocalFileStorageProvider(
            _tempDir,
            Mock.Of<ILogger<LocalFileStorageProvider>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Save ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_CreatesFileAtExpectedPath()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test-content"));

        await _sut.SaveAsync("evidence/sys-1/art-1/test.pdf", stream, "application/pdf");

        var fullPath = Path.Combine(_tempDir, "evidence", "sys-1", "art-1", "test.pdf");
        File.Exists(fullPath).Should().BeTrue();
        (await File.ReadAllTextAsync(fullPath)).Should().Be("test-content");
    }

    [Fact]
    public async Task Save_CreatesIntermediateDirectories()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

        await _sut.SaveAsync("deep/nested/path/file.txt", stream, "text/plain");

        File.Exists(Path.Combine(_tempDir, "deep", "nested", "path", "file.txt")).Should().BeTrue();
    }

    // ─── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ExistingFile_ReturnsStream()
    {
        var path = "evidence/sys-1/art-1/test.txt";
        var fullPath = Path.Combine(_tempDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "hello");

        var stream = await _sut.GetAsync(path);

        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);
        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Get_NonExistentFile_ReturnsNull()
    {
        var stream = await _sut.GetAsync("nonexistent/path.txt");

        stream.Should().BeNull();
    }

    // ─── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingFile_DeletesAndReturnsTrue()
    {
        var path = "evidence/sys-1/art-1/delete-me.txt";
        var fullPath = Path.Combine(_tempDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "data");

        var result = await _sut.DeleteAsync(path);

        result.Should().BeTrue();
        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_NonExistentFile_ReturnsFalse()
    {
        var result = await _sut.DeleteAsync("nonexistent/file.txt");

        result.Should().BeFalse();
    }

    // ─── Exists ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Exists_ExistingFile_ReturnsTrue()
    {
        var path = "evidence/sys-1/art-1/exists.txt";
        var fullPath = Path.Combine(_tempDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "data");

        var result = await _sut.ExistsAsync(path);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_NonExistentFile_ReturnsFalse()
    {
        var result = await _sut.ExistsAsync("nonexistent/file.txt");

        result.Should().BeFalse();
    }

    // ─── Path Traversal Prevention ───────────────────────────────────────

    [Fact]
    public void PathTraversal_ThrowsInvalidOperationException()
    {
        var act = () => _sut.GetAsync("../../../etc/passwd");

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*path traversal*");
    }

    // ─── Path Convention ─────────────────────────────────────────────────

    [Fact]
    public async Task Save_UsesStructuredPathConvention()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        var path = "evidence/system-abc/artifact-def/report.pdf";

        await _sut.SaveAsync(path, stream, "application/pdf");

        var expected = Path.Combine(_tempDir, "evidence", "system-abc", "artifact-def", "report.pdf");
        File.Exists(expected).Should().BeTrue();
    }
}
