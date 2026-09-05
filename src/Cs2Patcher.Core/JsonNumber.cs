using System.Text.Json.Nodes;

namespace Cs2Patcher.Core;

/// <summary>
/// A JsonValue is bound to the CLR type it was built from, so one created from an int
/// refuses GetValue&lt;double&gt;() even though its kind is Number. Try each numeric type
/// rather than assuming.
/// </summary>
internal static class JsonNumber
{
    public static bool TryRead(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue jv) return false;

        if (jv.TryGetValue<double>(out var d)) { value = d; return true; }
        if (jv.TryGetValue<int>(out var i)) { value = i; return true; }
        if (jv.TryGetValue<long>(out var l)) { value = l; return true; }
        if (jv.TryGetValue<float>(out var f)) { value = f; return true; }
        if (jv.TryGetValue<decimal>(out var m)) { value = (double)m; return true; }

        return false;
    }

    public static double Read(JsonNode? node, double fallback = 0) =>
        TryRead(node, out var v) ? v : fallback;
}
