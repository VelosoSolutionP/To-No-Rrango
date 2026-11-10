using System.Globalization;

namespace ToNoRango.Domain.Establishments;

/// <summary>One opening window. A window whose close is before its open runs past midnight.</summary>
public sealed record OpeningWindow(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

/// <summary>
/// Weekly opening hours kept as text, such as "1=11:00-15:00,18:00-23:30;5=18:00-02:00".
/// Days go from 0 (Sunday) to 6 (Saturday). No text means the store never closes.
/// </summary>
public static class OpeningSchedule
{
    public static IReadOnlyList<OpeningWindow> Parse(string? text)
    {
        var windows = new List<OpeningWindow>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return windows;
        }

        foreach (var dayPart in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = dayPart.Split('=', 2, StringSplitOptions.TrimEntries);

            if (pieces.Length != 2 || !int.TryParse(pieces[0], out var day) || day is < 0 or > 6)
            {
                throw new ArgumentException($"Horário inválido: '{dayPart}'. Use dia=HH:mm-HH:mm, com dia de 0 (domingo) a 6 (sábado).");
            }

            foreach (var range in pieces[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var times = range.Split('-', 2, StringSplitOptions.TrimEntries);

                if (times.Length != 2
                    || !TimeOnly.TryParseExact(times[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                    || !TimeOnly.TryParseExact(times[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes)
                    || opens == closes)
                {
                    throw new ArgumentException($"Faixa de horário inválida: '{range}'. Use HH:mm-HH:mm.");
                }

                windows.Add(new OpeningWindow((DayOfWeek)day, opens, closes));
            }
        }

        return windows;
    }

    /// <summary>Rewrites the text in a single, ordered form, or null when there is no window.</summary>
    public static string? Normalize(string? text)
    {
        var windows = Parse(text);

        if (windows.Count == 0)
        {
            return null;
        }

        return string.Join(';', windows
            .GroupBy(window => window.Day)
            .OrderBy(group => group.Key)
            .Select(group => $"{(int)group.Key}=" + string.Join(',', group
                .OrderBy(window => window.Opens)
                .Select(window => $"{window.Opens:HH\\:mm}-{window.Closes:HH\\:mm}"))));
    }

    public static bool IsOpen(string? text, DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        var windows = Parse(text);

        if (windows.Count == 0)
        {
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var today = local.DayOfWeek;
        var yesterday = (DayOfWeek)(((int)today + 6) % 7);

        return windows.Any(window =>
            window.Opens < window.Closes
                ? window.Day == today && time >= window.Opens && time < window.Closes
                : (window.Day == today && time >= window.Opens) || (window.Day == yesterday && time < window.Closes));
    }

    /// <summary>The next moment the store opens, or null when it never closes or has no window.</summary>
    public static DateTimeOffset? NextOpening(string? text, DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        var windows = Parse(text);

        if (windows.Count == 0)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);

        for (var offset = 0; offset <= 7; offset++)
        {
            var date = DateOnly.FromDateTime(local.DateTime).AddDays(offset);
            var candidates = windows
                .Where(window => window.Day == date.DayOfWeek)
                .Select(window => date.ToDateTime(window.Opens))
                .Where(start => start > local.DateTime)
                .OrderBy(start => start)
                .ToList();

            if (candidates.Count > 0)
            {
                var start = candidates[0];
                return new DateTimeOffset(start, timeZone.GetUtcOffset(start)).ToUniversalTime();
            }
        }

        return null;
    }

    public static string Describe(string? text)
    {
        var windows = Parse(text);

        if (windows.Count == 0)
        {
            return "Aberto todos os dias, a qualquer hora.";
        }

        string[] names = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

        return string.Join("\n", windows
            .GroupBy(window => window.Day)
            .OrderBy(group => ((int)group.Key + 6) % 7)
            .Select(group => $"{names[(int)group.Key]}: " + string.Join(" e ", group
                .OrderBy(window => window.Opens)
                .Select(window => $"{window.Opens:HH\\:mm} às {window.Closes:HH\\:mm}"))));
    }
}
