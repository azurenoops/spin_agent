namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>A safe, actionable Azure assessment prerequisite failure.</summary>
public sealed class AssessmentEnvironmentException : InvalidOperationException
{
    /// <summary>Creates a failure whose message and suggestion are safe for API callers.</summary>
    /// <param name="errorCode">Stable prerequisite identifier.</param>
    /// <param name="message">Safe user-facing explanation.</param>
    /// <param name="suggestion">Corrective guidance.</param>
    public AssessmentEnvironmentException(string errorCode, string message, string suggestion)
        : base(message)
    {
        ErrorCode = errorCode;
        Suggestion = suggestion;
    }

    /// <summary>Stable prerequisite identifier.</summary>
    public string ErrorCode { get; }

    /// <summary>Corrective guidance safe for API responses.</summary>
    public string Suggestion { get; }
}

/// <summary>Shared machine-readable Azure assessment admission failures.</summary>
public static class AssessmentEnvironmentErrors
{
    /// <summary>The system is absent or outside the caller's tenant scope.</summary>
    public const string SystemNotFound = "SYSTEM_NOT_FOUND";
    /// <summary>The actor must select the system organization.</summary>
    public const string OrganizationRequired = "ASSESSMENT_AZURE_ORGANIZATION_REQUIRED";
    /// <summary>The caller lacks assessment write permission.</summary>
    public const string PermissionRequired = "ASSESSMENT_PERMISSION_REQUIRED";
    /// <summary>No Azure profile is attached.</summary>
    public const string EnvironmentRequired = "ASSESSMENT_AZURE_ENVIRONMENT_REQUIRED";
    /// <summary>No usable subscription selection was supplied.</summary>
    public const string SubscriptionRequired = "ASSESSMENT_AZURE_SUBSCRIPTION_REQUIRED";
    /// <summary>The selection contains malformed, duplicate or excessive identifiers.</summary>
    public const string InvalidSubscription = "ASSESSMENT_AZURE_SUBSCRIPTION_INVALID";
    /// <summary>A selected subscription is no longer eligible or accessible.</summary>
    public const string SubscriptionUnavailable = "ASSESSMENT_AZURE_SUBSCRIPTION_UNAVAILABLE";
    /// <summary>The declared cloud/connector is not supported.</summary>
    public const string UnsupportedCloud = "ASSESSMENT_AZURE_CLOUD_UNSUPPORTED";
    /// <summary>The profile or registration disagrees with the deployed cloud.</summary>
    public const string CloudMismatch = "ASSESSMENT_AZURE_CLOUD_MISMATCH";
    /// <summary>The deployment has disabled or invalid Azure configuration.</summary>
    public const string DeploymentConfiguration = "ASSESSMENT_AZURE_CONFIGURATION_REQUIRED";
    /// <summary>The system has not been categorized.</summary>
    public const string CategorizationRequired = "ASSESSMENT_CATEGORIZATION_REQUIRED";
    /// <summary>The Azure assessment identity cannot authenticate.</summary>
    public const string AuthenticationRequired = "ASSESSMENT_AZURE_AUTHENTICATION_REQUIRED";
    /// <summary>The Azure identity lacks required read access.</summary>
    public const string AccessDenied = "ASSESSMENT_AZURE_ACCESS_DENIED";
    /// <summary>A required Azure service is unreachable or timed out.</summary>
    public const string ConnectionUnavailable = "ASSESSMENT_AZURE_CONNECTION_UNAVAILABLE";

    /// <summary>Maps safe prerequisite failures to the existing dashboard error contract.</summary>
    /// <param name="errorCode">Prerequisite identifier.</param>
    /// <returns>The appropriate HTTP status code.</returns>
    public static int HttpStatusCode(string errorCode) => errorCode switch
    {
        SystemNotFound => 404,
        PermissionRequired => 403,
        OrganizationRequired => 409,
        ConnectionUnavailable => 503,
        _ => 400
    };
}
