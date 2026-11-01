// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.Email;

/// <summary>
/// The mail settings, read once from a configuration section (<see cref="DefaultSectionName"/> unless the app names another).
/// The API key switches sending on: with a key, mail goes out. Without one, <see cref="EmailService"/> logs the mail in
/// Development and reports <see cref="EmailSendResult.NotConfigured"/> elsewhere. A key needs a sender address
/// (<c>{Section}:FromEmail</c>). Blank values count as unset, as an appsettings file ships them empty.
/// </summary>
public sealed class MailSettings
{
    /// <summary>The configuration section that holds the settings when the app does not name another.</summary>
    public const string DefaultSectionName = "SendGrid";

    /// <summary>
    /// Creates the settings from values. Blank values count as unset. Nothing is checked here: <see cref="IsConfigured"/> and
    /// <see cref="MissingSettings"/> say what is set, and <see cref="Read"/> is the checked way to build them from configuration.
    /// </summary>
    /// <param name="apiKey">The SendGrid API key, or null for none. Setting one switches sending on.</param>
    /// <param name="fromEmail">The sender address. It must be verified with the provider.</param>
    /// <param name="fromName">The sender's display name, or null for none.</param>
    /// <param name="sectionName">The configuration section these values came from, used in the setting names of a message.</param>
    /// <param name="logInsteadOfSending">
    /// Whether to write the mail to the log when sending is off. Null means the default: on in Development, off elsewhere.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="sectionName"/> is null, empty or blank.</exception>
    public MailSettings(string? apiKey, string? fromEmail, string? fromName = null, string sectionName = DefaultSectionName, bool? logInsteadOfSending = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        SectionName = sectionName.Trim();
        ApiKey = Configured(apiKey);
        FromEmail = Configured(fromEmail);
        FromName = Configured(fromName);
        LogInsteadOfSending = logInsteadOfSending;
    }

    /// <summary>The configuration section the settings are read from. The setting names in messages start with it.</summary>
    public string SectionName { get; }

    /// <summary>The SendGrid API key, or null when none is set.</summary>
    public string? ApiKey { get; }

    /// <summary>The sender address, or null when none is set. It must be verified with the provider.</summary>
    public string? FromEmail { get; }

    /// <summary>The sender's display name, or null when none is set. The e-mail then has no name for its sender.</summary>
    public string? FromName { get; }

    /// <summary>
    /// Whether to write the mail to the log when sending is off, or null for the default: on in Development, off elsewhere.
    /// </summary>
    public bool? LogInsteadOfSending { get; }

    /// <summary>True when an API key is set. The key is what switches sending on.</summary>
    public bool IsConfigured => ApiKey is not null;

    /// <summary>
    /// The settings that are not set while the API key is. A key without a sender lists <c>{Section}:FromEmail</c>. Empty when
    /// the key is not set, because then there is nothing to be missing.
    /// </summary>
    public IReadOnlyList<string> MissingSettings
    {
        get
        {
            List<string> missing = [];
            if (IsConfigured && FromEmail is null)
            {
                missing.Add($"{SectionName}:FromEmail");
            }

            return missing;
        }
    }

    /// <summary>
    /// Reads the settings from a configuration section, and checks them: a key without a sender is refused, so a
    /// misconfiguration stops the app at startup rather than failing every send.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="sectionName">The section to read: <c>SendGrid</c> unless the app names another.</param>
    /// <param name="defaultFromName">The sender name to use when the section has none, or null for none.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="sectionName"/> is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The API key is set and the sender address is not, or <c>LogInsteadOfSending</c> is set to something other than true or
    /// false. The message names the setting.
    /// </exception>
    public static MailSettings Read(IConfiguration configuration, string sectionName = DefaultSectionName, string? defaultFromName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName.Trim());
        var fromName = section["FromName"];
        var settings = new MailSettings(
            section["ApiKey"],
            section["FromEmail"],
            string.IsNullOrWhiteSpace(fromName) ? defaultFromName : fromName,
            sectionName,
            ParseLogSetting(section["LogInsteadOfSending"], settingName: $"{sectionName.Trim()}:LogInsteadOfSending"));

        if (settings.MissingSettings.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", settings.MissingSettings)} is not set, but {settings.SectionName}:ApiKey is. SendGrid needs a verified sender address to send from. Set it in configuration, or remove the API key to turn sending off.");
        }

        return settings;
    }

    private static bool? ParseLogSetting(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value.Trim(), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{settingName} must be true or false, not \"{value.Trim()}\".");
    }

    private static string? Configured(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
