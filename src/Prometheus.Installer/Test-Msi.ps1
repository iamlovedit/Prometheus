param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,

    [ValidateSet(0, 1)]
    [int]$DesktopShortcut = 0,

    [ValidateSet(0, 1)]
    [int]$StartWithWindows = 0,

    [switch]$VerifyRunningApplicationClosure
)

$ErrorActionPreference = 'Stop'

$resolvedMsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($resolvedMsiPath, 0)

function Get-MsiProperty
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $view = $database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='$Name'")
    $null = $view.Execute()
    $record = $view.Fetch()

    if ($null -eq $record)
    {
        return $null
    }

    return $record.StringData(1)
}

$productCode = Get-MsiProperty -Name 'ProductCode'
$productName = Get-MsiProperty -Name 'ProductName'
$productVersion = Get-MsiProperty -Name 'ProductVersion'

$perUserUninstallKey = "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"
$perMachineUninstallKey = "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"

if ((Test-Path -LiteralPath $perUserUninstallKey) -or
    (Test-Path -LiteralPath $perMachineUninstallKey))
{
    throw "Product $productCode is already registered. Uninstall it before running the MSI smoke test."
}

$artifactsDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts'))
New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null

$smokeDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsDirectory ('msi-smoke-' + [guid]::NewGuid().ToString('N'))))

if (-not $smokeDirectory.StartsWith(
        $artifactsDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The MSI smoke-test directory escaped the artifacts directory.'
}

$installDirectory = Join-Path $smokeDirectory 'app'
$installLog = Join-Path $smokeDirectory 'install.log'
$uninstallLog = Join-Path $smokeDirectory 'uninstall.log'
$installed = $false
$installExitCode = $null
$uninstallExitCode = $null
$installedExecutableVersion = $null
$runningApplicationProbe = $null
$runningApplicationClosed = $null
$verificationSucceeded = $false
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
    throw 'A Prometheus shortcut already exists. Remove it before running the MSI smoke test.'
}

New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null

try
{
    $installArguments = @(
        '/i',
        "`"$resolvedMsiPath`"",
        '/qn',
        '/norestart',
        "INSTALLFOLDER=`"$installDirectory`"",
        "DESKTOP_SHORTCUT=$DesktopShortcut",
        "START_WITH_WINDOWS=$StartWithWindows",
        '/l*v',
        "`"$installLog`"")

    $installProcessParameters = @{
        FilePath = 'msiexec.exe'
        ArgumentList = $installArguments
        Wait = $true
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    $installProcess = Start-Process @installProcessParameters

    if ($installProcess.ExitCode -ne 0)
    {
        throw "MSI installation failed with exit code $($installProcess.ExitCode). Log: $installLog"
    }

    $installExitCode = $installProcess.ExitCode
    $installed = $true

    $executablePath = Join-Path $installDirectory 'Prometheus.Desktop.exe'
    $licensePath = Join-Path $installDirectory 'LICENSE.txt'
    if (-not (Test-Path -LiteralPath $executablePath))
    {
        throw 'The installed executable is missing.'
    }

    if (-not (Test-Path -LiteralPath $licensePath))
    {
        throw 'The installed license is missing.'
    }

    if ($DesktopShortcut -eq 0 -and (Test-Path -LiteralPath $desktopShortcutPath))
    {
        throw 'A desktop shortcut was created despite DESKTOP_SHORTCUT=0.'
    }

    if ($DesktopShortcut -eq 1 -and -not (Test-Path -LiteralPath $desktopShortcutPath))
    {
        throw 'The desktop shortcut is missing despite DESKTOP_SHORTCUT=1.'
    }

    if ($StartWithWindows -eq 0 -and (Test-Path -LiteralPath $startupShortcutPath))
    {
        throw 'A startup shortcut was created despite START_WITH_WINDOWS=0.'
    }

    if ($StartWithWindows -eq 1 -and -not (Test-Path -LiteralPath $startupShortcutPath))
    {
        throw 'The startup shortcut is missing despite START_WITH_WINDOWS=1.'
    }

    if (-not (Test-Path -LiteralPath $startMenuShortcutPath))
    {
        throw 'The Start menu shortcut is missing.'
    }

    $settings = Get-ItemProperty -LiteralPath 'HKCU:\Software\Prometheus\Installer'
    if ($settings.DesktopShortcut -ne $DesktopShortcut.ToString() -or
        $settings.StartWithWindows -ne $StartWithWindows.ToString())
    {
        throw 'The installer option values were not persisted correctly.'
    }

    $installedExecutableVersion = (Get-Item -LiteralPath $executablePath).VersionInfo.ProductVersion

    if ($VerifyRunningApplicationClosure)
    {
        $probeDirectory = Join-Path $smokeDirectory 'process-probe'
        $probeExecutablePath = Join-Path $probeDirectory 'Prometheus.Desktop.exe'
        $windowsPowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

        New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null
        Copy-Item -LiteralPath $windowsPowerShellPath -Destination $probeExecutablePath
        $runningApplicationProbe = Start-Process `
            -FilePath $probeExecutablePath `
            -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 300' `
            -WindowStyle Hidden `
            -PassThru

        Start-Sleep -Milliseconds 500
        if ($runningApplicationProbe.HasExited)
        {
            throw 'The running-application probe exited before MSI uninstall.'
        }
    }

    $verificationSucceeded = $true
}
finally
{
    if ($installed)
    {
        $uninstallArguments = @(
            '/x',
            "`"$resolvedMsiPath`"",
            '/qn',
            '/norestart',
            '/l*v',
            "`"$uninstallLog`"")

        $uninstallProcessParameters = @{
            FilePath = 'msiexec.exe'
            ArgumentList = $uninstallArguments
            Wait = $true
            PassThru = $true
            WindowStyle = 'Hidden'
        }
        try
        {
            $uninstallProcess = Start-Process @uninstallProcessParameters

            if ($uninstallProcess.ExitCode -ne 0)
            {
                throw "MSI uninstall failed with exit code $($uninstallProcess.ExitCode). Log: $uninstallLog"
            }

            $uninstallExitCode = $uninstallProcess.ExitCode

            if ($null -ne $runningApplicationProbe)
            {
                $runningApplicationProbe.Refresh()
                $runningApplicationClosed = $runningApplicationProbe.HasExited
            }
        }
        finally
        {
            if ($null -ne $runningApplicationProbe)
            {
                $runningApplicationProbe.Refresh()
                if (-not $runningApplicationProbe.HasExited)
                {
                    Stop-Process -Id $runningApplicationProbe.Id -Force -ErrorAction SilentlyContinue
                }
            }
        }

        if ($VerifyRunningApplicationClosure -and -not $runningApplicationClosed)
        {
            throw 'Prometheus.Desktop.exe remains running after MSI uninstall.'
        }

        $installedExecutable = Join-Path $installDirectory 'Prometheus.Desktop.exe'
        if (Test-Path -LiteralPath $installedExecutable)
        {
            throw 'The executable remains after MSI uninstall.'
        }

        if ((Test-Path -LiteralPath $desktopShortcutPath) -or
            (Test-Path -LiteralPath $startupShortcutPath) -or
            (Test-Path -LiteralPath $startMenuShortcutPath))
        {
            throw 'One or more Prometheus shortcuts remain after MSI uninstall.'
        }
    }

    if ($verificationSucceeded -and (Test-Path -LiteralPath $smokeDirectory))
    {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}

[pscustomobject]@{
    ProductName = $productName
    ProductCode = $productCode
    ProductVersion = $productVersion
    InstalledExecutableVersion = $installedExecutableVersion
    DesktopShortcut = $DesktopShortcut
    StartWithWindows = $StartWithWindows
    RunningApplicationClosed = $runningApplicationClosed
    InstallExitCode = $installExitCode
    UninstallExitCode = $uninstallExitCode
} | Format-List
