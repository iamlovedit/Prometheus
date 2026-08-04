param(
    [Parameter(Mandatory = $true)]
    [string]$PreviousMsiPath,

    [Parameter(Mandatory = $true)]
    [string]$UpgradeMsiPath
)

$ErrorActionPreference = 'Stop'

$resolvedPreviousMsiPath = (Resolve-Path -LiteralPath $PreviousMsiPath).Path
$resolvedUpgradeMsiPath = (Resolve-Path -LiteralPath $UpgradeMsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer

function Get-MsiProperty
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $database = $installer.OpenDatabase($Path, 0)
    $view = $database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='$Name'")
    $null = $view.Execute()
    $record = $view.Fetch()

    if ($null -eq $record)
    {
        return $null
    }

    return $record.StringData(1)
}

function Invoke-MsiExec
{
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $processParameters = @{
        FilePath = 'msiexec.exe'
        ArgumentList = $Arguments
        Wait = $true
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    $process = Start-Process @processParameters

    if ($process.ExitCode -ne 0)
    {
        throw "$Operation failed with exit code $($process.ExitCode)."
    }

    return $process.ExitCode
}

function Get-InstalledProductVersion
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProductCode
    )

    try
    {
        return $installer.ProductInfo($ProductCode, 'VersionString')
    }
    catch
    {
        return $null
    }
}

$previousProductCode = Get-MsiProperty -Path $resolvedPreviousMsiPath -Name 'ProductCode'
$upgradeProductCode = Get-MsiProperty -Path $resolvedUpgradeMsiPath -Name 'ProductCode'
$previousUpgradeCode = Get-MsiProperty -Path $resolvedPreviousMsiPath -Name 'UpgradeCode'
$upgradeUpgradeCode = Get-MsiProperty -Path $resolvedUpgradeMsiPath -Name 'UpgradeCode'
$previousVersion = [Version](Get-MsiProperty -Path $resolvedPreviousMsiPath -Name 'ProductVersion')
$upgradeVersion = [Version](Get-MsiProperty -Path $resolvedUpgradeMsiPath -Name 'ProductVersion')

if ($previousProductCode -eq $upgradeProductCode)
{
    throw 'The previous and upgrade MSI packages must have different ProductCode values.'
}

if ($previousUpgradeCode -ne $upgradeUpgradeCode)
{
    throw 'The previous and upgrade MSI packages must have the same UpgradeCode.'
}

if ($upgradeVersion -le $previousVersion)
{
    throw 'UpgradeMsiPath must have a higher ProductVersion than PreviousMsiPath.'
}

if (($null -ne (Get-InstalledProductVersion -ProductCode $previousProductCode)) -or
    ($null -ne (Get-InstalledProductVersion -ProductCode $upgradeProductCode)))
{
    throw 'One of the MSI packages is already registered for the current user.'
}

$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory)) 'Prometheus.lnk'
$startupShortcutPath = Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::Startup)) 'Prometheus.lnk'
$startMenuShortcutPath = Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::Programs)) 'Prometheus\Prometheus.lnk'

if ((Test-Path -LiteralPath $desktopShortcutPath) -or
    (Test-Path -LiteralPath $startupShortcutPath) -or
    (Test-Path -LiteralPath $startMenuShortcutPath))
{
    throw 'A Prometheus shortcut already exists. Remove it before running the MSI upgrade test.'
}

$artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))
New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null

$smokeDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsDirectory ('msi-upgrade-smoke-' + [guid]::NewGuid().ToString('N'))))

if (-not $smokeDirectory.StartsWith(
        $artifactsDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The MSI upgrade-test directory escaped the artifacts directory.'
}

$installDirectory = Join-Path $smokeDirectory 'custom-install'
$previousInstallLog = Join-Path $smokeDirectory 'previous-install.log'
$upgradeInstallLog = Join-Path $smokeDirectory 'upgrade-install.log'
$uninstallLog = Join-Path $smokeDirectory 'uninstall.log'
$installedMsiPath = $null
$verificationSucceeded = $false
$previousInstallExitCode = $null
$upgradeInstallExitCode = $null
$uninstallExitCode = $null

New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null

try
{
    $previousInstallExitCode = Invoke-MsiExec -Operation 'Previous MSI installation' -Arguments @(
        '/i',
        "`"$resolvedPreviousMsiPath`"",
        '/qn',
        '/norestart',
        "INSTALLFOLDER=`"$installDirectory`"",
        'DESKTOP_SHORTCUT=0',
        'START_WITH_WINDOWS=1',
        '/l*v',
        "`"$previousInstallLog`"")
    $installedMsiPath = $resolvedPreviousMsiPath

    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'Prometheus.Desktop.exe')))
    {
        throw 'The previous MSI did not install to the requested custom directory.'
    }

    $upgradeInstallExitCode = Invoke-MsiExec -Operation 'MSI major upgrade' -Arguments @(
        '/i',
        "`"$resolvedUpgradeMsiPath`"",
        '/qn',
        '/norestart',
        '/l*v',
        "`"$upgradeInstallLog`"")
    $installedMsiPath = $resolvedUpgradeMsiPath

    if ($null -ne (Get-InstalledProductVersion -ProductCode $previousProductCode))
    {
        throw 'The previous MSI registration remains after the major upgrade.'
    }

    $installedUpgradeVersion = Get-InstalledProductVersion -ProductCode $upgradeProductCode
    if ($null -eq $installedUpgradeVersion)
    {
        throw 'The upgrade MSI registration is missing after the major upgrade.'
    }

    $installedExecutable = Join-Path $installDirectory 'Prometheus.Desktop.exe'
    if (-not (Test-Path -LiteralPath $installedExecutable))
    {
        throw 'The major upgrade did not preserve the custom installation directory.'
    }

    if (Test-Path -LiteralPath $desktopShortcutPath)
    {
        throw 'The major upgrade did not preserve the disabled desktop shortcut option.'
    }

    if (-not (Test-Path -LiteralPath $startupShortcutPath))
    {
        throw 'The major upgrade did not preserve the enabled startup option.'
    }

    if (-not (Test-Path -LiteralPath $startMenuShortcutPath))
    {
        throw 'The Start menu shortcut is missing after the major upgrade.'
    }

    $settings = Get-ItemProperty -LiteralPath 'HKCU:\Software\Prometheus\Installer'
    if ($settings.DesktopShortcut -ne '0' -or $settings.StartWithWindows -ne '1')
    {
        throw 'The major upgrade did not preserve the installer option values.'
    }

    if ($settings.InstallFolder.TrimEnd('\') -ne $installDirectory.TrimEnd('\'))
    {
        throw 'The persisted installation directory does not match the upgraded directory.'
    }

    if ([Version]$installedUpgradeVersion -ne $upgradeVersion)
    {
        throw 'The registered product version does not match the upgrade MSI.'
    }

    $verificationSucceeded = $true
}
finally
{
    if ($null -ne $installedMsiPath)
    {
        $uninstallExitCode = Invoke-MsiExec -Operation 'Upgraded MSI uninstall' -Arguments @(
            '/x',
            "`"$installedMsiPath`"",
            '/qn',
            '/norestart',
            '/l*v',
            "`"$uninstallLog`"")
    }

    if ($verificationSucceeded -and (Test-Path -LiteralPath $smokeDirectory))
    {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}

[pscustomobject]@{
    PreviousVersion = $previousVersion
    UpgradeVersion = $upgradeVersion
    CustomInstallDirectoryPreserved = $true
    ShortcutOptionsPreserved = $true
    PreviousInstallExitCode = $previousInstallExitCode
    UpgradeInstallExitCode = $upgradeInstallExitCode
    UninstallExitCode = $uninstallExitCode
} | Format-List
