# Verification

Run the standard checks with `dotnet run --project Tests/Verification`.

For system-font selection and actual Avalonia headless rendering checks:

```sh
dotnet run --project Tests/Verification -- --avalonia-fonts
```

This checks the Windows, macOS and Linux font preferences, missing-font fallback,
theme resources, and the baselines of mixed Chinese/numeric and numeric-only
metrics. Run it again with `SHIROBOT_DEFAULT_FONT_FAMILY` set to an installed font
to verify the explicit override. Without an installed supported CJK font, the
alignment check is skipped and the platform default is preserved.

For the installed old-version → requested new-version integration checks:

```sh
python3 Tests/Verification/run_update_integration.py --dashboard-dist ../Shirobot.Dashboard/dist
```

The Dashboard path is optional. Two actual loadable component builds (`1.0.0` and `2.0.0`) are created in a temporary directory. An isolated host with API authentication enabled installs the old ZIP via HTTP upload. A loopback Release server supplies the new release metadata and ZIP downloads, so the suite needs no GitHub repository or platform credentials.

The checks also cover deferred plugin deletion, cancellation of staged/console updates on deletion, blocking reinstalls while deletion is pending, and avoiding resurrection at startup. The checks cover plugin HTTP updates, console `update check plugins` / `update confirm`, plugin unload failure and staged application, adapter HTTP updates, pinned adapter assemblies and staged application, adapter startup failure with rollback, and process shutdown without waiting for a pinned assembly. Assertions inspect the running component version, installed files, obsolete file removal, and preservation of user configuration and data. Staged application uses the same startup applier after stopping the component; it does not reboot the machine or restart the host process.

The runner prints an artifact directory containing the old/new packages, each isolated installation, and `report.json`. Keep that directory to inspect a failure. For Windows verification, run the same Python script with .NET 10 installed (`python` may be the appropriate command there).

For actual macOS arm64 host self-update and terminal restart checks, publish two
single-file, framework-dependent hosts from the current source into separate
directories, with host versions `0.9.4` and `0.9.5`, and run:

```sh
python3 Tests/Verification/run_host_restart_integration.py \
  --old-publish /tmp/shiro-host-old --new-publish /tmp/shiro-host-new \
  --install-dir ../demo
```

The runner uses a controlling PTY, verifies two console restarts and an HTTP
restart preserve the foreground process and keyboard input, and tests startup
failure on an occupied API port. It then calls the About page's check/apply
endpoints and verifies a real ZIP download, executable replacement, process
restart, version change, backup cleanup and embedded Dashboard. Release metadata
comes from the live GitHub repository; a loopback download proxy serves the
rebuilt new package. The expected latest release must match `--new-version`.
The install directory must be stopped first. Existing configuration, plugins and
adapters are preserved, a temporary test config disables adapter loading, and
the newest executable is left installed with the test process stopped.
