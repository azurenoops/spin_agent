using System.IO.Compression;
using System.Buffers.Binary;
using Ato.Copilot.Core.Interfaces.PackageImports;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private async Task ReadPdfAttachmentStreamAsync(PdfAttachmentScan scan, IToken token,
        string location, string? name, int depth)
    {
        var child = PdfAttachmentEntry(scan, location, name);
        if (child is null || child.Status != CspPackageEntryStatus.Pending || child.Content is not null) return;
        try
        {
            var stream = scan.Resolve(token, ref depth) as StreamToken ?? throw PdfMalformed("Embedded file object is not a stream.");
            var declaredSize = PdfAttachmentSize(scan, stream.StreamDictionary, depth + 1);
            child.ExpandedBytes = declaredSize ?? 0;
            if (name is null || string.IsNullOrWhiteSpace(name))
                throw PdfMalformed("Embedded file has no usable filename or name-tree label.");
            if (!SafeArchivePath(name))
                throw new PdfAttachmentException("UNSAFE_ARCHIVE_PATH",
                    "PDF attachment uses an absolute, traversing or ambiguous filename. Repackage with safe relative names.");
            if (PdfValue(stream.StreamDictionary, "F") is not null)
                throw new PdfAttachmentException("PDF_ATTACHMENT_EXTERNAL_ONLY",
                    "Attachment stream references an external file. External stream bytes are never fetched; upload the source separately.");
            var flate = PdfAttachmentUsesFlate(scan, stream.StreamDictionary, depth + 1);
            if (!flate)
            {
                child.ExpandedBytes = declaredSize ?? stream.Data.Length;
                if (declaredSize.HasValue && declaredSize != stream.Data.Length)
                    throw PdfMalformed("Uncompressed attachment bytes do not match the declared expanded size.");
            }
            if (!scan.Session.CanRead(child)) return;
            var checksum = flate ? PdfZlibChecksum(stream.Data.Span) : (uint?)null;
            using var encoded = new PdfAttachmentMemoryStream(stream.Data);
            using var decoded = flate ? new ZLibStream(encoded, CompressionMode.Decompress) : null;
            await ReadDecodedPdfAttachmentAsync(scan.Session, child, decoded ?? (Stream)encoded,
                declaredSize ?? (flate ? null : stream.Data.Length), checksum).ConfigureAwait(false);
            if (scan.TargetKey is null)
                await ExtractAsync(scan.Session, child, scan.Parent.Depth + 1).ConfigureAwait(false);
        }
        catch (BudgetExceededException exception) when (exception.Code is "ENTRY_SIZE_LIMIT" or "EXPANDED_SIZE_LIMIT")
        {
            child.Fail(exception.Code, exception.Message);
        }
        catch (PdfAttachmentException exception)
        {
            if (exception.Code is "PDF_ATTACHMENT_DEPTH_LIMIT" or "PDF_ATTACHMENT_CYCLE") scan.Complete = false;
            SetPdfAttachmentFailure(child, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidDataException or PdfDocumentFormatException or FormatException)
        {
            child.Set(CspPackageEntryStatus.Unreadable, "PDF_ATTACHMENT_MALFORMED",
                "Attachment stream is malformed or truncated. Re-export the source.");
        }
        catch (IOException)
        {
            child.Fail("CONTENT_READ_FAILED", "PDF attachment stream could not be read. Re-export it.");
        }
    }

    private static long? PdfAttachmentSize(PdfAttachmentScan scan, DictionaryToken dictionary, int depth)
    {
        var token = scan.Resolve(PdfValue(dictionary, "Params"), ref depth);
        if (token is null or NullToken) return null;
        var parameters = token as DictionaryToken ?? throw PdfMalformed("Attachment parameters are not a dictionary.");
        var size = scan.Read(parameters, "Size", depth + 1);
        if (size is null) return null;
        if (size is not NumericToken number || number.HasDecimalPlaces || number.Data < 0 || number.Data > long.MaxValue)
            throw PdfMalformed("Attachment expanded size is not a nonnegative integer.");
        return number.Long;
    }

    private static bool PdfAttachmentUsesFlate(PdfAttachmentScan scan, DictionaryToken dictionary, int depth)
    {
        var filterDepth = depth;
        var token = scan.Resolve(PdfValue(dictionary, "Filter"), ref filterDepth);
        var filters = new List<string>();
        if (token is ArrayToken array)
        {
            foreach (var item in array.Data)
                filters.Add((scan.Resolve(item, filterDepth + 1) as NameToken)?.Data
                    ?? throw PdfMalformed("Attachment filter list contains an invalid value."));
        }
        else if (token is NameToken name) filters.Add(name.Data);
        else if (token is not (null or NullToken)) throw PdfMalformed("Attachment filter is not a name or array.");
        if (filters.Contains("Crypt", StringComparer.Ordinal))
            throw new PdfAttachmentException("PDF_ATTACHMENT_ENCRYPTED",
                "Attachment requires a cryptographic filter. Supply an unrestricted export.");
        if (filters.Count > 1 || filters.Count == 1 && filters[0] is not ("FlateDecode" or "Fl"))
            throw PdfUnsupportedFilter();
        var parameters = scan.Resolve(PdfValue(dictionary, "DecodeParms"), ref depth);
        if (parameters is ArrayToken parameterArray)
        {
            if (parameterArray.Length != filters.Count || parameterArray.Length != 1) throw PdfUnsupportedFilter();
            parameters = scan.Resolve(parameterArray[0], depth + 1);
        }
        if (parameters is DictionaryToken parameterDictionary)
        {
            foreach (var pair in parameterDictionary.Data)
                if (pair.Key != "Predictor" || scan.Resolve(pair.Value, depth + 1) is not NumericToken { Data: 1, HasDecimalPlaces: false })
                    throw PdfUnsupportedFilter();
        }
        else if (parameters is not (null or NullToken)) throw PdfUnsupportedFilter();
        return filters.Count == 1;
    }

    private static PdfAttachmentException PdfUnsupportedFilter() => new("PDF_ATTACHMENT_FILTER_UNSUPPORTED",
        "Only unfiltered or a single Flate/zlib attachment stream without predictors is supported. Export the original attachment separately.");

    private async Task ReadDecodedPdfAttachmentAsync(AnalysisSession session, Entry child, Stream stream,
        long? declaredSize, uint? checksum)
    {
        var maximum = Math.Min(int.MaxValue, Math.Min(_limits.MaxEntryBytes,
            _limits.MaxExpandedBytes - session.ExpandedBytes));
        if (declaredSize.HasValue) maximum = Math.Min(maximum, declaredSize.Value);
        using var content = new MemoryStream();
        var buffer = new byte[(int)Math.Min(16 * 1024, Math.Max(1, maximum))];
        while (true)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var remaining = maximum - content.Length;
            // A one-byte EOF probe never becomes retained content or a budget charge.
            var request = (int)Math.Min(buffer.Length, Math.Max(1, remaining));
            var count = await stream.ReadAsync(buffer.AsMemory(0, request), session.Cancellation).ConfigureAwait(false);
            if (count == 0) break;
            if (remaining == 0) ThrowPdfDecodedLimit(session, child, declaredSize, content.Length);
            var length = content.Length + count;
            if (length > content.Capacity)
                content.Capacity = (int)Math.Min(maximum, Math.Max(length, Math.Max(4096L, content.Capacity * 2L)));
            content.Write(buffer, 0, count);
            if (!declaredSize.HasValue) child.ExpandedBytes = length;
            session.ExpandedBytes += count;
            child.ExpandedBytesCharged += count;
        }
        if (declaredSize.HasValue && content.Length != declaredSize)
            throw PdfMalformed("Decoded attachment does not match its declared expanded size.");
        if (checksum.HasValue && checksum != PdfAdler32(content.GetBuffer().AsSpan(0, (int)content.Length), session.Cancellation))
            throw PdfMalformed("Attachment zlib stream is truncated or its checksum is invalid.");
        child.ExpandedBytes = content.Length;
        child.Content = content.ToArray();
    }

    private static uint PdfZlibChecksum(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6 || (bytes[0] & 15) != 8 || bytes[0] >> 4 > 7 || ((bytes[0] << 8) + bytes[1]) % 31 != 0)
            throw PdfMalformed("Attachment does not contain a complete zlib stream.");
        if ((bytes[1] & 32) != 0) throw PdfUnsupportedFilter();
        return BinaryPrimitives.ReadUInt32BigEndian(bytes[^4..]);
    }

    private static uint PdfAdler32(ReadOnlySpan<byte> bytes, CancellationToken cancellation)
    {
        uint first = 1, second = 0;
        while (!bytes.IsEmpty)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(bytes.Length, 5552);
            foreach (var value in bytes[..count])
            {
                first += value;
                second += first;
            }
            first %= 65521;
            second %= 65521;
            bytes = bytes[count..];
        }
        return (second << 16) | first;
    }

    private void ThrowPdfDecodedLimit(AnalysisSession session, Entry child, long? declaredSize, long length)
    {
        if (declaredSize.HasValue && length >= declaredSize)
            throw PdfMalformed("Decoded attachment exceeds its declared expanded size.");
        if (length >= _limits.MaxEntryBytes || length >= int.MaxValue)
            throw new BudgetExceededException("ENTRY_SIZE_LIMIT", "Decoded PDF attachment exceeds its per-entry byte budget.");
        if (session.ExpandedBytes >= _limits.MaxExpandedBytes)
            throw new BudgetExceededException("EXPANDED_SIZE_LIMIT", "Decoded PDF attachments exhaust the expanded package byte budget.");
        throw PdfMalformed("Decoded attachment exceeded its reserved read budget.");
    }

    private sealed class PdfAttachmentMemoryStream(ReadOnlyMemory<byte> memory) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => memory.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, memory.Length - _position);
            memory.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
