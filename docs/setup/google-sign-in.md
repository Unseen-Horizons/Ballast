# Setup: Google sign-in

A rider or user signs in with a Google account. The app asks Google for an ID token; the server checks the token's
signature, its audience and that Google has verified the address. This guide does the Google side. The app's own
settings are in [Auth.md](../Auth.md), section "Google sign-in".

## What is set up in Google

Three pieces, all in one Google Cloud project:

| Piece | Used for |
|---|---|
| **Google Auth Platform**, the OAuth consent screen | The app's sign-in screen. Scopes `email`, `profile` and `openid` only, so Google does not review the app for sensitive scopes |
| **Web** OAuth client | Its client id is the token's audience. The app requests the token with it, and the server checks the token against the same id |
| **Android** OAuth clients | One per signing key (the Play app-signing key, the upload key and the debug key). Each is the package name and the key's SHA-1. They must exist in the same project as the Web client; the code does not reference them |

## 1. The consent screen

**Google Auth Platform** → **Branding**: the app name and a support address. **Audience**: **External**. Set it to
**In production** once the sign-in is tested. In **Testing**, only the listed test users can sign in.

**Data access** (scopes): `openid`, `.../auth/userinfo.email`, `.../auth/userinfo.profile`. Nothing else, so Google's
sensitive-scope review does not apply.

## 2. The Web client

**Google Auth Platform** → **Clients** → **Create client** → **Web application**. Give it a name. Under **Authorized
JavaScript origins** and **Authorized redirect URIs**, add nothing: the sign-in is a token request from the app, not a
redirect to a web page.

Copy the **Client ID**. It ends in `.apps.googleusercontent.com`. It is not a secret.

Set it as `Google:WebClientId` in the app's configuration, for the server and for the app that asks for the token. The
server checks the token's audience against this value; a token issued for another client id is refused with
`GOOGLE_TOKEN_INVALID`.

## 3. The Android clients

For each signing key the app is built with:

1. **Clients** → **Create client** → **Android**.
2. The **Package name** of the app, exactly as in the app's manifest.
3. The **SHA-1** of the signing key. For a Play-signed app, use the app-signing key from the Play Console; for a local
   build, the debug or upload key.

Android clients are how Google checks that the app asking for a token is the one it was issued to.

## 4. If the Google project is recreated

- Change `Google:WebClientId` in the server and in the app, ship a new app build, and deploy the server.
- Create the Android clients again in the new project (package and SHA-1).
- Installs on an old build lose Google sign-in until they update. Their tokens carry the old audience, and the server
  checks only one audience, so accepting both for a while would need a code change.
- Accounts already linked to Google keep working, because the link stores Google's account id (`sub`), which is the same
  whichever client issues the token. NEEDS VERIFICATION before relying on it across a project change.

## 5. Address and sign-in rules the server applies

- The address must be verified by Google (`email_verified`). An unverified address is refused with
  `GOOGLE_EMAIL_UNVERIFIED`.
- A first sign-in whose address belongs to a password account is refused with `ACCOUNT_EXISTS_USE_PASSWORD`. The Google
  account is never linked to that account automatically.
- An account is matched by Google's subject, never by address.
- Google being unreachable is `GOOGLE_UNREACHABLE` (503), not a bad token, so the person may retry.
