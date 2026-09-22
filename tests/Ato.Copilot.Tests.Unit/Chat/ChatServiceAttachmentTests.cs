using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Unit tests for ChatService attachment methods (US5):
/// SaveAttachmentAsync, GetAttachmentTypeFromContentType, GenerateAnalysisPrompt.
/// </summary>
public class ChatServiceAttachmentTests : IDisposable
{
    private readonly ChatDbContext _dbContext;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ChatService>> _loggerMock;
    private readonly ChatService _service;
    private readonly LegacyChatTestScope _scope = new();

    public ChatServiceAttachmentTests()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ChatDbContext(options);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ChatService>>();

        var handler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:3001") };
        _httpClientFactoryMock.Setup(f => f.CreateClient("McpServer")).Returns(client);

        var pathSanitizer = new Mock<Ato.Copilot.Core.Interfaces.IPathSanitizationService>();
        pathSanitizer.Setup(s => s.ValidatePathWithinBase(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string path, string _) => new Ato.Copilot.Core.Interfaces.PathValidationResult { IsValid = true, CanonicalPath = path });

        _service = new ChatService(_dbContext, _httpClientFactoryMock.Object, _loggerMock.Object, pathSanitizer.Object, _scope.Resolver);
    }

    public void Dispose()
    {
        foreach (var attachment in _dbContext.Attachments)
            if (File.Exists(attachment.StoragePath)) File.Delete(attachment.StoragePath);
        _dbContext.Dispose();
        _scope.Dispose();
    }

    private async Task SeedOwnedMessageAsync(string id)
    {
        var conversation = new Conversation { OwnerKey = LegacyChatTestScope.DefaultOwnerKey };
        _dbContext.Conversations.Add(conversation);
        _dbContext.Messages.Add(new ChatMessage { Id = id, ConversationId = conversation.Id });
        await _dbContext.SaveChangesAsync();
    }

    // ─── SaveAttachmentAsync Tests ───────────────────────────────

    [Fact]
    public async Task SaveAttachmentAsync_StoresFileAndReturnsAttachment()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        await SeedOwnedMessageAsync(messageId);
        var content = "test file content"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var result = await _service.SaveAttachmentAsync(messageId, "test.txt", "text/plain", stream);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.MessageId.Should().Be(messageId);
        result.FileName.Should().Be("test.txt");
        result.ContentType.Should().Be("text/plain");
        result.Size.Should().Be(content.Length);
        result.StoragePath.Should().NotBeNullOrEmpty();
        File.Exists(result.StoragePath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAttachmentAsync_PersistsToDatabase()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        await SeedOwnedMessageAsync(messageId);
        using var stream = new MemoryStream("data"u8.ToArray());

        // Act
        var result = await _service.SaveAttachmentAsync(messageId, "data.csv", "text/csv", stream);

        // Assert
        var saved = await _dbContext.Attachments.FindAsync(result.Id);
        saved.Should().NotBeNull();
        saved!.FileName.Should().Be("data.csv");
    }

    [Fact]
    public async Task SaveAttachmentAsync_WithNullStream_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _service.SaveAttachmentAsync("msg-1", "file.txt", "text/plain", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAttachmentAsync_WithEmptyFileName_ThrowsArgumentException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[1]);

        // Act
        var act = () => _service.SaveAttachmentAsync("msg-1", "", "text/plain", stream);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*File name*");
    }

    [Fact]
    public async Task SaveAttachmentAsync_WithEmptyFile_StoresZeroByteAttachment()
    {
        // Arrange
        await SeedOwnedMessageAsync("msg-1");
        using var stream = new MemoryStream(Array.Empty<byte>());

        // Act
        var result = await _service.SaveAttachmentAsync("msg-1", "empty.txt", "text/plain", stream);

        // Assert
        result.Size.Should().Be(0);
    }

    // ─── GetAttachmentTypeFromContentType Tests ──────────────────

    [Theory]
    [InlineData("image/png", AttachmentType.Image)]
    [InlineData("image/jpeg", AttachmentType.Image)]
    [InlineData("image/gif", AttachmentType.Image)]
    [InlineData("image/svg+xml", AttachmentType.Image)]
    public void GetAttachmentTypeFromContentType_ClassifiesImages(string contentType, AttachmentType expected)
    {
        var result = _service.GetAttachmentTypeFromContentType(contentType);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("application/json", AttachmentType.Configuration)]
    [InlineData("application/xml", AttachmentType.Configuration)]
    [InlineData("text/yaml", AttachmentType.Configuration)]
    public void GetAttachmentTypeFromContentType_ClassifiesConfiguration(string contentType, AttachmentType expected)
    {
        var result = _service.GetAttachmentTypeFromContentType(contentType);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("text/javascript", AttachmentType.Code)]
    [InlineData("application/javascript", AttachmentType.Code)]
    [InlineData("text/x-python", AttachmentType.Code)]
    public void GetAttachmentTypeFromContentType_ClassifiesCode(string contentType, AttachmentType expected)
    {
        var result = _service.GetAttachmentTypeFromContentType(contentType);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("text/x-log", AttachmentType.Log)]
    [InlineData("text/plain", AttachmentType.Log)]
    public void GetAttachmentTypeFromContentType_ClassifiesLogs(string contentType, AttachmentType expected)
    {
        var result = _service.GetAttachmentTypeFromContentType(contentType);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("application/pdf", AttachmentType.Document)]
    [InlineData("application/msword", AttachmentType.Document)]
    [InlineData("application/octet-stream", AttachmentType.Document)]
    public void GetAttachmentTypeFromContentType_DefaultsToDocument(string contentType, AttachmentType expected)
    {
        var result = _service.GetAttachmentTypeFromContentType(contentType);
        result.Should().Be(expected);
    }

    [Fact]
    public void GetAttachmentTypeFromContentType_WithEmptyString_ReturnsDocument()
    {
        var result = _service.GetAttachmentTypeFromContentType("");
        result.Should().Be(AttachmentType.Document);
    }

    [Fact]
    public void GetAttachmentTypeFromContentType_WithNull_ReturnsDocument()
    {
        var result = _service.GetAttachmentTypeFromContentType(null!);
        result.Should().Be(AttachmentType.Document);
    }

    // ─── GenerateAnalysisPrompt Tests ────────────────────────────

    [Theory]
    [InlineData("config.yaml", "YAML")]
    [InlineData("config.yml", "YAML")]
    [InlineData("data.json", "JSON")]
    [InlineData("config.xml", "XML")]
    public void GenerateAnalysisPrompt_ForConfigFiles_ContainsExpectedKeyword(string fileName, string expectedKeyword)
    {
        var result = _service.GenerateAnalysisPrompt(fileName);
        result.Should().Contain(expectedKeyword);
        result.Should().Contain(fileName);
    }

    [Theory]
    [InlineData("app.cs", "source code")]
    [InlineData("main.java", "source code")]
    [InlineData("script.py", "source code")]
    [InlineData("app.ts", "source code")]
    [InlineData("index.js", "source code")]
    public void GenerateAnalysisPrompt_ForCodeFiles_ContainsSourceCodeReference(string fileName, string expectedPhrase)
    {
        var result = _service.GenerateAnalysisPrompt(fileName);
        result.Should().ContainEquivalentOf(expectedPhrase);
        result.Should().Contain(fileName);
    }

    [Fact]
    public void GenerateAnalysisPrompt_ForLogFile_ContainsErrorsAndWarnings()
    {
        var result = _service.GenerateAnalysisPrompt("server.log");
        result.Should().Contain("log");
        result.Should().Contain("server.log");
    }

    [Fact]
    public void GenerateAnalysisPrompt_ForDockerfile_ContainsBuildStages()
    {
        var result = _service.GenerateAnalysisPrompt("Dockerfile.dockerfile");
        result.Should().ContainEquivalentOf("Dockerfile");
    }

    [Fact]
    public void GenerateAnalysisPrompt_ForInfraAsCode_ContainsResources()
    {
        var result = _service.GenerateAnalysisPrompt("main.tf");
        result.Should().ContainEquivalentOf("infrastructure");
    }

    [Fact]
    public void GenerateAnalysisPrompt_ForUnknownExtension_ReturnsGenericPrompt()
    {
        var result = _service.GenerateAnalysisPrompt("data.xyz");
        result.Should().Contain("data.xyz");
        result.Should().Contain("analyze");
    }
}
