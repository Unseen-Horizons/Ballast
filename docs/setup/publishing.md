# Publishing setup: GitHub Packages

This guide sets up what releases Ballast. Do it once, before the first release. The releases themselves are described
in [releasing.md](../releasing.md).

Ballast is published in two places, both on GitHub, in the organisation `Unseen-Horizons`:

- **GitHub Packages**, the organisation's NuGet feed: `https://nuget.pkg.github.com/Unseen-Horizons/index.json`. Projects
  restore from here. GitHub asks for a sign-in on every download from this feed, even for a public repository's
  packages, so a project that restores from it needs a token (Part 2).
- **The GitHub Release** of each version, on https://github.com/Unseen-Horizons/Ballast/releases. Every package is attached
  to it. Anyone can download these files without signing in.

Nothing is published to nuget.org.

**Labels on the screens.** The names of buttons and menus below come from GitHub's documentation and may differ slightly
on your screen. If a label does not match, look for the nearest one.

---

## Part 1: The `release` environment

The environment is what makes the owner approve each release. Create it before any tag is pushed.

1. Open https://github.com/Unseen-Horizons/Ballast. Click **Settings** in the top bar.
2. In the left menu, click **Environments**, then **New environment**.
3. In **Name**, type `release`. Click **Configure environment**.
4. Under **Deployment protection rules**, tick **Required reviewers**. Type your GitHub username, and click it in the list.
5. Under **Deployment branches and tags**, choose **Selected branches and tags**. Click **Add deployment tag rule**. In
   **Name pattern**, type `v*`. Click **Add rule**.
6. Click **Save protection rules**.

**Check:** the **Environments** page lists `release`, and its page shows **Required reviewers** with your username.

**Warning:** if the `release` environment does not exist when a tag is pushed, GitHub creates it with no reviewer, and the
publish would run without your approval. Do this before any tag.

No secret is needed. The release workflow pushes with the token GitHub gives each run.

---

## Part 2: A token for restoring

Every project that restores Ballast from the feed needs a token with the `read:packages` scope. GitHub Packages accepts
only a classic personal access token for this.

1. Open https://github.com/settings/tokens and click **Generate new token**, then **Generate new token (classic)**.
2. **Note:** `packages read`. **Expiration:** your choice. Write the date down: restores fail once it passes.
3. Tick **read:packages** only.
4. Click **Generate token**, and copy it. GitHub shows it once.

Then give it to each place that restores:

- **Your computer:** set it as a user environment variable named `GITHUB_PACKAGES_TOKEN`. In PowerShell:

  ```powershell
  [Environment]::SetEnvironmentVariable('GITHUB_PACKAGES_TOKEN', '<the token>', 'User')
  ```

  Open a new terminal (and restart Visual Studio) afterwards, so they read it.
- **Each repository whose GitHub Actions restore Ballast:** a repository secret named `PACKAGES_TOKEN` with the same
  token (**Settings**, **Secrets and variables**, **Actions**, **New repository secret**).

**Check:** in a new terminal, `dotnet restore` in an application that uses Ballast finishes without `401` or `NU1301`.

---

## Part 3: After the first release

1. Open https://github.com/orgs/Unseen-Horizons/packages. Each Ballast package is listed.
2. Open one. Its page links to the Ballast repository. A package linked to a public repository is public.

If a package shows no repository link, open its **Package settings** and connect it to `Unseen-Horizons/Ballast`.
