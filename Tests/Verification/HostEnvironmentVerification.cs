using ShiroBot.Hosting.Runtime;

namespace ShiroBot.Verification;

internal static class HostEnvironmentVerification
{
    public static void Run()
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const int processId = 4242;
        var cases = 0;
        Check("native", false);
        Check("native", false, env: new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        Check("native", false,
            env: new Dictionary<string, string> { ["INVOCATION_ID"] = "unit", ["SYSTEMD_EXEC_PID"] = "4242" });
        Check("native", false,
            new Dictionary<string, string> { ["/proc/1/cgroup"] = "0::/system.slice/sshd.service" },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        foreach (var cgroup in new[]
                 {
                     "0::/user.slice/user-1000.slice/session-3.scope",
                     "0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-terminal.scope",
                     "0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-terminal.service",
                     "0::/system.slice/sshd.service", "0::/system.slice/tmux.service"
                 })
            Check("native", false, new Dictionary<string, string>
                {
                    ["/proc/self/cgroup"] = cgroup,
                    ["/proc/self/stat"] = "4242 (ShiroBot) S 4000 4000 4000", ["/proc/4000/comm"] = "bash"
                },
                new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "3000" });
        foreach (var cgroup in new[]
                 {
                     "0::/system.slice/shirobot.service", "0::/system.slice/shirobot@main.service/workers",
                     "0::/user.slice/user-1000.slice/user@1000.service/app.slice/shirobot.service",
                     "2:cpu:/\n1:name=systemd:/system.slice/shirobot.service"
                 })
            Check("systemd", false, new Dictionary<string, string> { ["/proc/self/cgroup"] = cgroup },
                new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4242" });
        // Strong launch metadata still works when the cgroup hierarchy is hidden.
        Check("systemd", false,
            env: new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4242" });
        Check("systemd", false, new Dictionary<string, string>
            {
                ["/proc/self/cgroup"] = "0::/system.slice/shirobot.service",
                ["/proc/self/stat"] = "4242 (ShiroBot) S 4000 4000 4000 0", ["/proc/4000/comm"] = "bash"
            },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4000" });
        Check("native", false, new Dictionary<string, string>
            {
                ["/proc/self/cgroup"] = "0::/system.slice/terminal.service",
                ["/proc/self/stat"] = "4242 (ShiroBot) S 4000 4000 4000 34816", ["/proc/4000/comm"] = "bash"
            },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4000" });
        foreach (var execPid in new[] { "1", "invalid", "-1" })
            Check("native", false, new Dictionary<string, string>
                {
                    ["/proc/self/cgroup"] = "0::/system.slice/shirobot.service",
                    ["/proc/self/stat"] = "4242 (ShiroBot) S 1 1 1", ["/proc/1/comm"] = "systemd"
                },
                new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = execPid });
        Check("native", false,
            new Dictionary<string, string>
                { ["/proc/self/cgroup"] = "0::/user.slice/user-1000.slice/user@1000.service" },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4242" });
        Check("native", false, new Dictionary<string, string> { ["/proc/self/cgroup"] = "0::/session-3.scope" },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id, ["SYSTEMD_EXEC_PID"] = "4242" });
        Check("systemd", false, new Dictionary<string, string>
            {
                ["/proc/self/cgroup"] = "0::/system.slice/shirobot.service",
                ["/proc/self/stat"] = "4242 (Shiro)Bot runner) S 1 1 1", ["/proc/1/comm"] = "systemd\n"
            },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        Check("native", false, new Dictionary<string, string>
            {
                ["/proc/self/cgroup"] = "0::/system.slice/sshd.service",
                ["/proc/self/stat"] = "4242 (ShiroBot) S 4000 1 1", ["/proc/4000/comm"] = "bash"
            },
            new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        Check("native", false, new Dictionary<string, string>
        {
            ["/proc/self/cgroup"] = "0::/system.slice/shirobot.service",
            ["/proc/self/stat"] = "invalid"
        }, new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        Check("native", false, new Dictionary<string, string>
        {
            ["/proc/self/stat"] = "4242 (ShiroBot) S 1 1 1",
            ["/proc/1/comm"] = "systemd"
        }, new Dictionary<string, string> { ["INVOCATION_ID"] = id });
        Check("container", true, env: new Dictionary<string, string> { ["DOTNET_RUNNING_IN_CONTAINER"] = "true" });
        Check("container", true, env: new Dictionary<string, string> { ["DOTNET_RUNNING_IN_CONTAINERS"] = "1" });
        Check("docker", true, new Dictionary<string, string> { ["/.dockerenv"] = "" }, engine: "docker");
        Check("podman", true, new Dictionary<string, string> { ["/run/.containerenv"] = "", ["/.dockerenv"] = "" },
            engine: "podman");
        Check("podman", true, new Dictionary<string, string> { ["/var/run/.containerenv"] = "" }, engine: "podman");
        foreach (var (cgroup, engine) in new[]
                 {
                     ($"0::/docker/{id}", "docker"), ($"0::/system.slice/docker-{id}.scope", "docker"),
                     ($"0::/user.slice/libpod-{id}.scope", "podman"), ("0::/libpod.slice/child", "podman"),
                     ($"0::/kubepods.slice/crio-{id}.scope", "cri-o"),
                     ($"0::/kubepods/burstable/cri-containerd-{id}.scope", "containerd"),
                     ($"0::/containerd/{id}", "containerd")
                 })
            Check(engine, true, new Dictionary<string, string> { ["/proc/self/cgroup"] = cgroup }, engine: engine,
                orchestrator: cgroup.Contains("kubepods") ? "kubernetes" : null);
        Check("podman", true,
            new Dictionary<string, string>
                { ["/proc/1/environ"] = "TOKEN=private\0container=podman\0PATH=/bin\0", ["/.dockerenv"] = "" },
            engine: "podman");
        Check("cri-o", true,
            new Dictionary<string, string> { ["/run/host/container-manager"] = "crio\n", ["/.dockerenv"] = "" },
            engine: "cri-o");
        Check("containerd", true, new Dictionary<string, string> { ["/run/systemd/container"] = "containerd" },
            engine: "containerd");
        Check("lxc", true, env: new Dictionary<string, string> { ["container"] = "lxc-libvirt" }, engine: "lxc");
        Check("container", true, env: new Dictionary<string, string> { ["container"] = "unknown-manager" });
        Check("container", true,
            new Dictionary<string, string> { ["/var/run/secrets/kubernetes.io/serviceaccount"] = "" },
            orchestrator: "kubernetes");
        Check("container", true, env: new Dictionary<string, string> { ["KUBERNETES_SERVICE_HOST"] = "10.0.0.1" },
            orchestrator: "kubernetes");
        Check("docker", true,
            new Dictionary<string, string>
            {
                ["/proc/self/mountinfo"] =
                    $"1 2 0:1 /var/lib/docker/containers/{id}/hostname /etc/hostname rw - ext4 /dev/sda rw"
            }, engine: "docker");
        Check("docker", true,
            new Dictionary<string, string>
            {
                ["/.dockerenv"] = "",
                ["/proc/self/mountinfo"] = "lowerdir=/var/lib/docker/containerd/daemon/snapshots/2/fs",
                ["/proc/1/cgroup"] = "0::/"
            }, engine: "docker");
        Check("native", false,
            new Dictionary<string, string> { ["/proc/1/cgroup"] = "0::/services/docker-backup/containerd-worker" });
        Check("docker", true,
            new Dictionary<string, string>
                { ["/.dockerenv"] = "", ["/proc/version"] = "Linux version 4.19.0-gvisor" }, engine: "docker",
            sandbox: "gvisor");
        Check("gvisor", true, new Dictionary<string, string> { ["/proc/version"] = "Linux version 4.19.0-gvisor" },
            sandbox: "gvisor");
        Check("kata", true, new Dictionary<string, string> { ["/sys/class/dmi/id/product_name"] = "Kata Containers" },
            sandbox: "kata");
        Check("native", false,
            new Dictionary<string, string>
            {
                ["/sys/class/dmi/id/product_name"] = "QEMU Firecracker Cloud-Hypervisor Apple Virtualization",
                ["/proc/self/mountinfo"] = "virtiofs /mnt/shared"
            });
        Check("apple-container", true, new Dictionary<string, string> { ["/proc/1/comm"] = "vminitd\n" },
            engine: "apple-container", host: "apple-container");
        Check("wsl-containers", true, env: new Dictionary<string, string> { ["container"] = "wslc" },
            engine: "wsl-containers", host: "wsl-containers");
        Check("native", false,
            new Dictionary<string, string> { ["/proc/sys/kernel/osrelease"] = "6.6.87-microsoft-standard-WSL2" },
            host: "wsl2");
        Check("docker", true,
            new Dictionary<string, string>
                { ["/.dockerenv"] = "", ["/proc/sys/kernel/osrelease"] = "6.6.87-microsoft-standard-WSL2" },
            engine: "docker", host: "wsl2");
        Check("docker", true,
            new Dictionary<string, string>
                { ["/.dockerenv"] = "", ["/proc/sys/kernel/osrelease"] = "7.2.12-orbstack" }, engine: "docker",
            host: "orbstack");
        Check("native", false, new Dictionary<string, string> { ["/proc/sys/kernel/osrelease"] = "6.10.14-linuxkit" },
            host: "linuxkit");
        Check("docker", true,
            new Dictionary<string, string>
            {
                ["/.dockerenv"] = "",
                ["/proc/self/mountinfo"] = "10 2 0:1 /run/desktop/mnt/host/c /data rw - virtiofs host rw"
            }, engine: "docker", host: "docker-desktop");
        Check("container", true,
            new Dictionary<string, string> { ["/etc/hosts"] = "192.168.5.2 host.lima.internal host.docker.internal" },
            new Dictionary<string, string> { ["DOTNET_RUNNING_IN_CONTAINER"] = "true" }, host: "lima");
        Check("container", true,
            new Dictionary<string, string>
                { ["/etc/hosts"] = "192.168.127.1 host.rancher-desktop.internal host.lima.internal" },
            new Dictionary<string, string> { ["DOTNET_RUNNING_IN_CONTAINER"] = "true" }, host: "rancher-desktop");
        Check("native", false,
            new Dictionary<string, string>
            {
                ["/etc/hosts"] =
                    "# 192.168.5.2 host.lima.internal\n192.168.65.1 host.docker.internal\n198.19.249.1 fake.orb.local"
            });
        Check("containerd", true,
            env: new Dictionary<string, string>
            {
                ["SHIROBOT_CONTAINER_ENGINE"] = "nerdctl", ["SHIROBOT_CONTAINER_HOST"] = "Colima",
                ["INVOCATION_ID"] = "unit"
            }, engine: "containerd", host: "colima");
        Check("podman", true,
            env: new Dictionary<string, string>
                { ["SHIROBOT_CONTAINER_ENGINE"] = "podman", ["SHIROBOT_CONTAINER_RUNTIME"] = "runsc" },
            engine: "podman", sandbox: "gvisor");
        Check("native", false,
            env: new Dictionary<string, string>
                { ["SHIROBOT_CONTAINER_ENGINE"] = "$(bad)", ["SHIROBOT_CONTAINER_HOST"] = "invalid" });
        Check("native", false, new Dictionary<string, string> { ["/.dockerenv"] = "" },
            new Dictionary<string, string> { ["INVOCATION_ID"] = "unit" }, linux: false);
        Check("container", true, env: new Dictionary<string, string> { ["DOTNET_RUNNING_IN_CONTAINER"] = "true" },
            linux: false);
        var denied = HostEnvironmentDetector.Detect(_ => throw new UnauthorizedAccessException(),
            _ => throw new IOException(), _ => null, true);
        if (denied != new HostExecutionEnvironment("native", false))
            throw new Exception("Unreadable probes did not fall back");
        Console.WriteLine(
            $"PASS host environment: {cases + 1} cases; engines, sandbox/host layers, ambiguous fingerprints, permissions and supervisor restart policy.");

        return;

        void Check(string mode, bool container, Dictionary<string, string>? files = null,
            Dictionary<string, string>? env = null, string? engine = null, string? sandbox = null,
            string? host = null, string? orchestrator = null, bool linux = true)
        {
            var found = HostEnvironmentDetector.Detect(
                p => files?.GetValueOrDefault(p), p => files?.ContainsKey(p) == true,
                key => env?.GetValueOrDefault(key), linux, processId);
            var expected = new HostExecutionEnvironment(mode, container, engine, sandbox, host, orchestrator);
            if (found != expected) throw new Exception($"Environment case {cases}: expected {expected}, got {found}");
            if (found.IsSupervised != (container || mode == "systemd"))
                throw new Exception("Incorrect restart supervision");
            cases++;
        }
    }
}