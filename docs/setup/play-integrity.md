# Setting up Play Integrity for Ballast.AppAttestation

Ballast.AppAttestation checks an install with Google Play Integrity before it gives the install a session. This guide is the
one-time setup on Google's side and in the app's configuration. Every name in angle brackets is yours to fill in:

- `<package>`: the Android application id, for example `com.example.app`. It is the `PackageName` the app sets.
- `<project-number>`: the Google Cloud project number the app is linked to. It is shown on the Play Console's App integrity page.
- `<api-app>`: the App Service (or other host) that runs the API.
- `<resource-group>`: the resource group of `<api-app>`.

Do these steps yourself. The library never creates a Google account, a Cloud project, a service account or a key for you, and
nothing in the repository can do them.

Play Integrity works with one Cloud project per app. An app is linked to one project at a time, so all of the steps below use
the same project.

## 1. Link the app to a Google Cloud project

1. In Play Console, open the app, then **Release > App integrity > Play Integrity API**.
2. Link the app to a Google Cloud project. Create one if there is none.
3. Note the project's **project number** (the number, not the project id). The app's configuration needs it, not this library.

Read Google's terms for the Play Integrity API before you rely on it:
[Play Integrity: terms of service and data safety](https://developer.android.com/google/play/integrity/terms).

## 2. Enable the Play Integrity API

In the Cloud project, enable the **Play Integrity API** (APIs & Services > Library). The API page should then show *API Enabled*.

## 3. Create a service account and its key

1. In the Cloud project, create a **service account** (IAM & Admin > Service accounts). Give it a name you will recognise, such as
   `play-integrity`.
2. The service account needs **no IAM role** to call `decodeIntegrityToken`. The Play Console's App integrity page can grant it
   access; use that page rather than adding a role.
3. Create a **JSON key** for the service account and download it.

The key is a secret. Do not paste it into chat, a repository, an issue or a log.

## 4. Store the key as a secret

Put the whole JSON file in one setting. Use one of these, never a file in the repository.

App Service (the double underscore is how App Service spells the nested name):

```bash
az webapp config appsettings set -g <resource-group> -n <api-app> --settings PlayIntegrity__ServiceAccountJson='<the whole json>'
```

Or a Key Vault reference, which the same setting name accepts.

Locally, with user secrets, from the API's project folder:

```bash
dotnet user-secrets set "PlayIntegrity:ServiceAccountJson" "<the whole json>"
```

Until this setting holds a usable key, every attestation answers 503 `APP_ATTESTATION_UNAVAILABLE`. The API logs a warning at
startup outside Development. Nothing is ever read as a pass.

## 5. Set the session signing key

The session token is signed with its own key, separate from the user-token key. Generate one with at least 32 bytes:

```bash
openssl rand -base64 48
```

Store it as `AppSession__Key` (App Service) or `AppSession:Key` (user secrets). The API will not start without it, or with
`CHANGE-ME` or a value shorter than 32 bytes.

Changing this key ends every session at once. The apps renew their sessions on their next call.

## 6. List your own signing certificates (the developer path)

A build you install yourself from an IDE is not recognised by Play, so Play Integrity reports `UNRECOGNIZED_VERSION` for it. The
server accepts such a build only when the certificate that signed it is listed here. List the SHA-256 digest of each keystore you
install with:

- the debug keystore, which Android Studio and Visual Studio use:

  ```bash
  keytool -list -v -keystore "<path to debug.keystore>" -alias androiddebugkey -storepass android
  ```

- the upload key, which is your own `.keystore` (or Play Console > App signing for the key Play holds).

Put the digests in one comma-separated setting:

```bash
az webapp config appsettings set -g <resource-group> -n <api-app> --settings AppAttestation__DeveloperCertificates='AA:BB:...,CC:DD:...'
```

Colon-separated hex (as `keytool` prints it) and base64 both work. A sideload signed with any other key is refused, and the
refusal is logged with the verdict's fields.

## 7. Roll it out in log-only mode first

Enforcement is off by default. Keep it off until every build you still support can attest.

1. Deploy the API with `AppAttestation__Enforce` unset. The log shows `App session issued` for every session and
   `App session would be required (log-only)` for each data call that had none.
2. Install the app on a phone and open it. The log shows `att=play` for a Play install and `att=dev` for your sideload.
   A refusal logs `App attestation refused` with the verdict's own fields, for example `appRecognitionVerdict=UNRECOGNIZED_VERSION`.
3. Once every build you still support attests, set `AppAttestation__Enforce=true`. From then on a data call with no session
   answers 401 with the app's required code. Setting it back to `false` is the rollback.

## Local runs

Emulators and desktop builds have no Play Integrity. For a local API, set the Development-only bypass in the API's
`appsettings.Development.json`:

```json
"AppAttestation": { "AllowUnattested": true }
```

It issues unattested `none` sessions, and only in the Development environment. Outside Development the flag is ignored, and the
API logs an error at startup when it is set. Remove it from every other environment.

## Checking the setup

- `AppAttestation__Enforce` reads back as the value you set (`az webapp config appsettings list`).
- `PlayIntegrity__ServiceAccountJson` is set. Read the key's `client_email` and `project_id` to confirm the account, never the secret.
- The API's startup log says `Developer certificates configured: <n>` with the count you expect.
- A Play install gets `att=play` in the log.

## When something changes

- **A new Cloud project** (the app is linked to another one): create a new service account and key in it, store the key again, and
  ship a build whose configuration names the new project number. Relink in Play Console first, because an app links to one
  project at a time.
- **A rotated service account key**: store the new JSON in the same setting. Nothing else changes.
- **A new signing key for the owner's sideloads**: list its digest in `AppAttestation__DeveloperCertificates`, alongside the others.
  Remove a digest only when you no longer sign with its key.
