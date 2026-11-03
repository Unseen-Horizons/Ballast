# Copyright (c) 2026 Unseen Horizons
# This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
# distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

@{
    RootModule        = 'Ballast.Deploy.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = 'b21c386d-1ff2-470e-a20f-37e5e54b1aa7'
    Author            = 'Unseen Horizons'
    CompanyName       = 'Unseen Horizons'
    Copyright         = '(c) 2026 Unseen Horizons. Licensed under the Mozilla Public License 2.0.'
    Description       = 'Deploys an Azure App Service app from a pushed commit: a clean build, EF migrations behind a safety guard, a zip deploy, proof that the new copy is live, and a deploy tag with rollback by tag.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Read-BallastConfig',
        'Invoke-BallastDeploy',
        'Invoke-BallastMigrations',
        'Wait-BallastAppSwitched',
        'Hide-BallastSecret'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags       = @('Azure', 'AppService', 'Deploy', 'Migrations', 'Ballast')
            LicenseUri = 'https://mozilla.org/MPL/2.0/'
            Prerelease = 'preview2'
        }
    }
}
