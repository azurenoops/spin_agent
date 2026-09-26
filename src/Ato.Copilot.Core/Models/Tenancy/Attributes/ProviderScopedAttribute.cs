namespace Ato.Copilot.Core.Models.Tenancy.Attributes;

/// <summary>Private provider data requiring an ordinary provider workspace and explicit provider ownership.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProviderScopedAttribute : Attribute;
