using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FormatWheel.Data;

// Общее дерево, через которое идут все конвертации:
// объект — Obj (порядок ключей сохраняется), массив — List<object?>,
// скаляры — string, long, decimal, double, bool или null.
internal sealed class Obj : OrderedDictionary<string, object?>
{
    // повторный ключ не роняет конвертацию, последний побеждает
    public void Put(string key, object? value) => this[key] = value;
}

internal static partial class Tree
{
    // только «настоящие» числа: 007 и +1 остаются строками, чтобы не терять почтовые индексы и телефоны
    [GeneratedRegex(@"^-?(0|[1-9]\d*)$")]
    private static partial Regex IntRx();

    [GeneratedRegex(@"^-?(0|[1-9]\d*)(\.\d+)?([eE][-+]?\d+)?$")]
    private static partial Regex NumberRx();

    // тип для текста без кавычек (YAML без кавычек, CSV, XML)
    public static object? Infer(string? s)
    {
        if (s is null) return null;
        switch (s)
        {
            case "" or "~" or "null" or "Null" or "NULL": return null;
            case "true" or "True" or "TRUE": return true;
            case "false" or "False" or "FALSE": return false;
        }
        if (IntRx().IsMatch(s) && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
        if (NumberRx().IsMatch(s))
        {
            if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal m)) return m;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        }
        return s;
    }

    // скаляр в текст (XML, CSV, YAML)
    public static string Text(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static object? Sorted(object? value) => value switch
    {
        Obj o => o.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                  .Aggregate(new Obj(), (acc, kv) => { acc.Put(kv.Key, Sorted(kv.Value)); return acc; }),
        List<object?> list => list.Select(Sorted).ToList(),
        _ => value
    };
}
