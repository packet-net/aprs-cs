using System.Collections;

namespace Packet.Aprs.Internal;

/// <summary>What stood where an element was lifted from the text.</summary>
internal enum Lifted : byte
{
    /// <summary>Nothing was lifted here.</summary>
    None = 0,

    /// <summary>A <c>/A=</c> altitude, which a voice frequency's fields may follow (APRS12c ch. 6, "anywhere in the comment").</summary>
    Altitude = 1,

    /// <summary>Any other element: nothing is read across it.</summary>
    Other = 2,
}

/// <summary>
/// The text of a comment (or Mic-E status text) while its elements are lifted out. Lifting an
/// element leaves a boundary where it stood, so that no later element is recognised across it
/// (vectors interpretations.md, "Lifting an element never joins the text either side of it");
/// the comment is the bytes left, in order, with the boundaries ignored.
/// </summary>
internal sealed class CommentText : IReadOnlyList<byte>
{
    private readonly List<byte> _bytes;

    // _cuts[i] is what was lifted straight before byte i (_cuts[Count] straight after the last byte).
    private readonly List<Lifted> _cuts;

    public CommentText(IEnumerable<byte> bytes)
    {
        _bytes = [.. bytes];
        _cuts = [.. Enumerable.Repeat(Lifted.None, _bytes.Count + 1)];
    }

    public CommentText(CommentText other)
    {
        _bytes = [.. other._bytes];
        _cuts = [.. other._cuts];
    }

    public int Count => _bytes.Count;

    public byte this[int index] => _bytes[index];

    /// <summary>What was lifted straight before byte <paramref name="index"/> (or after the last byte).</summary>
    public Lifted CutAt(int index) => _cuts[index];

    /// <summary>What was lifted before each byte from <paramref name="start"/> on, and after the last: one more than the bytes.</summary>
    public Lifted[] CutsFrom(int start) => [.. _cuts.Skip(start)];

    /// <summary>True when the <paramref name="length"/> bytes from <paramref name="start"/> stand whole as sent: nothing was lifted from between them.</summary>
    public bool Whole(int start, int length)
    {
        for (int k = start + 1; k < start + length; k++)
        {
            if (_cuts[k] != Lifted.None)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes <paramref name="length"/> bytes out as a lifted element, leaving a boundary in their place.</summary>
    public void Lift(int start, int length, Lifted kind) => Take(start, length, kind);

    /// <summary>Takes bytes out that were never part of the text proper (padding, a type code, a locator): no boundary of their own.</summary>
    public void Remove(int start, int length) => Take(start, length, Lifted.None);

    public void RemoveAll(byte value)
    {
        for (int i = _bytes.Count - 1; i >= 0; i--)
        {
            if (_bytes[i] == value)
            {
                Remove(i, 1);
            }
        }
    }

    public bool Contains(byte value) => _bytes.Contains(value);

    public byte[] GetRange(int start, int length) => _bytes.GetRange(start, length).ToArray();

    public byte[] ToArray() => [.. _bytes];

    public IEnumerator<byte> GetEnumerator() => _bytes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Take(int start, int length, Lifted kind)
    {
        Lifted merged = kind;
        for (int k = start; k <= start + length; k++)
        {
            merged = (Lifted)Math.Max((byte)merged, (byte)_cuts[k]);
        }

        // Only the boundaries at either end of the range stay; they become one.
        _cuts.RemoveRange(start + 1, length);
        _cuts[start] = merged;
        _bytes.RemoveRange(start, length);
    }
}
