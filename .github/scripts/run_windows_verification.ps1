$ErrorActionPreference = 'Stop'

function Invoke-Verification([string] $name, [string] $mode, [bool] $allowKnownRuntimeHang = $false) {
    $output = Join-Path $PWD "$name-stdout.log"
    $errors = Join-Path $PWD "$name-stderr.log"
    $process = Start-Process dotnet -ArgumentList "Tests/Verification/bin/Release/net10.0/ShiroBot.Verification.dll $mode" -PassThru -NoNewWindow -RedirectStandardOutput $output -RedirectStandardError $errors
    try {
        if (-not $process.WaitForExit(120000)) {
            $lines = @(Get-Content $output)
            $lines | Write-Output
            Get-Content $errors
            $runtimeLine = $lines | Where-Object { $_ -match '^Verification runtime: ' } | Select-Object -First 1
            $lastLine = $lines | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 1
            $runtime = if ($runtimeLine) { [version]($runtimeLine -replace '^Verification runtime: ', '') } else { $null }
            # TODO(#5): Remove after verifying the upstream fix on an official .NET runtime.
            # https://github.com/ShirokaProject/ShiroBot/issues/5
            # Only this specific hang on affected Windows runtimes is non-blocking.
            if ($allowKnownRuntimeHang -and $IsWindows -and $runtime -and
                $runtime.Major -eq 10 -and $runtime.Minor -eq 0 -and $runtime.Build -le 12 -and
                $lastLine -match '^Adapter load probe: initial config: ') {
                Write-Output '::warning::Known Windows .NET generic dispatch hang reproduced; see https://github.com/ShirokaProject/ShiroBot/issues/5. All other verification remains mandatory.'
                return
            }
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

# Separate processes prevent the hung regression probe from suppressing the rest of the suite.
Invoke-Verification 'verification' '--without-adapter-reload-isolation'
Invoke-Verification 'adapter-reload-isolation' '--adapter-reload-isolation' $true
