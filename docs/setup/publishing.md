# Publishing setup: GitHub and nuget.org

This guide sets up the accounts that release Ballast. Do it once, before the first release. You do not need to have
used nuget.org or GitHub organisations before. Do the parts in order. Each part ends with a check. When all three
parts are done, the releases are described in [releasing.md](../releasing.md).

**Labels on the screens.** The names of buttons and menus below come from the documentation and may differ slightly on
your screen. If a label does not match, look for the nearest one. Where a label is uncertain, the step says so.

**Three values to keep:**

- The GitHub organisation: `unseen-horizons`. If GitHub says the name is taken, choose another name.
  The name is in `Directory.Build.props`, and it is typed into the Trusted Publishing policy (Step 2.3). If it changes,
  change all of them together.
- The GitHub repository: `ballast`.
- Your nuget.org profile name. It is the name in the address of your nuget.org profile page. It is not your email address.

What you need: a GitHub account, a nuget.org account, and a phone or an authenticator app for the two-factor code.

---

## Part 1: GitHub

### Step 1.1: Create the organisation

1. Go to https://github.com and sign in.
2. Go to https://github.com/account/organizations/new. If that address does not open, click your profile picture at the
   top right, then **Your organizations**, then **New organization**.
3. Choose the **Free** plan.
4. In **Organization account name**, type `unseen-horizons`. In **Contact email**, type your email address. Click **Next**.
5. On the screen that asks you to invite people, click **Skip this step**. (Labels: check on screen.)

**Check:** https://github.com/unseen-horizons opens, and shows the organisation.

### Step 1.2: Create the repository

1. On the organisation page, click **New repository**.
2. **Owner**: `unseen-horizons`. **Repository name**: `ballast`.
3. Choose **Public**.
4. Leave **Add a README file**, **Add .gitignore** and **Choose a license** unticked. The repository must be empty.
5. Click **Create repository**.

**Check:** https://github.com/unseen-horizons/ballast opens. It shows an empty repository with setup instructions.

### Step 1.3: Create the `release` environment

The environment is what makes the owner approve each release. Create it before any tag is pushed.

1. Open https://github.com/unseen-horizons/ballast. Click **Settings** in the top bar.
2. In the left menu, click **Environments**, then **New environment**.
3. In **Name**, type `release`. Click **Configure environment**.
4. Under **Deployment protection rules**, tick **Required reviewers**. In the box that opens, type your GitHub username,
   click it in the list, and it is added.
5. Under **Deployment branches and tags**, choose **Selected branches and tags**. Click **Add deployment tag rule**. In
   **Name pattern**, type `v*`. Click **Add rule**. Now only tags whose names start with `v` can use the environment.
6. Click **Save protection rules**. (Labels in steps 4 to 6: check on screen.)

**Check:** the **Environments** page lists `release`. Its page shows **Required reviewers** with your username.

**Warning:** if the `release` environment does not exist when a tag is pushed, GitHub creates it with no reviewer. The
publish would then run without your approval. Do this step before any tag.

### Step 1.4: Add the secret `NUGET_USER`

1. In the repository, click **Settings**, then **Secrets and variables**, then **Actions**.
2. Click **New repository secret**.
3. In **Name**, type `NUGET_USER`.
4. In **Secret**, type your nuget.org profile name (see the note at the top). Not your email address.
5. Click **Add secret**.

**Check:** `NUGET_USER` is listed under **Repository secrets**. Its value stays hidden. That is normal.

---

## Part 2: nuget.org

### Step 2.1: Sign in and turn on two-factor sign-in

nuget.org requires two-factor sign-in for the account that publishes.

1. Go to https://www.nuget.org and sign in.
2. Click your username at the top right. Open **Account Settings**. (Label: check on screen.)
3. Find the two-factor setting in Account Settings, and turn it on. Follow the screens. (Exact label: check on screen.)
4. Save the recovery codes it shows somewhere safe.

**Check:** the two-factor setting shows that it is on.

### Step 2.2: Create the nuget.org organisation

The Trusted Publishing policy in Step 2.3 is set for this organisation, so the organisation must exist first.

1. On nuget.org, find the option to create an organisation. It is reached from your username menu. (Label and place:
   check on screen; nuget.org may call it **Organizations**.)
2. Name it `unseen-horizons`, or the nearest name nuget.org accepts. If the name is different, use that same name in the
   **Owner** field in Step 2.3.
3. Finish the screens. Check that your account is listed as an owner.

**Check:** the organisation's page on nuget.org opens, and lists you as an owner.

### Step 2.3: Add the Trusted Publishing policy

A Trusted Publishing policy lets one GitHub workflow publish, with no password or API key stored anywhere. The policy
says which repository and which workflow file may publish.

1. Click your username at the top right, then **Trusted Publishing**.
2. Click **Add policy**. (Label: check on screen.)
3. Fill in the five fields exactly as below:

   | Field | Type this |
   |---|---|
   | Owner | the organisation from Step 2.2 |
   | Repository Owner | `unseen-horizons` |
   | Repository | `ballast` |
   | Workflow File | `release.yml` (the file name only, not `.github/workflows/release.yml`) |
   | Environment | `release` |

4. Click **Add**, or the button that saves the policy.

**Check:** the policy is listed with those five values.

**The 7-day window.** A new policy starts as temporarily active, for 7 days. It becomes permanent after the first
successful publish. Publish the first release within those 7 days. If the policy has stopped working by then, check the
Trusted Publishing page. If the policy is gone, add it again with the same five values.

### Step 2.4: Ask nuget.org to reserve the prefix `Ballast.*`

A reserved prefix means that only the owners of that reservation can publish package names that start with `Ballast.`. Without it, someone
else could publish a package named `Ballast.Something`. nuget.org asks people to avoid common words, and `Ballast` may
be refused. The packages publish either way, under their own names.

1. Send an email from the address on your nuget.org account.
2. **To:** `account@nuget.org`
3. **Subject:** `Prefix reservation request: Ballast.*`
4. **Body:** copy the text below. Replace the two items in angle brackets.

   ```
   Hello,

   I would like to reserve the package ID prefix Ballast.* on nuget.org for my account.

   nuget.org display name: <your nuget.org display name>
   Prefix: Ballast.*
   Packages: Ballast.Errors, Ballast.Paging, Ballast.Time, Ballast.Text, Ballast.Geo, Ballast.Money
   Owner: Unseen Horizons
   Source code: https://github.com/unseen-horizons/ballast (MPL-2.0)

   Thank you,
   <your name>
   ```

5. Send the email yourself.

**Check:** nuget.org replies by email. A refusal is not a failure of the setup. Keep the reply.

---

## Part 3: The first release

Check the four points below before you push a tag. If one is not true, stop and fix it first.

- [ ] The `release` environment exists and lists you as a required reviewer (Step 1.3).
- [ ] The secret `NUGET_USER` exists (Step 1.4).
- [ ] The Trusted Publishing policy is listed on nuget.org, with the five values (Step 2.3).
- [ ] The commit you tag contains `.github/workflows/release.yml`.

### Step 3.1: Put the Release workflow on `main`

The workflow file is on the `release` branch of your local Ballast repository. It must be on `main` before you tag.

1. Open a terminal in your local Ballast repository (the folder `C:\Users\matej\Documents\VS Projects\Ballast`).
2. Check out `main`, then merge `release` into it:

   ```bash
   git checkout main
   git merge release
   ```

3. Connect the repository to GitHub, then push `main`:

   ```bash
   git remote add origin https://github.com/unseen-horizons/ballast.git
   git push -u origin main
   ```

   A sign-in window may open. Sign in with your GitHub account.

**Check:** https://github.com/unseen-horizons/ballast now shows the code, and the folder `.github/workflows` holds `release.yml`.

### Step 3.2: Move the tag to the new commit

The tag `v0.1.0-preview.1` already exists on your computer, on an older commit. That commit has no `release.yml`, so the
tag must move. The tag has not been pushed yet, so moving it is safe.

```bash
git tag -f v0.1.0-preview.1
```

This tags the commit that `main` now points to. Check that it did what you expect:

```bash
git log -1 --oneline v0.1.0-preview.1
```

### Step 3.3: Push the tag, and approve the run

1. Push the tag. This starts the Release run:

   ```bash
   git push origin v0.1.0-preview.1
   ```

2. On github.com, open the **Actions** tab of the repository, then the **Release** run.
3. Wait for the **Build, test, pack and check the tag** job to pass.
4. Click **Review deployments**, tick **release**, then click **Approve and deploy**. (Labels: check on screen.)

**Check:** the publish job log ends with a push for each package.

### Step 3.4: Check the packages on nuget.org

1. On nuget.org, search for each of these: `Ballast.Errors`, `Ballast.Paging`, `Ballast.Time`, `Ballast.Text`,
   `Ballast.Geo`, `Ballast.Money`.
2. Each one lists version `0.1.0-preview.1`. A new package can take a few minutes to appear in search.
3. Open one package. Its page links to https://github.com/unseen-horizons/ballast.
4. Open the Trusted Publishing page again. The policy is now permanent, because the first publish succeeded.

**If it fails:** the log of the publish job says what went wrong. If the sign-in step fails, check that the five policy
values (Step 2.3) and the `NUGET_USER` secret (Step 1.4) match this guide exactly. Fix the value, then use **Re-run
failed jobs** on the run.

**Before a project restores these packages from nuget.org for the first time:** if an earlier build restored Ballast
packages from a local folder, delete the cached copies in `%USERPROFILE%\.nuget\packages\ballast.*`. The cache holds the
same version numbers as nuget.org, so a restore could keep the local copy.

Once the checks pass, the next releases follow [releasing.md](../releasing.md).
