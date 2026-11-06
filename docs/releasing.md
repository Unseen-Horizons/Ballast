# Releasing Ballast

A release puts the packages on GitHub Packages (the organisation's NuGet feed) and attaches them to a GitHub Release.
Only the release workflow ([release.yml](../.github/workflows/release.yml)) publishes, and only after the owner approves
the run. No package is pushed by hand.

A version on the feed is never replaced: a mistake means a new version. Check each step before you go on to the next one.

The one-time setup is in [setup/publishing.md](setup/publishing.md). Do that before the first release.

## What a release is

- All the packages share one version. It is set in `VersionPrefix` and `VersionSuffix` in [Directory.Build.props](../Directory.Build.props).
- The release tag is `v` followed by that version. For version `0.1.0` with the suffix `preview.3` the tag is
  `v0.1.0-preview.3`. A stable release has no suffix, so its tag is for example `v0.2.0`.
- The build job fails, and nothing is published, unless the tag and the package version are the same.
- A tag with a suffix makes a Release marked as a pre-release.

## 1. Set the version

1. Open `Directory.Build.props`. Set `VersionPrefix` and `VersionSuffix`. Leave `VersionSuffix` empty for a stable release.
2. Open `CHANGELOG.md`. Add a section for the new version above the older ones, newest first, and say what changed.
3. Run the four commands in [CONTRIBUTING.md](../CONTRIBUTING.md). All four must pass.
4. Commit the version and the changelog to `main`, and push `main`. Pushing a branch does not start a release.

## 2. Push the tag

```bash
git tag v0.1.0-preview.3
git push origin v0.1.0-preview.3
```

Tag the commit on `main` that you pushed in step 1.4. The push starts the Release run on GitHub.

## 3. Approve the publish

1. On github.com, open the repository, then **Actions**, then the **Release** run for your tag.
2. The **Build, test, pack and check the tag** job runs first.
3. When it passes, the **Publish to GitHub Packages and the GitHub Release** job waits. Click **Review deployments**, tick
   **release**, then click **Approve and deploy**. Nothing is published before this click.

If the build job fails, nothing was published. Fix the cause, then delete the tag on GitHub and on your computer, and
tag again:

```bash
git push origin --delete v0.1.0-preview.3
git tag -d v0.1.0-preview.3
```

If the publish job fails partway, use **Re-run failed jobs**. Packages already on the feed are skipped, and the rest are
pushed. If the Release was already made, delete it on GitHub before the re-run, because the step makes a new one.

## 4. Check the release

1. https://github.com/orgs/Unseen-Horizons/packages lists each package with the new version.
2. https://github.com/Unseen-Horizons/Ballast/releases shows the release, with every `.nupkg` and `.snupkg` attached.

## Things to know

- **The push skips a version that is already on the feed** (`--skip-duplicate`). A green run may have pushed nothing
  new: read the log.
- **A bad version stays.** Release a new version with the fix. Never reuse a version number: a project that already
  restored it keeps the old files in its cache.
- **The release environment must exist with a reviewer.** Otherwise GitHub creates it with no reviewer when the tag is
  pushed, and the publish runs without a click.
- **The tag must point at a commit that has `release.yml`.** The workflow file is read from the tagged commit.
- **No key is stored.** The push uses the token GitHub gives the run, which ends with it.
