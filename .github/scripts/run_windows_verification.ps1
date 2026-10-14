$ErrorActionPreference = 'Stop'

function Invoke-Verification([string] $name, [string] $mode) {
    $output = Join-Path $PWD "$name-stdout.log"
    $errors = Join-Path $PWD "$name-stderr.log"
    $process = Start-Process dotnet -ArgumentList "Tests/Verification/bin/Release/net10.0/ShiroBot.Verification.dll $mode" -PassThru -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError $errors
    try {
        if (-not $process.WaitForExit(120000)) {
            $lines = @(Get-Content $output)
            $lines | Write-Output
            Get-Content $errors
            $dump = Join-Path $PWD "$name-hang.dmp"
            dotnet-dump collect --process-id $process.Id --output $dump
            dotnet-dump analyze $dump -c 'clrstack -all' -c 'exit'
            dotnet-dump analyze $dump -c 'dumpasync' -c 'syncblk' -c 'threadpool' -c 'exit'
            throw "$name did not finish within two minutes."
        }
        Get-Content $output
        Get-Content $errors
        if ($process.ExitCode -ne 0) { throw "$name exited with code $($process.ExitCode)." }
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}

# Keep separate logs for the complete suite and the adapter reload regression.
Invoke-Verification 'verification' '--without-adapter-reload-isolation'
Invoke-Verification 'adapter-reload-isolation' '--adapter-reload-isolation'
