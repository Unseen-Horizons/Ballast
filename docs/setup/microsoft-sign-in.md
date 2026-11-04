# Setup: staff sign-in with Microsoft

Staff sign in to an admin console with a Microsoft work or school account. This guide does the Microsoft side: one Entra
app registration, its roles, its optional claims, its client secret for the directory sync, and the settings the app
reads. The app's own settings are in [Auth.md](../Auth.md), section "Microsoft staff sign-in".

The steps are written for the Entra admin center. Replace `<api-host>` with the host the admin console is served from.

## Before you start

- An Azure account, with an Entra ID directory. The default directory (Entra ID Free) is enough: it holds the app
  registration and test users at no cost.
- Microsoft 365 is not needed to set this up.
- Sign in to the Entra admin center as an administrator of that directory.

## 1. Register the app, once

**Entra admin center** → **Identity** → **Applications** → **App registrations** → **New registration**:

- **Name:** any name, such as `Staff console`.
- **Supported account types:** **Accounts in this organizational directory only (Single tenant)**.
- **Redirect URI:** platform **Web**, `https://<api-host>/signin-oidc`.
- **Register.**

Then open **Authentication**:

- Under **Web** → **Redirect URIs**, add every address the console is opened on, for example
  `https://localhost:7230/signin-oidc` for a local run. The path is the app's `CallbackPath`, `/signin-oidc` by default.
- Under **Implicit grant and hybrid flows**, tick **ID tokens**. Leave **Access tokens** unticked.
- **Save.** Forgetting Save is the most common cause of "Microsoft sign-in did not finish".

Then open **API permissions**:

- **Add a permission** → **Microsoft Graph** → **Delegated permissions** → tick `openid`, `profile` and `email` →
  **Add permissions**.
- **Grant admin consent for** the directory → **Yes**. Every line shows a green tick, so nobody sees a consent prompt.

Then open **Overview** and copy the **Application (client) ID** and the **Directory (tenant) ID**. Neither is a secret.
They go into the settings in step 6.

## 2. Security defaults

**Identity** → **Overview** → **Properties** → **Manage security defaults**: set to **Enabled**, then **Save**. A new
setup usually has this on already. Microsoft then asks each person to set up the free Microsoft Authenticator app at
their next sign-in. If Entra refuses because of "Conditional Access" policies, those are paid rules. A new setup has
none.

## 3. App roles: which console each role is for

**App registrations** → your app → **App roles** → **Create app role**, once for each role:

- **Display name:** what you see when you give someone the role, for example `Owner`.
- **Allowed member types:** **Users/Groups**.
- **Value:** the role's name, exactly. The app reads only this field. Its prefix says which console the role is for.
- **Description:** free text for you.
- **Do you want to enable this app role?:** ticked. **Apply**.

The prefix of the **Value** decides the console. It applies only when the app sets `ConsolePrefix`; without it, every
value is a role name as it is.

| Value in Microsoft | Meaning when the app's `ConsolePrefix` is `Api` | Meaning when `ConsolePrefix` is `Map` |
|---|---|---|
| `Admin` | A role `Admin` in this console (no prefix, so it is read as a role name) | The same role `Admin`, with no prefix |
| `Api.Admin` | The role `Admin` | Ignored: another console's value |
| `Map.Admin` | Ignored: another console's value | The role `Admin` |
| `Api.` (bare prefix) | Reported as `BAD_ROLE_VALUE`, and ignored | Reported as `BAD_ROLE_VALUE`, and ignored |

The rules:

- **No prefix** means the value is a role name, as written. The prefix ignores case. The rest of the value is the role
  name, in its own case.
- A value with another console's prefix is that console's to judge. It is ignored here without a problem.
- A bare prefix (`Api.` on its own) names no role. It is ignored and listed as `BAD_ROLE_VALUE`.
- A value that the app names as its rider role (`RiderRole`) is never a staff role. It is listed as `RIDER_ROLE`.
- A person can hold several roles unless the app is set to single-role mode (`RoleMode = One`). Their permissions add up.
- A role that a console does not have yet is created there when a person with it first signs in, or at the next sync.
  It has **no permissions** until an administrator gives it permissions in the console. Until then the person is refused
  with `MS_ROLE_NO_PERMISSIONS`. Their account and the role are kept.
- A typo in a value, such as `Api.Admn`, makes a new empty role. It gives nothing.
- If you delete a role in Microsoft, the console keeps it and marks it as no longer in Microsoft. The sync never deletes
  a role.

## 4. Give people their roles, one at a time

**Enterprise applications** (not App registrations) → your app → **Users and groups** → **Add user/group**. Choose the
person, then the role, then **Assign**.

Do this one person at a time. A role given to a group needs Entra ID P1, which is paid. The sync lists a group as
`GROUP_ASSIGNMENT` and does not read its members.

**Assignment required.** Turn it on only after you have given yourself a role. Then open **Properties** →
**Assignment required?** → **Yes** → **Save**. From then on Microsoft lets only people with a role into the app. Turned
on before you have a role, it locks you out too.

- An administrator of the directory can get past **Assignment required**. Without a role in the console, such a sign-in
  is refused with `MS_NO_ROLE`. That is correct.
- **NEEDS VERIFICATION:** whether **Assignment required** and the Microsoft Graph application permissions work on Entra
  ID Free. The licensing docs name no licence for either, but they do not say so explicitly. Check on the Free default
  directory before you rely on them.

## 5. Names in the sign-in

**App registrations** → your app → **Token configuration** → **Add optional claim**:

- Token type **ID**. Tick `family_name` and `given_name`. **Add**.
- If Microsoft asks to turn on the Microsoft Graph profile permission, tick it and confirm.

Give every person a first name and a last name in Microsoft: the user's profile in Entra, or **Manage contact
information** in the Microsoft 365 admin center. Without both, the sign-in is refused with `MS_NAME_MISSING`. The app
never splits a display name to guess them.

## 6. Settings

Set these in the app's configuration (the `MicrosoftSignIn` section by default). For a local run, use user secrets, or
environment variables, where a double underscore writes a nested setting.

| Setting | Value | Secret? |
|---|---|---|
| `MicrosoftSignIn:TenantId` | The Directory (tenant) ID | No |
| `MicrosoftSignIn:ClientId` | The Application (client) ID | No |
| `MicrosoftSignIn:SyncMinutes` | Optional. 15 by default, 5 at least | No |
| `MicrosoftSignIn:ClientSecret` | The client secret from step 7 | **Yes** |
| `MicrosoftSignIn:Domains` | Optional. Company domains, separated by `;`, such as `example.com` | No |

- Empty `TenantId` or `ClientId` means Microsoft sign-in is off, and nothing is sent to Microsoft.
- A value that is set but is not a GUID stops the app at start. The message names the setting.
- The client secret is never written in a file in the repository. Read it from user secrets or the app's settings only.

## 7. The directory sync and its client secret

The sync reads the staff list in Microsoft on a schedule. It copies each signed-in person's name, address and roles. It
switches off a person who is blocked, removed, or has no role in that console. It creates the roles that Microsoft gives,
with no permissions. It never creates an account (that happens at the first sign-in), and it never deletes a role. It
runs in one copy of the app only, the copy that holds the background-job lock.

The sign-in itself does not use the secret. Sign-in keeps working when the sync is off.

Set up the sync in the app registration:

1. **API permissions** → **Add a permission** → **Microsoft Graph** → **Application permissions** (not Delegated). Tick
   `User.Read.All` and `Application.Read.All`. **Add permissions**.
2. **Grant admin consent** → **Yes**. Both show type **Application** and a green tick.
3. **Certificates & secrets** → **Client secrets** → **New client secret**. Give it a description, and expire it in
   12 months. **Add**.
4. Copy the **Value** at once. It is shown only once. Copy the Value, not the Secret ID.
5. Save the Value as `MicrosoftSignIn:ClientSecret`.

Until the secret is set, the sync is off.

**Renewing.** Put a reminder in your calendar for 11 months after you create the secret. Then create a new secret, put
its Value in `MicrosoftSignIn:ClientSecret`, and delete the old secret in Entra. The app logs a warning 30 days before
the secret ends. When the secret has ended, sign-in keeps working. Only the sync stops.

## 8. Testing before Microsoft 365 exists

- The default directory of an Azure account can hold the app registration and test users at no cost. Create test users
  in **Users** → **New user**, give each a first and last name, and assign them roles as in step 4.
- Sign in with a test user in a private browser window, so the test user does not replace your own session.

## 9. Moving to the company's tenant later

Create the same app registration in the company's tenant, with the same redirect URIs and app roles. Change
`TenantId`, `ClientId` and the client secret in the app's settings, and turn on **Assignment required** there. Then
give the people their roles again. Accounts already in the app are linked to the Microsoft object id of the old tenant. A person's object id differs between tenants, so their sign-in in the new tenant is refused with `MS_ADDRESS_TAKEN` until the old Microsoft login is removed from their account. Plan that removal as part of the move. The app's administrator does it, because the library has no call for it.

## 10. Leaving

Set `ClientId` to empty in the app's settings to turn Microsoft sign-in off. Remove the app registration in Entra when
nothing else uses it. Accounts stay in the app; their Microsoft login stays too, and the sync stops switching them on.

## 11. The emergency way in

Keep one password account for the day Microsoft is unavailable. The account is named in the app's code, not in this file:
ask the developer to register it as an emergency account. The directory sync never switches that account off, never applies
a change to it, and never counts it when it decides whether to stop a run. A Microsoft sign-in never links that account to a
Microsoft login and never changes it. Keep its password in a safe place, not in the repository.

## 12. The app's own profile table

Some apps keep a row of their own for each staff member. The developer sets that up in the app's code, and nothing is needed
in Entra for it. When the row is written together with the account, a sign-in that cannot write it creates no account, so
no half-made account is left behind. When the directory sync cannot write it for one person, that person's change is undone,
the rest of the run goes on, and the problem is listed as `PROFILE_HOOK_FAILED` (section 14).

## 13. Shared computers

Sign out at the end of a session, and use a private window on a shared computer, so the next person cannot reuse the session.

## 14. When something fails

The sign-in page shows one sentence per refusal. The page's address carries the refusal in `error`, and Microsoft's own
code, when there is one, in `ms`. Neither carries Microsoft's message.

| Refusal (`error`) | Meaning | What to do |
|---|---|---|
| `MS_NOT_CONFIGURED` | `TenantId` or `ClientId` is empty | Set both, see step 6 |
| `MS_NOT_COMPANY_ADDRESS` | The address is not at one of `Domains` | Sign in with a company address |
| `MS_SIGNIN_FAILED` | The handshake failed, or the token lacks a usable object id or address | Check the redirect URI and the ID token setting, step 1 |
| `MS_WRONG_TENANT` | The account belongs to another tenant | Sign in with an account in this tenant |
| `MS_NO_ROLE` | The person has no role for this console | Give the role, step 4 |
| `MS_MULTIPLE_ROLES` | Single-role mode, and the person has several roles | Leave one role for this console |
| `MS_NAME_MISSING` | First or last name is missing in Microsoft | Fill in the name, step 5 |
| `MS_ROLE_NO_PERMISSIONS` | The role exists but has no permissions yet | An administrator gives it permissions in the console |
| `MS_ADDRESS_TAKEN` | A password account holds the address, and is never linked | Ask an administrator to resolve the duplicate |
| `MS_EMERGENCY_ACCOUNT` | The Microsoft login is on an emergency account, which sign-in never changes | Ask an administrator |
| `SIGNIN_CODE_INVALID` | The one-time code is used, expired, or its account was switched off | Sign in again |

The sync's problems are listed to the administrator and never applied: `GROUP_ASSIGNMENT`, `NAME_MISSING`, `NO_ADDRESS`,
`ADDRESS_TAKEN`, `MULTIPLE_ROLES`, `ROLE_NOT_IN_MICROSOFT`, `RIDER_ROLE` and `BAD_ROLE_VALUE`. `PROFILE_HOOK_FAILED` means the
app's own row for one person could not be written (section 12): that person's change was undone, and the rest of the run went
on. A run that would switch off every linked account is stopped and writes nothing. Check the tenant, the app roles and the assignments in Entra first.
