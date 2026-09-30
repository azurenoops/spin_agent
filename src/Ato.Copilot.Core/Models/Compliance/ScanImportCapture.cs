namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Server-validated capture passed through the existing parser/service pipeline.</summary>
public sealed record ScanImportCapture(string RecordId, string AssessmentId, string OperationKey, string ProvenanceJson)
{
    private static readonly AsyncLocal<ScanImportCapture?> Slot = new();
    public static ScanImportCapture? Current => Slot.Value;
    public static IDisposable Push(ScanImportCapture capture)
    {
        var prior = Slot.Value;
        Slot.Value = capture;
        return new Restore(() => Slot.Value = prior);
    }
    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
