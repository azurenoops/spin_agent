using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Service for sending profile-related notifications to Mission Owners (FR-049).
/// </summary>
public interface IProfileNotificationService
{
    /// <summary>
    /// Notify a user they have been assigned MissionOwner role for a system.
    /// Sends email notification with system name and profile link.
    /// </summary>
    Task NotifyMissionOwnerAssignedAsync(
        string systemId,
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for sending email notifications (injectable for testability).
/// </summary>
public interface IEmailSender
{
    /// <summary>Send an email to the specified address.</summary>
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stub email sender that logs instead of sending (suitable for dev/test).
/// Replace with real SMTP/Graph implementation for production.
/// </summary>
public class StubEmailSender : IEmailSender
{
    private readonly ILogger<StubEmailSender> _logger;

    public StubEmailSender(ILogger<StubEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email stub — To: {ToEmail}, Subject: {Subject}, Body length: {BodyLength}",
            toEmail, subject, body.Length);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends profile-related notifications when a Mission Owner is assigned.
/// Creates email via <see cref="IEmailSender"/> (stubbed for dev, real for prod).
/// </summary>
public class ProfileNotificationService : IProfileNotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ProfileNotificationService> _logger;

    public ProfileNotificationService(
        IServiceScopeFactory scopeFactory,
        IEmailSender emailSender,
        ILogger<ProfileNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _emailSender = emailSender;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyMissionOwnerAssignedAsync(
        string systemId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system == null)
        {
            _logger.LogWarning("Cannot notify — system '{SystemId}' not found.", systemId);
            return;
        }

        var systemName = system.Name;

        var subject = $"Mission Owner Assignment: {systemName}";
        var body = $"You have been assigned as Mission Owner for \"{systemName}\". " +
                   $"Please complete the System Profile by navigating to the system's profile page.";

        await _emailSender.SendAsync(userId, subject, body, cancellationToken);

        _logger.LogInformation(
            "Notified user '{UserId}' of MissionOwner assignment for system '{SystemId}' ({SystemName})",
            userId, systemId, systemName);
    }
}
