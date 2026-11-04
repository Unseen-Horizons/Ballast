# Setup: the container's stop limit on Azure App Service

`Ballast.Jobs` gives a stopping copy of an app `Hosting:ShutdownTimeoutSeconds` (60 by default) to finish its background work.
The platform, not the app, stops the container, and it gives the container its own grace period first. That period must be
longer than the app's wait, or the platform ends the copy while its workers are still winding down. This setting is made in the
App Service, outside the code, so it is set once for each app.

The app's wait and the platform's limit are in two places, and they must agree:

| Setting | Where | Value |
|---|---|---|
| `Hosting:ShutdownTimeoutSeconds` | the app's configuration | 60 by default (`ConfigureGracefulShutdown`) |
| `WEBSITES_CONTAINER_STOP_TIME_LIMIT` | the App Service's application settings | 90, which is 30 seconds above the default wait |

## Set it in the Azure portal

1. Open the App Service in the Azure portal.
2. Open **Settings**, then **Environment variables** (**Configuration**, then **Application settings**, in some portal versions).
3. Select **Add**, name the setting `WEBSITES_CONTAINER_STOP_TIME_LIMIT`, and enter `90`.
4. Select **Apply**, then **Confirm**. Saving the settings restarts the app.

## Or set it with the Azure CLI

```
az webapp config appsettings set --resource-group <resource group> --name <app name> --settings WEBSITES_CONTAINER_STOP_TIME_LIMIT=90
```

Check it with `az webapp config appsettings list --resource-group <resource group> --name <app name>`. The value is `90` in
the list.

## If the app's wait is changed

Keep the platform's limit above the app's wait. If `Hosting:ShutdownTimeoutSeconds` is raised, raise the limit to match, with
a margin of at least 30 seconds.

The usage guide, `docs/Jobs.md`, explains the wait and the lock. The deploy guide of the app that uses the library says how the
two copies of a deploy overlap.
