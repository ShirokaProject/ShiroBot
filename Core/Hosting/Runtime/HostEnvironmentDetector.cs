using System.Text.RegularExpressions;
using System.Text;

namespace ShiroBot.Hosting.Runtime;

internal sealed record HostExecutionEnvironment(
    string Mode, bool IsContainer, string? Engine = null, string? Sandbox = null,
    string? Host = null, string? Orchestrator = null)
{
    internal bool IsSupervised => IsContainer || Mode == "systemd";
}

/// <summary>Read-only, startup-only detection. Missing or ambiguous fingerprints stay unknown.</summary>
internal static class HostEnvironmentDetector
{
    internal static HostExecutionEnvironment Detect() => Detect(
        ReadBounded, path => File.Exists(path) || Directory.Exists(path),
        Environment.GetEnvironmentVariable, OperatingSystem.IsLinux(), Environment.ProcessId);

    internal static HostExecutionEnvironment Detect(
        Func<string, string?> readFile, Func<string, bool> exists,
        Func<string, string?> environment, bool linux, int processId = 0)
    {
        string Read(string path)
        {
            try { return readFile(path) ?? string.Empty; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return string.Empty; }
        }
        bool Exists(string path)
        {
            try { return exists(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }

        var genericContainer = IsTrue(environment("DOTNET_RUNNING_IN_CONTAINER")) ||
            IsTrue(environment("DOTNET_RUNNING_IN_CONTAINERS"));
        if (!linux) return new(genericContainer ? "container" : "native", genericContainer);

        // These optional declarations cover managers whose identifying VM files are not exposed
        // to the container. They are allowlisted, not interpreted as paths or shell commands.
        var engine = EngineName(environment("SHIROBOT_CONTAINER_ENGINE"));
        var sandbox = SandboxName(environment("SHIROBOT_CONTAINER_RUNTIME"));
        var host = HostName(environment("SHIROBOT_CONTAINER_HOST"));
        var declaration = Read("/run/host/container-manager").Trim();
        if (declaration.Length == 0) declaration = Read("/run/systemd/container").Trim();
        if (declaration.Length == 0)
        {
            // PID 1 may not pass its environment down; retain only this one non-secret variable.
            foreach (var item in Read("/proc/1/environ").Split('\0'))
                if (item.StartsWith("container=", StringComparison.Ordinal)) { declaration = item[10..]; break; }
        }
        if (declaration.Length == 0) declaration = environment("container") ?? string.Empty;
        engine ??= EngineName(declaration);
        sandbox ??= SandboxName(declaration);
        if (EngineName(declaration) is "apple-container" or "wsl-containers")
            host ??= EngineName(declaration);

        // Podman's more specific marker wins over an inherited /.dockerenv in the image.
        var podman = Exists("/run/.containerenv") || Exists("/var/run/.containerenv");
        var docker = Exists("/.dockerenv");
        var cgroups = Read("/proc/1/cgroup") + "\n" + Read("/proc/self/cgroup");
        engine ??= podman ? "podman" : CgroupEngine(cgroups);
        var mounts = Read("/proc/self/mountinfo");
        // Docker uses containerd internally too: a generic 'containerd' substring is not proof
        // that the user started this container with nerdctl or Kubernetes.
        if (engine is null && (docker || Regex.IsMatch(mounts,
                @"/(?:var/lib/)?docker/containers/[0-9a-f]{12,64}/(?:hostname|hosts|resolv\.conf)(?: |$)", RegexOptions.CultureInvariant)))
            engine = "docker";

        var kernel = Read("/proc/sys/kernel/osrelease") + "\n" + Read("/proc/version");
        if (Contains(kernel, "gvisor")) sandbox ??= "gvisor";
        var product = Read("/sys/class/dmi/id/product_name");
        // QEMU/Firecracker/virtiofs/Apple VZ identify virtualization, not its container manager.
        if (Contains(product, "Kata Containers") || Contains(kernel, "-kata")) sandbox ??= "kata";
        if (Read("/proc/1/comm").Trim() == "vminitd")
        {
            engine ??= "apple-container";
            host ??= "apple-container";
        }

        var hosts = Read("/etc/hosts");
        if (host is null)
        {
            if (Contains(kernel, "orbstack")) host = "orbstack";
            else if (Contains(mounts, "/run/desktop/mnt/host") || Exists("/run/desktop/mnt/host")) host = "docker-desktop";
            else if (HasHost(hosts, "host.rancher-desktop.internal")) host = "rancher-desktop";
            else if (HasHost(hosts, "host.lima.internal") || Exists("/run/lima-guestagent.sock")) host = "lima";
            else if (Contains(kernel, "microsoft") || Contains(kernel, "WSL2")) host = "wsl2";
            else if (Contains(kernel, "linuxkit")) host = "linuxkit";
        }
        var kubernetes = Exists("/var/run/secrets/kubernetes.io/serviceaccount") ||
            !string.IsNullOrWhiteSpace(environment("KUBERNETES_SERVICE_HOST")) || Contains(cgroups, "kubepods");
        var isContainer = genericContainer || declaration.Length > 0 || engine is not null ||
            sandbox is not null || kubernetes || podman || docker;
        var mode = isContainer ? engine ?? sandbox ?? "container"
            : IsSystemdService(Read, environment, processId == 0 ? Environment.ProcessId : processId) ? "systemd" : "native";
        return new(mode, isContainer, engine, sandbox, host, kubernetes ? "kubernetes" : null);
    }

    private static bool IsSystemdService(Func<string, string> read, Func<string, string?> environment, int processId)
    {
        // Shells, SSH, tmux and desktop terminals can inherit a service's INVOCATION_ID.
        // It identifies the original invocation, not necessarily this application's supervisor.
        if (!Guid.TryParseExact(environment("INVOCATION_ID"), "N", out var invocation) || invocation == Guid.Empty)
            return false;

        var lines = read("/proc/self/cgroup").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var hierarchy = lines.FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal))
            ?? lines.FirstOrDefault(line => line.Split(':', 3) is [_, var controllers, _] &&
                controllers.Split(',').Contains("name=systemd", StringComparer.Ordinal));
        var path = hierarchy?.Split(':', 3).ElementAtOrDefault(2);
        var unit = path?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(part =>
            part.EndsWith(".service", StringComparison.Ordinal) || part.EndsWith(".scope", StringComparison.Ordinal));
        if (unit is not null && (!unit.EndsWith(".service", StringComparison.Ordinal) ||
                Regex.IsMatch(unit, @"^user@\d+\.service$"))) return false;

        var execPid = environment("SYSTEMD_EXEC_PID");
        if (!string.IsNullOrEmpty(execPid))
        {
            if (!int.TryParse(execPid, out var launchedPid) || launchedPid <= 0) return false;
            if (launchedPid == processId) return true;
            // ExecStart may use a shell script without exec. Accept only its immediate,
            // non-interactive child in the same service, not commands typed into a terminal.
            var parent = ProcessParent(read);
            return unit is not null && parent.Pid == launchedPid && parent.Terminal == 0 &&
                read($"/proc/{parent.Pid}/comm").Trim() is "sh" or "bash" or "dash" or "zsh" or "ksh";
        }

        // Older managers without SYSTEMD_EXEC_PID need both a service cgroup and a
        // direct systemd parent. Do not reuse PID 1's cgroup or merely detect systemd on the OS.
        if (unit is null) return false;
        var legacyParent = ProcessParent(read).Pid;
        return legacyParent > 0 && read($"/proc/{legacyParent}/comm").Trim() == "systemd";
    }

    private static (int Pid, int? Terminal) ProcessParent(Func<string, string> read)
    {
        var stat = read("/proc/self/stat");
        var end = stat.LastIndexOf(')');
        if (end < 0) return (0, null);
        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !int.TryParse(fields[1], out var parentPid)) return (0, null);
        return (parentPid, fields.Length >= 5 && int.TryParse(fields[4], out var terminal) ? terminal : null);
    }

    private static string? CgroupEngine(string text)
    {
        if (Regex.IsMatch(text, @"(?:/libpod-[0-9a-f]{12,64}(?:\.scope|/|$)|/libpod\.slice(?:/|$))")) return "podman";
        if (Regex.IsMatch(text, @"/crio-[0-9a-f]{12,64}(?:\.scope|/|$)")) return "cri-o";
        if (Regex.IsMatch(text, @"/(?:cri-containerd-|containerd[-/])[0-9a-f]{12,64}(?:\.scope|/|$)")) return "containerd";
        if (Regex.IsMatch(text, @"/docker[-/][0-9a-f]{12,64}(?:\.scope|/|$)")) return "docker";
        return null;
    }

    private static bool IsTrue(string? value) => value is not null &&
        (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
    private static bool HasHost(string hosts, string name) => hosts.Split('\n').Any(line =>
        line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static string? EngineName(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "docker" or "moby" => "docker",
        "podman" or "libpod" => "podman",
        "containerd" or "nerdctl" => "containerd",
        "cri-o" or "crio" => "cri-o",
        "apple" or "apple-container" => "apple-container",
        "wslc" or "wsl-containers" => "wsl-containers",
        "lxc" or "lxc-libvirt" => "lxc",
        "systemd-nspawn" => "systemd-nspawn",
        _ => null
    };
    private static string? SandboxName(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "gvisor" or "runsc" => "gvisor",
        "kata" or "kata-containers" => "kata",
        _ => null
    };
    private static string? HostName(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "orbstack" => "orbstack",
        "docker-desktop" => "docker-desktop",
        "colima" => "colima",
        "lima" => "lima",
        "rancher-desktop" => "rancher-desktop",
        "apple" or "apple-container" => "apple-container",
        "wslc" or "wsl-containers" => "wsl-containers",
        "wsl" or "wsl2" => "wsl2",
        _ => null
    };

    private static string? ReadBounded(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            Span<char> buffer = stackalloc char[4096];
            var text = new StringBuilder();
            while (text.Length < 65536)
            {
                var count = reader.Read(buffer[..Math.Min(buffer.Length, 65536 - text.Length)]);
                if (count == 0) break;
                text.Append(buffer[..count]);
            }
            return text.ToString();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
