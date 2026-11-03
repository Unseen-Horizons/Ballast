// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Build.Framework;

namespace Ballast.Translations;

/// <summary>
/// The MSBuild task that runs <see cref="I18nGenerator"/> during a build. The package's <c>build/Ballast.Translations.targets</c> runs it
/// when a project sets <c>BallastI18nBaseName</c>. It logs what the generator noticed as warnings, and a refusal as an error that fails the build.
/// </summary>
/// <remarks>
/// The task is in the same assembly as the library. Its build-time dependency on the MSBuild task library is private to the package, so an
/// app that only reads the library's types does not take it on.
/// </remarks>
public sealed class GenerateI18nTask : Microsoft.Build.Utilities.Task
{
    /// <summary>The folder with the .resx files. Required.</summary>
    [Required]
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>The base name of the files, such as <c>SharedResources</c>. Required.</summary>
    [Required]
    public string BaseName { get; set; } = string.Empty;

    /// <summary>The folder the <c>culture.json</c> files are written to. Required.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>The data script to write. Required.</summary>
    [Required]
    public string DataFile { get; set; } = string.Empty;

    /// <summary>The error-code JSON file, or empty for none.</summary>
    public string ErrorCodesFile { get; set; } = string.Empty;

    /// <summary>The web root whose HTML files are checked, or empty to skip the check.</summary>
    public string WebRoot { get; set; } = string.Empty;

    /// <summary>The loader script the HTML files load, such as <c>/js/i18n.js</c>, or empty to skip the check.</summary>
    public string LoaderScript { get; set; } = string.Empty;

    /// <summary>The first line of the script, or empty for the library's default.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Runs the generator. A refusal, or a file that cannot be read or written, is logged as an error and fails the build.
    /// </summary>
    /// <returns>True when the files were written.</returns>
    public override bool Execute()
    {
        try
        {
            var options = new I18nGeneratorOptions
            {
                SourceDirectory = SourceDirectory,
                BaseName = BaseName,
                OutputDirectory = OutputDirectory,
                DataFile = DataFile,
                ErrorCodesFile = Blank(ErrorCodesFile),
                WebRoot = Blank(WebRoot),
                LoaderScript = Blank(LoaderScript),
            };
            if (Blank(Header) is string header)
            {
                options.Header = header;
            }

            var result = I18nGenerator.Generate(options);
            foreach (var warning in result.Warnings)
            {
                Log.LogWarning(warning.ToString());
            }

            var counts = string.Join(", ", result.Cultures.Select((culture, i) => $"{culture}.json ({result.Keys[i]})"));
            Log.LogMessage(MessageImportance.Normal,
                $"Generated {counts}, statusMap ({result.StatusKeys}), langs {string.Join(",", result.Cultures.Select(c => "\"" + c + "\""))}.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentException)
        {
            Log.LogError(exception.Message);
            return false;
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
