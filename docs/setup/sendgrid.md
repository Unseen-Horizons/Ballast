# Setting up SendGrid for Ballast.Email

Ballast.Email sends through SendGrid's web API. To send for real you need three things: a SendGrid account, one sender
address that is verified with SendGrid, and an API key that may only send mail. The key goes in configuration, never in a
committed file.

Do these steps yourself. The library never creates an account or a key for you.

## 1. Create the account

1. Go to sendgrid.com and sign up, or sign in if you have an account.
2. Check the plan's sending limits on SendGrid's pricing page before you rely on the account for real mail.
3. Read SendGrid's terms of service before you use it.

## 2. Verify the sender

The sender address is the one the mail appears to come from. SendGrid sends only from verified addresses, and the
`FromEmail` setting must be one of them.

There are two ways to verify a sender. Pick one.

**Single Sender Verification.** The quickest way, for one address you can read mail at.

1. In SendGrid, open Settings, then Sender Authentication.
2. Choose Single Sender Verification, and add the sender's name and address.
3. SendGrid sends a confirmation to that address. Open it and confirm.

**Domain Authentication.** Better for delivery, and it lets you send from any address on the domain.

1. In SendGrid, open Settings, then Sender Authentication, and choose Authenticate Your Domain.
2. SendGrid shows DNS records (DKIM and the return path). Add them at the DNS host of the domain.
3. Return to SendGrid and verify. Verification can take some time after the records are added.

Whichever you choose, the `FromEmail` value must be the verified address, or an address on the verified domain. A different
address is refused by SendGrid with "The from address does not match a verified Sender Identity."

## 3. Create the API key

1. In SendGrid, open Settings, then API Keys, then Create API Key.
2. Give the key a name that says what it is for, such as `ballast-email-dev`.
3. Choose Restricted Access.
4. Under Mail Send, set Mail Send to Full Access. Leave every other permission at No Access.
5. Create the key. SendGrid shows the key once. Copy it now, because it cannot be shown again.

A key with only Mail Send can send mail and do nothing else, so a leaked key cannot read contacts or change the account.

## 4. Put the key and the sender in configuration

The settings are read from the `SendGrid` section (or the section the app names). The keys are:

| Key | Secret? | Where |
|---|---|---|
| `SendGrid:ApiKey` | yes | User secrets in development, an environment variable in production |
| `SendGrid:FromEmail` | no | Configuration. It may also be an environment variable |
| `SendGrid:FromName` | no | Optional. The sender's display name |

**Development.** Run these in the project folder that holds the app, using your own key and address:

```
dotnet user-secrets set "SendGrid:ApiKey" "<your key>"
dotnet user-secrets set "SendGrid:FromEmail" "<your verified address>"
```

Leave `SendGrid:ApiKey` out of every `appsettings*.json` file, and leave the value in those files blank.

**Production (Azure App Service).** Add two application settings, under Settings, then Environment variables, or under
Configuration in the portal:

- `SendGrid__ApiKey` with the key as its value.
- `SendGrid__FromEmail` with the verified address as its value.

Two underscores stand for the colon in the configuration name, because a colon is not allowed in an environment variable
name.

If you set an API key and leave out the sender address, the app stops at startup and names `SendGrid:FromEmail`. That is
deliberate: the mistake shows at once, not on the first mail.

## 5. Check that mail goes out

1. Start the app with the key and the sender set.
2. Send one mail to a mailbox you can read, from the app's own flow.
3. The mail arrives. The app's log has no warning from the mail service.

If the log shows an error, it is one of these:

| Log shows | Meaning | Fix |
|---|---|---|
| `SendGrid returned 400` with "from address does not match a verified Sender Identity" | The sender is not verified, or differs from the verified address. | Use the verified address in `FromEmail`, or finish the verification in step 2. |
| `SendGrid returned 401` with "invalid, expired, or revoked" | The key is wrong, deleted or revoked. | Create a new key in step 3 and set it again. |
| `SendGrid returned 403` | The key lacks the Mail Send permission. | Edit the key and set Mail Send to Full Access, or create a new key. |
| `SendGrid:ApiKey is not set, so the e-mail ... was not sent.` | No key is set outside Development. | Set the key as in step 4. |

The log never shows the key, the text of a mail, or a full address. Addresses appear masked, as `k***@example.test`.

## 6. If the key leaks

1. In SendGrid, open Settings, then API Keys, and delete the key.
2. Create a new key as in step 3, and set it as in step 4.
3. Look at the key's history in SendGrid for sends you did not make.
