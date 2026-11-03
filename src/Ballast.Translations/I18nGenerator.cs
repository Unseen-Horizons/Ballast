// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ballast.Translations;

/// <summary>
/// Generates a site's translation files from its .resx files: one <c>culture.json</c> file for each culture, and the data script
/// that the site loads before its loader. It replaces a build script that did the same with Node, and writes the same bytes for the
/// same inputs.
/// </summary>
/// <remarks>
/// <para>
/// The base file is English. Each culture file is read with <see cref="ResxReader"/>, its values are trimmed the way JavaScript's
/// <c>trim()</c> trims them, and the texts are written as <c>JSON.stringify(texts, null, 2)</c> writes them, with no line break at the end.
/// The data script holds the English texts, the status map (each <c>status_*</c> key by its code in Pascal case), the culture list and
/// the error-code map.
/// </para>
/// <para>
/// Unlike the script, the generator refuses what it cannot write unambiguously, and writes nothing when it refuses: a key that is in a
/// file twice, two files for one culture, two status keys that give one code, an error-code value that is not text, and an error-code
/// file that is named but missing. A key with no name is left out and reported as a warning.
/// </para>
/// </remarks>
public static class I18nGenerator
{
    private const string EnglishCulture = "en";
    private const string StatusPrefix = "status_";

    /// <summary>
    /// Generates the files of one site.
    /// </summary>
    /// <param name="options">The folders, files and options. Required.</param>
    /// <returns>What was written, and what was noticed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A required option is blank, or the base name holds a character a file name cannot hold.</exception>
    /// <exception cref="DirectoryNotFoundException">The source folder, or the web root when it is checked, does not exist.</exception>
    /// <exception cref="FileNotFoundException">The error-code file is named but does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// A .resx file is not well-formed or is not a resx file, a key is in a file twice, two files are for one culture, two status keys give
    /// one code, or the error-code file is not a JSON object of text. The message lists every problem. Nothing is written.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An HTML file loads the loader script without loading the data script first. The files are written first, as the script was.
    /// </exception>
    public static I18nGenerationResult Generate(I18nGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceDirectory, nameof(options.SourceDirectory));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaseName, nameof(options.BaseName));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputDirectory, nameof(options.OutputDirectory));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataFile, nameof(options.DataFile));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Header, nameof(options.Header));
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"The source folder {options.SourceDirectory} does not exist.");
        }

        List<string> errors = [];
        List<TranslationProblem> warnings = [];

        var sources = Discover(options);
        var texts = new Dictionary<string, OrderedStrings>(StringComparer.Ordinal);
        foreach (var (culture, path) in sources)
        {
            var map = ResxReader.ReadFile(path);
            foreach (var problem in map.Problems)
            {
                if (problem.Kind == TranslationProblemKind.UnnamedEntry)
                {
                    warnings.Add(problem);
                }
                else if (problem.Kind == TranslationProblemKind.DuplicateKey)
                {
                    // The reader keeps the first value; a generated file must not, so the duplicate is refused.
                    var where = problem.Line is int line ? $"{problem.File}:{line}" : problem.File;
                    errors.Add($"{where}: \"{problem.Key}\" appears more than once. Keep one entry.");
                }
                else
                {
                    errors.Add(problem.ToString());
                }
            }

            var cultureTexts = new OrderedStrings();
            foreach (var entry in map.Entries)
            {
                cultureTexts.Set(entry.Key, JsText.Trim(entry.Value));
            }

            texts[culture] = cultureTexts;
        }

        var cultures = sources.Select(source => source.Culture).ToList();
        var english = texts.TryGetValue(EnglishCulture, out var englishTexts) ? englishTexts : new OrderedStrings();
        var statusMap = BuildStatusMap(english, errors);
        var errorCodes = ReadErrorCodes(options.ErrorCodesFile, errors);

        if (errors.Count > 0)
        {
            throw new InvalidDataException("The translation files cannot be generated:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => "  - " + error)));
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var files = new List<string>();
        foreach (var culture in cultures)
        {
            var path = Path.Combine(options.OutputDirectory, culture + ".json");
            var json = new StringBuilder();
            JsText.WriteObject(json, texts[culture].InJsOrder());
            WriteUtf8(path, json.ToString());
            files.Add(Path.GetFullPath(path));
        }

        var dataFile = Path.GetFullPath(options.DataFile);
        Directory.CreateDirectory(Path.GetDirectoryName(dataFile)!);
        WriteUtf8(dataFile, DataScript(options.Header, english, statusMap, cultures, errorCodes));
        files.Add(dataFile);

        if (options.WebRoot is not null && options.LoaderScript is not null)
        {
            CheckLoaders(options.WebRoot, options.LoaderScript, Path.GetFileName(dataFile));
        }

        return new I18nGenerationResult(
            cultures,
            files,
            cultures.Select(culture => texts[culture].Count).ToList(),
            statusMap.Count,
            warnings);
    }

    private static List<(string Culture, string Path)> Discover(I18nGeneratorOptions options)
    {
        var pattern = new Regex(
            "^" + Regex.Escape(options.BaseName) + @"(?:\.([A-Za-z0-9_]+))?\.resx$",
            RegexOptions.CultureInvariant);

        var found = new List<(string Culture, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(options.SourceDirectory).OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal);
        foreach (var file in files)
        {
            var match = pattern.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            var culture = match.Groups[1].Success ? match.Groups[1].Value : EnglishCulture;
            if (!seen.Add(culture))
            {
                throw new InvalidDataException(
                    $"Two files are the texts of the same culture \"{culture}\": {Path.GetFileName(file)} and another. Keep one.");
            }

            found.Add((culture, file));
        }

        // English first; the other cultures in ordinal order, so the result does not depend on the order the file system lists them.
        found.Sort((a, b) =>
        {
            if (a.Culture == EnglishCulture || b.Culture == EnglishCulture)
            {
                return a.Culture == b.Culture ? 0 : a.Culture == EnglishCulture ? -1 : 1;
            }

            return string.CompareOrdinal(a.Culture, b.Culture);
        });
        return found;
    }

    private static OrderedStrings BuildStatusMap(OrderedStrings english, List<string> errors)
    {
        var statusMap = new OrderedStrings();
        var keyOfCode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, _) in english.InJsOrder())
        {
            if (!key.StartsWith(StatusPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var code = PascalCase(key[StatusPrefix.Length..]);
            if (keyOfCode.TryGetValue(code, out var first))
            {
                errors.Add($"\"{first}\" and \"{key}\" both give the status code {code}. Keep one of the two keys.");
                continue;
            }

            keyOfCode[code] = key;
            statusMap.Set(code, key);
        }

        return statusMap;
    }

    private static OrderedStrings ReadErrorCodes(string? path, List<string> errors)
    {
        var codes = new OrderedStrings();
        if (path is null)
        {
            return codes;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The error-code file {path} does not exist.", path);
        }

        using var document = ParseErrorCodes(path);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} is not a JSON object of error codes to keys.");
            return codes;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"\"{property.Name}\" in {path} is not text ({property.Value.ValueKind}). The error-code map holds text only.");
                continue;
            }

            if (codes.ContainsKey(property.Name))
            {
                errors.Add($"\"{property.Name}\" appears more than once in {path}.");
                continue;
            }

            try
            {
                codes.Set(property.Name, property.Value.GetString() ?? string.Empty);
            }
            catch (InvalidOperationException)
            {
                // A lone surrogate escape (for example \ud800) is text that JavaScript reads but .NET cannot hold as a string.
                errors.Add($"\"{property.Name}\" in {path} is not valid text: it holds an unpaired surrogate escape.");
            }
        }

        return codes;
    }

    private static JsonDocument ParseErrorCodes(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static string DataScript(string header, OrderedStrings english, OrderedStrings statusMap, IReadOnlyList<string> cultures, OrderedStrings errorCodes)
    {
        var script = new StringBuilder();
        script.Append(header).Append('\n');
        script.Append("window.__I18N_DATA = ");
        JsText.WriteObject(script, english.InJsOrder());
        script.Append(";\n");
        script.Append("window.__STATUS_MAP = ");
        JsText.WriteObject(script, statusMap.InJsOrder());
        script.Append(";\n");
        script.Append("window.__I18N_LANGS = ");
        JsText.WriteArray(script, cultures);
        script.Append(";\n");
        script.Append("window.ERROR_CODE_MAP = ");
        JsText.WriteObject(script, errorCodes.InJsOrder());
        script.Append(";\n");
        return script.ToString();
    }

    private static void CheckLoaders(string webRoot, string loaderScript, string dataFileName)
    {
        if (!Directory.Exists(webRoot))
        {
            throw new DirectoryNotFoundException($"The web root {webRoot} does not exist.");
        }

        List<string> problems = [];
        CheckFolder(webRoot, webRoot, loaderScript, dataFileName, problems);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{problems.Count} HTML file(s) load {loaderScript} without loading {dataFileName} before it:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem)));
        }
    }

    private static void CheckFolder(string root, string folder, string loaderScript, string dataFileName, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!file.EndsWith(".html", StringComparison.Ordinal))
            {
                continue;
            }

            var html = File.ReadAllText(file);
            var at = html.IndexOf(loaderScript, StringComparison.Ordinal);
            if (at >= 0 && !html[..at].Contains(dataFileName, StringComparison.Ordinal))
            {
                problems.Add(Path.GetRelativePath(root, file));
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(folder).OrderBy(name => name, StringComparer.Ordinal))
        {
            if (Path.GetFileName(sub) != "i18n")
            {
                CheckFolder(root, sub, loaderScript, dataFileName, problems);
            }
        }
    }

    private static string PascalCase(string snake)
    {
        var parts = snake.Split('_');
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            builder.Append(part[..1].ToUpperInvariant()).Append(part[1..].ToLowerInvariant());
        }

        return builder.ToString();
    }

    private static void WriteUtf8(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
