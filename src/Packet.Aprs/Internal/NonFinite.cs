using System.Collections;
using System.Reflection;

namespace Packet.Aprs.Internal;

/// <summary>
/// Whether any number of a data record, in any field of any depth, is NaN or an infinity. Found by
/// walking the record's public settable properties (not the ones derived from them), so a field added later is covered. Only asked once a
/// record is already refused, to name the refusal: a number that is not a number is
/// <c>not-a-number</c> wherever it is, even in a field the report has no place for.
/// </summary>
internal static class NonFinite
{
    public static bool Present(object? node, int depth = 0)
    {
        if (node is null || depth > 8)
        {
            return false;
        }

        switch (node)
        {
            case double d:
                return !double.IsFinite(d);
            case string or AprsPacket:
                return false;
            case IEnumerable items:
                return items.Cast<object?>().Any(i => Present(i, depth + 1));
        }

        Type type = node.GetType();
        if (type.Namespace != "Packet.Aprs")
        {
            return false;
        }

        foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length == 0 && p.GetMethod is not null && p.SetMethod is not null && Present(p.GetValue(node), depth + 1))
            {
                return true;
            }
        }

        return false;
    }
}
