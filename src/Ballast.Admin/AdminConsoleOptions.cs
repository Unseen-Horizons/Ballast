// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Admin;

/// <summary>
/// The choices for the headers of a staff console's static pages. The defaults give a console that loads its own script, style and
/// fonts from its own origin, with no inline script, and that no search engine indexes. Every source an app adds is one more origin
/// the page may load from, so each should be named on purpose.
/// </summary>
public sealed class AdminConsoleOptions
{
    /// <summary>
    /// The path of the console's static files. The rule applies to the path and everything under it, by segment. The default is
    /// <c>/admin</c>. It must start with '/'.
    /// </summary>
    public string Path { get; set; } = "/admin";

    /// <summary>
    /// The <c>Cache-Control</c> header of the console's files. The default is <c>no-cache</c>, which allows a cache but makes it check
    /// the file first, so a deploy takes effect at once.
    /// </summary>
    public string CacheControl { get; set; } = "no-cache";

    /// <summary>
    /// Whether the style sheets may hold inline styles (<c>'unsafe-inline'</c> in <c>style-src</c>). The default is true, because the
    /// consoles write inline style attributes. Scripts are never allowed inline.
    /// </summary>
    public bool AllowInlineStyles { get; set; } = true;

    /// <summary>
    /// Extra origins or keywords for <c>style-src</c>, after <c>'self'</c>. Each must hold no white space, no ';', no ',' and not <c>*</c>.
    /// </summary>
    public IList<string> StyleSources { get; } = [];

    /// <summary>
    /// Extra origins or keywords for <c>font-src</c>, after <c>'self'</c>. The same rules as <see cref="StyleSources"/> apply.
    /// </summary>
    public IList<string> FontSources { get; } = [];

    /// <summary>
    /// Extra origins or keywords for <c>img-src</c>, after <c>'self' data:</c>. The same rules as <see cref="StyleSources"/> apply.
    /// </summary>
    public IList<string> ImageSources { get; } = [];

    /// <summary>
    /// Extra origins or keywords for <c>connect-src</c>, after <c>'self'</c>. The same rules as <see cref="StyleSources"/> apply.
    /// </summary>
    public IList<string> ConnectSources { get; } = [];
}
