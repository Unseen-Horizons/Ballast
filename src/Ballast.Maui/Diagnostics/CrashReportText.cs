// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Ballast.Text;

namespace Ballast.Maui.Diagnostics;

/// <summary>What a crash report is made from. Plain values, so the text it produces is testable without a device.</summary>
/// <param name="TimeUtc">When the app went down, in UTC.</param>
/// <param name="AppVersion">The crashing build's version name.</param>
/// <param name="AppBuild">The crashing build's number.</param>
/// <param name="Platform">The platform's name.</param>
/// <param name="OsVersion">The operating system's version.</param>
/// <param name="Source">Which hook caught it.</param>
/// <param name="Exception">What was thrown. Its message and stack are scrubbed before they are written.</param>
/// <param name="Recent">The handled failures that came before, oldest first.</param>
public sealed record CrashReportInput(
    DateTime TimeUtc,
    string AppVersion,
    string AppBuild,
    string Platform,
    string OsVersion,
    string Source,
    Exception? Exception,
    IReadOnlyList<RecentFailure> Recent);

/// <summary>
/// The text of a crash report: what it says, how it is cut to fit, and what is taken out of it first.
/// </summary>
/// <remarks>
/// <para>
/// A report is sent only when the person says so, through the app's ordinary feedback endpoint, whose message is at most
/// <see cref="MaxLength"/> characters. It holds the exception (type, message, stack, inner exceptions), the build and operating system, the
/// time, and the recent handled failures, and nothing else: no account, no email, no place. An exception's message and stack are the one place a
/// person's data can slip in, because they quote whatever the failing code was holding, so <see cref="Scrub"/> runs over every free-text part.
/// </para>
/// <para>
/// The emails, phone numbers and links are taken out by <see cref="PersonalDataMasker"/> from <c>Ballast.Text</c>. The rules for positions, paths and
/// identifiers that only a crash quotes are this class's own, and they run first, so a coordinate keeps its own marker.
/// </para>
/// <para>Pure: no platform types, so each rule is pinned by a test.</para>
/// </remarks>
public static partial class CrashReportText
{
    /// <summary>The longest feedback message (the endpoint's own limit). A test in the app keeps the two equal.</summary>
    public const int MaxLength = 4000;

    /// <summary>The longest feedback subject (the endpoint's own limit).</summary>
    public const int MaxSubjectLength = 200;

    private const int MaxMessageLength = 300;
    private const int MaxFrameLength = 160;
    private const int MaxChain = 5;
    private const string Truncated = "\n[cut to fit]";

    // How many stack frames each exception keeps, widest first: (the crashing exception, each inner one). The report tries the widest budget
    // with every recent failure, then drops the oldest failures one by one, then moves to the next budget, so the stack is shortened last.
    private static readonly (int Outer, int Inner)[] StackBudgets = [(30, 12), (20, 8), (12, 4), (6, 2), (3, 0)];

    [GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s""'<>)\]}]+")]
    private static partial Regex AnyUri();

    [GeneratedRegex(@"-?\d{1,3}\.\d{2,}\s*[,;]\s*-?\d{1,3}\.\d{2,}")]
    private static partial Regex CoordinatePair();

    [GeneratedRegex(@"\b(lat|lon|lng|latitude|longitude|fromplace|toplace|planfrom|planto)\s*[=:]\s*-?[\d.,]+", RegexOptions.IgnoreCase)]
    private static partial Regex PositionValue();

    [GeneratedRegex(@"-?\d{1,3}\.\d{4,}")]
    private static partial Regex LongDecimal();

    [GeneratedRegex(@"\bin (?:[A-Za-z]:)?[^\s:]*[\\/]([^\\/\s:]+):line (\d+)")]
    private static partial Regex StackFilePath();

    [GeneratedRegex(@"(?<![\w.])(?:[A-Za-z]:)?(?:[\\/][^\\/\s'""<>:|?*]+){3,}")]
    private static partial Regex AbsolutePath();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidText();

    /// <summary>
    /// Takes out what must not leave the phone. A web address becomes its scheme and host, and any other address becomes <c>[uri]</c>. Coordinates,
    /// positions given as <c>lat=…</c>, long decimals, absolute file paths and GUIDs are replaced by a marker, and then the masker hides emails,
    /// phone numbers and links. Over-scrubbing a harmless number costs a little detail; under-scrubbing costs a person's position, so the rules
    /// lean to the first.
    /// </summary>
    /// <param name="text">The text to scrub. Null or empty gives the empty string.</param>
    /// <returns>The scrubbed text.</returns>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = AnyUri().Replace(text, UriReplacement);
        result = CoordinatePair().Replace(result, "[coords]");
        result = PositionValue().Replace(result, m => $"{m.Groups[1].Value}=[position]");
        result = LongDecimal().Replace(result, "[number]");
        result = StackFilePath().Replace(result, "in $1:line $2");
        result = AbsolutePath().Replace(result, "[path]");
        result = GuidText().Replace(result, "[id]");
        return PersonalDataMasker.Mask(result);
    }

    private static string UriReplacement(Match match)
    {
        var raw = match.Value;
        if ((raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host))
            return $"{uri.Scheme}://{uri.Host}/…";

        return "[uri]";
    }

    /// <summary>
    /// <paramref name="text"/> cut to at most <paramref name="max"/> characters, with an ellipsis where it was cut. Never splits a surrogate pair.
    /// </summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="max">The most characters the result may have.</param>
    /// <returns>The text, cut when it was longer.</returns>
    public static string Clip(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        if (max <= 1)
            return text[..Math.Max(max, 0)];

        var keep = max - 1;
        if (char.IsHighSurrogate(text[keep - 1]))
            keep--;
        return text[..keep] + "…";
    }

    /// <summary>
    /// The report, at most <paramref name="maxLength"/> characters. When the full text is too long, the oldest recent failures go first, then stack
    /// frames, and only as a last resort is the text cut, which the text says.
    /// </summary>
    /// <param name="input">What the report is made from.</param>
    /// <param name="maxLength">The most characters the report may have.</param>
    /// <returns>The report text.</returns>
    public static string Compose(CrashReportInput input, int maxLength = MaxLength)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (var (outer, inner) in StackBudgets)
        {
            for (var keep = input.Recent.Count; keep >= 0; keep--)
            {
                var text = Build(input, outer, inner, keep);
                if (text.Length <= maxLength)
                    return text;
            }
        }

        var smallest = Build(input, StackBudgets[^1].Outer, StackBudgets[^1].Inner, 0);
        var room = maxLength - Truncated.Length;
        if (room <= 0)
            return Clip(smallest, maxLength);

        var cut = room;
        if (char.IsHighSurrogate(smallest[cut - 1]))
            cut--;
        return smallest[..cut] + Truncated;
    }

    /// <summary>The feedback subject for a report: its first line, cut to the subject's limit.</summary>
    /// <param name="reportText">The report text.</param>
    /// <returns>The subject.</returns>
    public static string SubjectFor(string reportText)
    {
        var newline = reportText.IndexOfAny(['\r', '\n']);
        var first = newline < 0 ? reportText : reportText[..newline];
        return Clip(first.Trim(), MaxSubjectLength);
    }

    private static string Build(CrashReportInput input, int outerFrames, int innerFrames, int keepFailures)
    {
        var text = new StringBuilder();
        var type = input.Exception?.GetType().FullName ?? "unknown";
        text.Append("Crash: ").AppendLine(Clip(Scrub(type), 120));
        text.Append("App: ").Append(Clip(Scrub(input.AppVersion), 40)).Append(" (").Append(Clip(Scrub(input.AppBuild), 20)).AppendLine(")");
        text.Append("Platform: ").Append(Clip(Scrub(input.Platform), 40)).Append(' ').AppendLine(Clip(Scrub(input.OsVersion), 40));
        text.Append("Time (UTC): ").AppendLine(input.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        text.Append("Caught by: ").AppendLine(Clip(Scrub(input.Source), 80));
        text.AppendLine();

        AppendExceptions(text, input.Exception, outerFrames, innerFrames);

        if (keepFailures > 0)
        {
            var skipped = input.Recent.Count - keepFailures;
            text.AppendLine();
            text.Append("Recent handled failures (oldest first, ").Append(keepFailures).Append(" of ").Append(input.Recent.Count).AppendLine("):");
            foreach (var failure in input.Recent.Skip(skipped))
            {
                text.Append(failure.TimeUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("Z ").Append(failure.Source);
                if (failure.Summary.Length > 0)
                    text.Append(": ").Append(failure.Summary);
                if (failure.Code is not null)
                    text.Append(" code=").Append(failure.Code);
                if (failure.TraceId is not null)
                    text.Append(" trace=").Append(failure.TraceId);
                if (failure.ExceptionType is not null)
                    text.Append(" type=").Append(failure.ExceptionType);
                text.AppendLine();
            }
        }

        return text.ToString().TrimEnd();
    }

    private static void AppendExceptions(StringBuilder text, Exception? root, int outerFrames, int innerFrames)
    {
        if (root is null)
        {
            text.AppendLine("No exception was available.");
            return;
        }

        var chain = new List<Exception>();
        Collect(root, chain);
        var shown = chain.Take(MaxChain).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var ex = shown[i];
            text.Append(i == 0 ? string.Empty : "Caused by ")
                .Append(Clip(Scrub(ex.GetType().FullName), 120))
                .Append(": ")
                .AppendLine(Clip(Scrub(ex.Message), MaxMessageLength));
            AppendFrames(text, ex.StackTrace, i == 0 ? outerFrames : innerFrames);
        }

        if (chain.Count > shown.Count)
            text.Append('(').Append(chain.Count - shown.Count).AppendLine(" more inner exceptions not shown)");
    }

    /// <summary>The exception, then what it wraps. An aggregate lists each of its children before going deeper.</summary>
    private static void Collect(Exception ex, List<Exception> into)
    {
        into.Add(ex);
        if (into.Count > 20)
            return;

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                Collect(inner, into);
        }
        else if (ex.InnerException is { } single)
        {
            Collect(single, into);
        }
    }

    private static void AppendFrames(StringBuilder text, string? stackTrace, int budget)
    {
        if (budget <= 0 || string.IsNullOrWhiteSpace(stackTrace))
            return;

        var frames = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var frame in frames.Take(budget))
            text.Append("   ").AppendLine(Clip(Scrub(frame), MaxFrameLength));

        if (frames.Length > budget)
            text.Append("   … ").Append(frames.Length - budget).AppendLine(" more frames");
    }
}
