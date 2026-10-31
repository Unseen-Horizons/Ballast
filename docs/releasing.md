# Releasing Ballast

A release puts the packages on nuget.org. Only the release workflow ([release.yml](../.github/workflows/release.yml))
publishes, and only after the owner approves the run. No package is pushed by hand.

A published version can never be deleted from nuget.org. It can only be unlisted, so a mistake means a new version.
Check each step before you go on to the next one.

The accounts and their one-time setup are in [setup/publishing.md](setup/publishing.md). Do that before the first release.

## What a release is

- All the packages share one version. It is set in `VersionPrefix` and `VersionSuffix` in [Directory.Build.props](../Directory.Build.props).
- The release tag is `v` followed by that version. For version `0.1.0` with the suffix `preview.1` the tag is
  `v0.1.0-preview.1`. A stable release has no suffix, so its tag is for example `v0.2.0`.
- The build job fails, and nothing is published, unless the tag and the package version are the same.

## 1. Set the version

1. Open `Directory.Build.props`.
2. Set `VersionPrefix`, for example `0.2.0`. Set `VersionSuffix`, for example `preview.2`. Leave `VersionSuffix` empty
   for a stable release.
3. Open `CHANGELOG.md`. Add a section for the new version above the older ones, newest first, and say what changed.
4. Run the four commands in [CONTRIBUTING.md](../CONTRIBUTING.md). All four must pass.
5. Check the file names of the packages in `artifacts/packages`. They must carry the new version, for example
   `Ballast.Errors.0.2.0-preview.2.nupkg`.
6. Commit the version and the changelog changes to `main`, and push `main`. Pushing a branch does not start a release.

## 2. Push the tag

1. Make sure the commit you want to release is the one on `main` that you pushed in step 1.6, and that it contains
   `release.yml`.
2. Create the tag on that commit. Use the exact tag name, for example:

   ```bash
   git tag v0.2.0-preview.2
   ```

3. Push the tag:

   ```bash
   git push origin v0.2.0-preview.2
   ```

The push starts the Release run on GitHub.

## 3. Approve the publish

1. On github.com, open the repository, then the **Actions** tab, then the **Release** run for your tag.
2. The **Build, test, pack and check the tag** job runs first.
3. When it passes, the **Publish to nuget.org** job waits for approval. Click **Review deployments**. (The label may
   differ on screen. Look for the approval prompt at the top of the run.)
4. Tick **release**, then click **Approve and deploy**.
5. The publish job now runs. Nothing is published before this click.

If the build job fails, nothing was published. Read the log and fix the cause. Then delete the tag on GitHub and on your
computer, and tag again after the fix:

```bash
git push origin --delete v0.2.0-preview.2
git tag -d v0.2.0-preview.2
```

If the publish job fails partway, use **Re-run failed jobs** on the run page. Packages that are already on nuget.org are
skipped, and the rest are pushed.

## 4. Check the release

1. The publish job log ends with the push of each `.nupkg`. Each `.snupkg` beside it goes up with it.
2. On nuget.org, search for each package: `Ballast.Errors`, `Ballast.Paging`, `Ballast.Time`, `Ballast.Text`,
   `Ballast.Geo` and `Ballast.Money`. Each one lists the new version. A new version can take a few minutes to appear
   in search.
3. Each package page links to the GitHub repository.

## Things to know

- **The push skips a version that is already on nuget.org.** The step uses `--skip-duplicate`. Running a release again
  for a version that is already published does nothing, and the run still passes. The log then says the package was
  skipped. Check the log, and do not assume a green run pushed anything new.
- **A bad version stays on nuget.org.** The owner can unlist it on its version page on nuget.org (check the label on
  screen). An unlisted version can still be restored by a project that already uses it. Release a new version with the
  fix. Never reuse a version number.
- **The release environment must exist with a reviewer.** If the `release` environment does not exist when the tag is
  pushed, GitHub creates it with no reviewer, and the publish job runs without a click. Create it first, as in
  [setup/publishing.md](setup/publishing.md).
- **The tag must point at a commit that has `release.yml`.** The workflow file is read from the tagged commit.
- **No key is stored.** The push uses a temporary API key from nuget.org. It lasts 1 hour, and nothing in the repository
  or in GitHub keeps it.
