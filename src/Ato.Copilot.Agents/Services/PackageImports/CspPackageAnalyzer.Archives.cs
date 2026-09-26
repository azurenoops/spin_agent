using System.Buffers.Binary;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private const uint EndOfDirectorySignature = 0x06054b50;
    private const uint DirectoryRecordSignature = 0x02014b50;
    private const int EndOfDirectoryLength = 22;
    private const int DirectoryRecordLength = 46;

    private void ValidateArchiveDirectory(AnalysisSession session, Entry container)
    {
        var bytes = container.Content!.AsSpan();
        var footer = FindArchiveFooter(bytes, session.Cancellation);
        if (footer < 0) throw new InvalidDataException("ZIP end-of-directory record is absent.");
        var end = bytes[footer..];
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(end[10..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
        if (entries == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
            throw new UnsupportedContentException("ZIP64_UNSUPPORTED",
                "ZIP64 directory metadata is not supported by the bounded analyzer. Repackage as standard ZIP within the limits.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0)
            throw new UnsupportedContentException("MULTIPART_ARCHIVE_UNSUPPORTED",
                "Multi-volume archives are not supported. Supply a self-contained ZIP.");
        if ((long)offset + size > footer)
            throw new InvalidDataException("ZIP central directory lies outside the archive.");
        var retainedChildren = session.Entries.Count(entry => entry.ParentKey == container.Key);
        if (entries > _limits.MaxEntries - session.Entries.Count + retainedChildren)
            throw new BudgetExceededException("ENTRY_COUNT_LIMIT",
                $"Archive declares {entries} entries, exceeding the remaining entry budget. Its contents were not enumerated; split the archive.");
        ValidateDirectoryRecords(bytes.Slice((int)offset, (int)size), entries, session.Cancellation);
    }

    private static int FindArchiveFooter(ReadOnlySpan<byte> bytes, CancellationToken cancellation)
    {
        var first = Math.Max(0, bytes.Length - EndOfDirectoryLength - ushort.MaxValue);
        for (var index = bytes.Length - EndOfDirectoryLength; index >= first; index--)
        {
            cancellation.ThrowIfCancellationRequested();
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[index..]) == EndOfDirectorySignature
                && index + EndOfDirectoryLength + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index + 20)..]) == bytes.Length)
                return index;
        }
        return -1;
    }

    private static void ValidateDirectoryRecords(ReadOnlySpan<byte> directory, int expectedCount, CancellationToken cancellation)
    {
        var count = 0;
        while (!directory.IsEmpty)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count >= expectedCount || directory.Length < DirectoryRecordLength
                || BinaryPrimitives.ReadUInt32LittleEndian(directory) != DirectoryRecordSignature)
                throw new InvalidDataException("ZIP directory record count or format is invalid.");
            var length = DirectoryRecordLength + BinaryPrimitives.ReadUInt16LittleEndian(directory[28..])
                + BinaryPrimitives.ReadUInt16LittleEndian(directory[30..])
                + BinaryPrimitives.ReadUInt16LittleEndian(directory[32..]);
            if (length > directory.Length)
                throw new InvalidDataException("ZIP directory record exceeds the declared directory length.");
            directory = directory[length..];
            count++;
        }
        if (count != expectedCount)
            throw new InvalidDataException("ZIP directory is incomplete.");
    }
}
