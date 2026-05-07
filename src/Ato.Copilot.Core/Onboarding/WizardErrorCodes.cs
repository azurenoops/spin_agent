namespace Ato.Copilot.Core.Onboarding;

/// <summary>
/// Canonical wizard error codes (Constitution VII — plain-language remediation).
/// Each constant matches a row in
/// [contracts/progress-events.md](../../specs/047-onboarding-wizard/contracts/progress-events.md)
/// "Wizard error codes". Add new codes here whenever a new failure mode is introduced;
/// tests assert that the catalog stays in sync with the contracts document.
/// </summary>
public static class WizardErrorCodes
{
    /// <summary>Two simultaneous first-users hit <c>POST /api/onboarding/start</c>; one wins, one retries.</summary>
    public const string BootstrapRace = "WIZARD_BOOTSTRAP_RACE";

    /// <summary>Caller lacks <c>Administrator</c> after onboarding completed (FR-009).</summary>
    public const string AuthForbidden = "WIZARD_AUTH_FORBIDDEN";

    /// <summary>Attempt to remove the last <c>Administrator</c> without designating a replacement.</summary>
    public const string LastAdminProtected = "WIZARD_LAST_ADMIN_PROTECTED";

    /// <summary>Subscription enumeration attempted without ARM scope consent (FR-070a).</summary>
    public const string ArmConsentRequired = "WIZARD_ARM_CONSENT_REQUIRED";

    /// <summary>Delegated ARM token expired during enumeration.</summary>
    public const string ArmTokenExpired = "WIZARD_ARM_TOKEN_EXPIRED";

    /// <summary>User has no visible subscriptions (FR-073 / FR-075).</summary>
    public const string ArmNoSubscriptionsVisible = "WIZARD_ARM_NO_SUBSCRIPTIONS_VISIBLE";

    /// <summary>ARM endpoint unreachable (transient infra).</summary>
    public const string ArmUnreachable = "WIZARD_ARM_UNREACHABLE";

    /// <summary>Uploaded eMASS file fails structure check (FR-031).</summary>
    public const string EmassInvalidFormat = "WIZARD_EMASS_INVALID_FORMAT";

    /// <summary>eMASS upload exceeds <c>Limits:EmassMaxBytes</c> (FR-036).</summary>
    public const string EmassTooLarge = "WIZARD_EMASS_TOO_LARGE";

    /// <summary>Image-only PDF (FR-044).</summary>
    public const string SspPdfNoTextLayer = "WIZARD_SSP_PDF_NO_TEXT_LAYER";

    /// <summary>Encrypted / password-protected PDF (FR-044).</summary>
    public const string SspPdfPasswordProtected = "WIZARD_SSP_PDF_PASSWORD_PROTECTED";

    /// <summary>Other parse failure (FR-044).</summary>
    public const string SspPdfUnreadable = "WIZARD_SSP_PDF_UNREADABLE";

    /// <summary>Non-NIST 800-53 control framework (FR-045).</summary>
    public const string SspPdfUnknownFramework = "WIZARD_SSP_PDF_UNKNOWN_FRAMEWORK";

    /// <summary>Wrong file format for template slot (FR-081).</summary>
    public const string TemplateWrongFormat = "WIZARD_TEMPLATE_WRONG_FORMAT";

    /// <summary>Template upload exceeds <c>Limits:TemplateMaxBytes</c> (FR-088).</summary>
    public const string TemplateTooLarge = "WIZARD_TEMPLATE_TOO_LARGE";

    /// <summary>Template accepted but flagged non-compliant (FR-084).</summary>
    public const string TemplateValidationWarnings = "WIZARD_TEMPLATE_VALIDATION_WARNINGS";

    /// <summary>Cannot delete a template currently marked default (FR-096).</summary>
    public const string TemplateDefaultProtected = "WIZARD_TEMPLATE_DEFAULT_PROTECTED";

    /// <summary>Delete blocked pending explicit confirmation flag (FR-096).</summary>
    public const string DependentConfirmRequired = "WIZARD_DEPENDENT_CONFIRM_REQUIRED";

    /// <summary>Per-tenant storage budget exceeded (FR-054 / FR-088).</summary>
    public const string QuotaExceeded = "WIZARD_QUOTA_EXCEEDED";

    /// <summary>Background-job worker error; original artifact retained (FR-065).</summary>
    public const string JobFailed = "WIZARD_JOB_FAILED";

    /// <summary>Admin or system cancelled the job.</summary>
    public const string JobCancelled = "WIZARD_JOB_CANCELLED";

    /// <summary>All defined wizard error codes (used by drift tests).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        BootstrapRace,
        AuthForbidden,
        LastAdminProtected,
        ArmConsentRequired,
        ArmTokenExpired,
        ArmNoSubscriptionsVisible,
        ArmUnreachable,
        EmassInvalidFormat,
        EmassTooLarge,
        SspPdfNoTextLayer,
        SspPdfPasswordProtected,
        SspPdfUnreadable,
        SspPdfUnknownFramework,
        TemplateWrongFormat,
        TemplateTooLarge,
        TemplateValidationWarnings,
        TemplateDefaultProtected,
        DependentConfirmRequired,
        QuotaExceeded,
        JobFailed,
        JobCancelled,
    };
}
