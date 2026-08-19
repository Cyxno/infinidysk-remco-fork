using System.Buffers.Binary;
using System.Text;
using NzbWebDAV.Services.Repair;

namespace NzbWebDAV.Tests.Services.Repair;

public class Mp4LayoutProbeTests
{
    [Fact]
    public void FastStart_MoovBeforeMdat()
    {
        var head = Concat(Box("ftyp", 16), Box("moov", 24), BoxHeader("mdat", 4_000_000_000u));
        // ftyp is 24 bytes; moov is 32 bytes starting at 24.
        AssertProbe(head, MediaContainerClass.Mp4FastStart, 56);
    }

    [Fact]
    public void MoovAtEnd_MdatBeforeMoov()
    {
        var head = Concat(Box("ftyp", 16), Box("mdat", 100), Box("moov", 24));
        AssertProbe(head, MediaContainerClass.Mp4MoovAtEnd, 0);
    }

    [Fact]
    public void MoovAtEnd_MdatWithHugeDeclaredSize()
    {
        // The common case: mdat's declared size is the whole media payload, far
        // beyond the probed span. The decision triggers on sight, not on bounds.
        var head = Concat(Box("ftyp", 16), BoxHeader("mdat", 4_000_000_000u));
        AssertProbe(head, MediaContainerClass.Mp4MoovAtEnd, 0);
    }

    [Fact]
    public void Fragmented_MoofAfterMoov_IsResyncTolerant()
    {
        var head = Concat(Box("ftyp", 16), Box("moov", 24), Box("moof", 32));
        AssertProbe(head, MediaContainerClass.ResyncTolerant, 0);
    }

    [Fact]
    public void Fragmented_MoofBeforeMoov_IsResyncTolerant()
    {
        var head = Concat(Box("ftyp", 16), Box("moof", 32));
        AssertProbe(head, MediaContainerClass.ResyncTolerant, 0);
    }

    [Fact]
    public void FreeSkipWideUuidPrefixes_AreSkipped()
    {
        var head = Concat(
            Box("free", 8), Box("skip", 8), Box("wide", 0), Box("uuid", 16),
            BoxHeader("mdat", 4_000_000_000u));
        AssertProbe(head, MediaContainerClass.Mp4MoovAtEnd, 0);
    }

    [Fact]
    public void SixtyFourBitLargesize_IsParsed()
    {
        var head = Concat(Box("ftyp", 16), LargeBoxHeader("mdat", 5_000_000_000UL));
        AssertProbe(head, MediaContainerClass.Mp4MoovAtEnd, 0);
    }

    [Fact]
    public void SixtyFourBitLargesize_SkippedBox_FastStart()
    {
        var head = Concat(LargeBox("free", 24), Box("moov", 24), BoxHeader("mdat", 4_000_000_000u));
        AssertProbe(head, MediaContainerClass.Mp4FastStart, 56);
    }

    [Fact]
    public void ZeroSize_MdatWithoutMoov_ExtendsToEndOfFile()
    {
        var head = Concat(Box("ftyp", 16), ZeroSizeBox("mdat"));
        AssertProbe(head, MediaContainerClass.Mp4MoovAtEnd, 0);
    }

    [Fact]
    public void ZeroSize_NonMdat_IsUnknown()
    {
        var head = Concat(Box("ftyp", 16), ZeroSizeBox("free"));
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void SizeBelowHeader_IsUnknown()
    {
        var head = BoxHeader("ftyp", 4);
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void LargesizeBelowHeader_IsUnknown()
    {
        var head = LargeBoxHeader("mdat", 8);
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void TruncatedHeader_IsUnknown()
    {
        AssertProbe([0, 0, 0, 16], MediaContainerClass.Unknown, 0);
        var head = Concat(Box("ftyp", 16), new byte[] { 0, 0, 0, 8 });
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void TruncatedLargesizeHeader_IsUnknown()
    {
        // size == 1 but only 12 of the 16 largesize header bytes are available.
        var head = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(head, 1);
        Encoding.ASCII.GetBytes("ftyp").CopyTo(head, 4);
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void SkipPastAvailableData_IsUnknown()
    {
        var head = BoxHeader("ftyp", 1_000_000);
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void Garbage_IsUnknown()
    {
        var head = Enumerable.Repeat((byte)0xFF, 64).ToArray();
        AssertProbe(head, MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void MoovSpanningPastSpan_IsFastStart()
    {
        // A giant faststart moov we cannot see past still classifies; fMP4 init
        // moovs are small, so the fragmented case is always reached within the span.
        // The declared size sits in the header, so the extent is known anyway.
        var head = Concat(Box("ftyp", 16), BoxHeader("moov", 1_000_000));
        AssertProbe(head, MediaContainerClass.Mp4FastStart, 1_000_024);
    }

    [Fact]
    public void MoovThenEndOfData_IsFastStart()
    {
        var head = Concat(Box("ftyp", 16), Box("moov", 24));
        AssertProbe(head, MediaContainerClass.Mp4FastStart, 56);
    }

    [Fact]
    public void EmptyBuffer_IsUnknown()
    {
        AssertProbe([], MediaContainerClass.Unknown, 0);
    }

    [Fact]
    public void InsaneDeclaredMoovSize_ReturnsFastStartWithZeroExtent()
    {
        var head = Concat(Box("ftyp", 16), LargeBoxHeader("moov", ulong.MaxValue));
        AssertProbe(head, MediaContainerClass.Mp4FastStart, 0);
    }

    private static void AssertProbe(byte[] head, MediaContainerClass expected, long expectedExtent)
    {
        var (cls, extent) = Mp4LayoutProbe.ClassifyMp4Head(head);
        Assert.Equal(expected, cls);
        Assert.Equal(expectedExtent, extent);
    }

    private static byte[] Box(string type, int payloadSize)
    {
        var bytes = new byte[8 + payloadSize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(8 + payloadSize));
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] BoxHeader(string type, uint declaredSize)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, declaredSize);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] LargeBox(string type, ulong declaredSize)
    {
        var bytes = new byte[16 + ((int)declaredSize - 16)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), declaredSize);
        return bytes;
    }

    private static byte[] LargeBoxHeader(string type, ulong declaredSize)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), declaredSize);
        return bytes;
    }

    private static byte[] ZeroSizeBox(string type)
    {
        var bytes = new byte[8];
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] Concat(params byte[][] arrays) =>
        arrays.SelectMany(array => array).ToArray();
}
