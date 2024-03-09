﻿namespace Lavalink4NET.Tracks;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text.Unicode;

public partial record class LavalinkTrack : ISpanFormattable
{
    public override string ToString()
    {
        return ToString(version: null, format: null, formatProvider: null);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(version: null, format: format, formatProvider: formatProvider);
    }

    public string ToString(int? version)
    {
        return ToString(version: version, format: null, formatProvider: null);
    }

    public string ToString(int? version, string? format, IFormatProvider? formatProvider)
    {
        // The ToString method is culture-neutral and format-neutral
        if (TrackData is not null && version is null)
        {
            return TrackData;
        }

        Span<char> buffer = stackalloc char[256];

        int charsWritten;
        while (!TryFormat(buffer, out charsWritten, version, format ?? default, formatProvider))
        {
            buffer = GC.AllocateUninitializedArray<char>(buffer.Length * 2);
        }

        var trackData = new string(buffer[..charsWritten]);

        if (version is null)
        {
            TrackData = trackData;
        }

        return trackData;
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        return TryFormat(destination, out charsWritten, version: null, format, provider);
    }

#pragma warning disable IDE0060
    public bool TryFormat(Span<char> destination, out int charsWritten, int? version, ReadOnlySpan<char> format, IFormatProvider? provider)
#pragma warning restore IDE0060
    {
        var buffer = ArrayPool<byte>.Shared.Rent(destination.Length);

        try
        {
            var result = TryEncode(buffer, version, out var bytesWritten);

            if (!result)
            {
                charsWritten = default;
                return false;
            }

            var operationStatus = Base64.EncodeToUtf8InPlace(
                buffer: buffer,
                dataLength: bytesWritten,
                bytesWritten: out var base64BytesWritten);

            if (operationStatus is not OperationStatus.Done)
            {
                if (operationStatus is OperationStatus.DestinationTooSmall)
                {
                    charsWritten = default;
                    return false;
                }

                throw new InvalidOperationException("Error while encoding to Base64.");
            }

            operationStatus = Utf8.ToUtf16(
                source: buffer.AsSpan(0, base64BytesWritten),
                destination: destination,
                bytesRead: out _,
                charsWritten: out charsWritten);

            if (operationStatus is not OperationStatus.Done)
            {
                if (operationStatus is OperationStatus.DestinationTooSmall)
                {
                    charsWritten = default;
                    return false;
                }

                throw new InvalidOperationException("Error while encoding to UTF-8.");
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal bool TryEncode(Span<byte> buffer, int? version, out int bytesWritten)
    {
        var versionValue = version ?? 3;

        if (versionValue is not 2 and not 3)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (SourceName is null)
        {
            throw new InvalidOperationException("Unknown source.");
        }

        var isProbingAudioTrack = IsProbingTrack(SourceName);

        if (isProbingAudioTrack && ProbeInfo is null)
        {
            throw new InvalidOperationException("For the HTTP and local source audio manager, a probe info must be given.");
        }

        if (buffer.Length < 5)
        {
            bytesWritten = 0;
            return false;
        }

        // Reserve 5 bytes for the header
        var headerBuffer = buffer[..5];
        buffer = buffer[5..];
        bytesWritten = 5;

        // Write title and author
        if (!TryEncodeString(ref buffer, Title, ref bytesWritten) ||
            !TryEncodeString(ref buffer, Author, ref bytesWritten))
        {
            return false;
        }

        // Write track duration
        if (buffer.Length < 8)
        {
            return false;
        }

        var duration = Duration == TimeSpan.MaxValue
            ? long.MaxValue
            : (long)Math.Round(Duration.TotalMilliseconds);

        BinaryPrimitives.WriteInt64BigEndian(
            destination: buffer[..8],
            value: duration);

        buffer = buffer[8..];
        bytesWritten += 8;

        // Write track identifier
        if (!TryEncodeString(ref buffer, Identifier, ref bytesWritten))
        {
            return false;
        }

        // Write stream flag
        if (buffer.Length < 1)
        {
            return false;
        }

        buffer[0] = (byte)(IsLiveStream ? 1 : 0);

        bytesWritten++;
        buffer = buffer[1..];

        var rawUri = Uri is null ? string.Empty : Uri.ToString();

        if (!TryEncodeOptionalString(ref buffer, rawUri, ref bytesWritten))
        {
            return false;
        }

        if (versionValue >= 3)
        {
            var rawArtworkUri = ArtworkUri is null ? string.Empty : ArtworkUri.ToString();

            if (!TryEncodeOptionalString(ref buffer, rawArtworkUri, ref bytesWritten) ||
                !TryEncodeOptionalString(ref buffer, Isrc, ref bytesWritten))
            {
                return false;
            }
        }

        // Write source name
        if (!TryEncodeString(ref buffer, SourceName, ref bytesWritten))
        {
            return false;
        }

        // Write probe information
        if (isProbingAudioTrack && !TryEncodeString(ref buffer, ProbeInfo, ref bytesWritten))
        {
            return false;
        }

        if (IsExtendedTrack(SourceName))
        {
            bool TryEncodeOptionalJsonString(ref Span<byte> buffer, string propertyName, ref int bytesWritten)
            {
                var value = AdditionalInformation.TryGetValue(propertyName, out var jsonElement)
                    ? jsonElement.GetString()!
                    : string.Empty;

                return TryEncodeOptionalString(ref buffer, value, ref bytesWritten);
            }

            if (!TryEncodeOptionalJsonString(ref buffer, "albumName", ref bytesWritten) ||
                !TryEncodeOptionalJsonString(ref buffer, "albumUrl", ref bytesWritten) ||
                !TryEncodeOptionalJsonString(ref buffer, "artistUrl", ref bytesWritten) ||
                !TryEncodeOptionalJsonString(ref buffer, "artistArtworkUrl", ref bytesWritten) ||
                !TryEncodeOptionalJsonString(ref buffer, "previewUrl", ref bytesWritten))
            {
                return false;
            }

            var isPreview = AdditionalInformation.TryGetValue("isPreview", out var isPreviewElement) && isPreviewElement.GetBoolean();

            if (buffer.Length < 1)
            {
                return false;
            }

            buffer[0] = (byte)(isPreview ? 1 : 0);
            bytesWritten++;
            buffer = buffer[1..];
        }

        // Write track start position
        if (buffer.Length < 8)
        {
            return false;
        }

        BinaryPrimitives.WriteInt64BigEndian(
            destination: buffer[..8],
            value: (long)Math.Round(StartPosition?.TotalMilliseconds ?? 0));

        // buffer = buffer[8..];
        bytesWritten += 8;

        var payloadLength = bytesWritten - 4;
        EncodeHeader(headerBuffer, payloadLength, (byte)versionValue);

        return true;
    }

    private static void EncodeHeader(Span<byte> headerBuffer, int payloadLength, byte version)
    {
        // Set "has version" in header
        var header = 0b01000000000000000000000000000000 | payloadLength;
        BinaryPrimitives.WriteInt32BigEndian(headerBuffer, header);

        // version
        headerBuffer[4] = version;
    }

    private static bool TryEncodeString(ref Span<byte> span, ReadOnlySpan<char> value, ref int bytesWritten)
    {
        if (span.Length < 2)
        {
            return false;
        }

        var lengthBuffer = span[..2];
        span = span[2..];

        var previousBytesWritten = bytesWritten;

        if (!TryWriteModifiedUtf8(ref span, value, ref bytesWritten))
        {
            return false;
        }

        var utf8BytesWritten = bytesWritten - previousBytesWritten;

        BinaryPrimitives.WriteUInt16BigEndian(lengthBuffer, (ushort)utf8BytesWritten);

        bytesWritten += 2;

        return true;
    }

    private static bool TryEncodeOptionalString(ref Span<byte> span, ReadOnlySpan<char> value, ref int bytesWritten)
    {
        if (span.Length < 1)
        {
            return false;
        }

        var present = !value.IsWhiteSpace();

        span[0] = (byte)(present ? 1 : 0);
        span = span[1..];
        bytesWritten++;

        if (!present)
        {
            return true;
        }

        if (!TryEncodeString(ref span, value, ref bytesWritten))
        {
            return false;
        }

        return true;
    }

    private static bool TryWriteModifiedUtf8(ref Span<byte> span, ReadOnlySpan<char> value, ref int bytesWritten)
    {
        // Ported from https://android.googlesource.com/platform/prebuilts/fullsdk/sources/android-29/+/refs/heads/androidx-wear-release/java/io/DataOutputStream.java

        int index;
        for (index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character is not (>= (char)0x0001 and <= (char)0x007F))
            {
                break;
            }

            if (span.IsEmpty)
            {
                return false;
            }

            span[0] = (byte)character;
            bytesWritten++;
            span = span[1..];
        }

        for (; index < value.Length; index++)
        {
            var character = value[index];

            if (character is >= (char)0x0001 and <= (char)0x007F)
            {
                if (span.IsEmpty)
                {
                    return false;
                }

                span[0] = (byte)character;
                bytesWritten++;
                span = span[1..];
            }
            else if (character > 0x07FF)
            {
                if (span.Length < 3)
                {
                    return false;
                }

                span[0] = (byte)(0xE0 | ((character >> 12) & 0x0F));
                span[1] = (byte)(0x80 | ((character >> 6) & 0x3F));
                span[2] = (byte)(0x80 | ((character >> 0) & 0x3F));
                bytesWritten += 3;
                span = span[3..];
            }
            else
            {
                if (span.Length < 2)
                {
                    return false;
                }

                span[0] = (byte)(0xC0 | ((character >> 6) & 0x1F));
                span[1] = (byte)(0x80 | ((character >> 0) & 0x3F));
                bytesWritten += 2;
                span = span[2..];
            }
        }

        return true;
    }
}
