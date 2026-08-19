using System.Buffers.Binary;
using System.Text;

namespace NzbWebDAV.Services.Repair;

public static class Mp4LayoutProbe
{
    public static (MediaContainerClass Class, long CriticalHeadEndExclusive) ClassifyMp4Head(ReadOnlySpan<byte> head)
    {
        var offset = 0;
        var sawMoov = false;
        var criticalHeadEndExclusive = 0L;

        while (offset < head.Length)
        {
            if (head.Length - offset < 8) return (MediaContainerClass.Unknown, 0);

            var size = BinaryPrimitives.ReadUInt32BigEndian(head[offset..]);
            var type = Encoding.ASCII.GetString(head.Slice(offset + 4, 4));
            var headerSize = 8;
            ulong boxSize = size;

            if (size == 1)
            {
                if (head.Length - offset < 16) return (MediaContainerClass.Unknown, 0);
                boxSize = BinaryPrimitives.ReadUInt64BigEndian(head[(offset + 8)..]);
                headerSize = 16;
                if (boxSize < (ulong)headerSize) return (MediaContainerClass.Unknown, 0);
            }
            else if (size == 0)
            {
                return type == "mdat" && !sawMoov
                    ? (MediaContainerClass.Mp4MoovAtEnd, 0)
                    : (MediaContainerClass.Unknown, 0);
            }
            else if (boxSize < (ulong)headerSize)
            {
                return (MediaContainerClass.Unknown, 0);
            }

            // Decision points trigger on sight, regardless of the declared payload size
            // (mdat's size is the media payload — typically far beyond the probed span).
            if (type == "mdat")
                return sawMoov
                    ? (MediaContainerClass.Mp4FastStart, criticalHeadEndExclusive)
                    : (MediaContainerClass.Mp4MoovAtEnd, 0);
            if (type == "moof") return (MediaContainerClass.ResyncTolerant, 0);

            if (boxSize > (ulong)(head.Length - offset))
                // A moov we cannot see past still classifies: fMP4 init moovs are small, so
                // a moov spanning past the span means faststart. A skipped box (ftyp, free,
                // uuid, …) beyond the span leaves the layout unclassifiable. The moov size
                // sits in the header already read, so the critical extent is known even
                // when the box itself continues past the probed buffer.
                return type == "moov"
                    ? (MediaContainerClass.Mp4FastStart, ToCriticalHeadEndExclusive(offset, boxSize))
                    : (MediaContainerClass.Unknown, 0);

            if (type == "moov")
            {
                sawMoov = true;
                criticalHeadEndExclusive = ToCriticalHeadEndExclusive(offset, boxSize);
            }

            offset += checked((int)boxSize);
        }

        return sawMoov
            ? (MediaContainerClass.Mp4FastStart, criticalHeadEndExclusive)
            : (MediaContainerClass.Unknown, 0);
    }

    private static long ToCriticalHeadEndExclusive(int startOffset, ulong boxSize)
    {
        if (boxSize > (ulong)long.MaxValue) return 0;
        try
        {
            return checked(startOffset + (long)boxSize);
        }
        catch (OverflowException)
        {
            return 0;
        }
    }
}
