using System;
using System.Globalization;

namespace FormatWheel.Data;

// Маленькая локализация расширения. Код языка берём так же, как приложение.
internal static class L
{
    private static readonly bool Ru =
        (Environment.GetEnvironmentVariable("FORMATWHEEL_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        .StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static string T(string en, string ru) => Ru ? ru : en;

    public static string Name => T("Data (JSON, YAML, XML…)", "Данные (JSON, YAML, XML…)");
    public static string ForApi => T("APIs, JS", "API, JS");
    public static string ForConfigs => T("configs", "конфиги");
    public static string Markup => T("markup", "разметка");
    public static string ForSettings => T("settings", "настройки");
    public static string ForExcel => T("table, Excel", "таблица, Excel");
    public static string Pretty => T("Pretty", "Красиво");
    public static string WithIndents => T("with indents", "с отступами");
    public static string Compact => T("Compact", "Сжато");
    public static string OneLine => T("one line", "в одну строку");
    public static string AsIs => T("As is", "Как есть");
    public static string KeepOrder => T("keep key order", "порядок ключей");
    public static string SortKeys => T("A→Z", "А→Я");
    public static string SortHint => T("sort keys", "сортировать ключи");
}
