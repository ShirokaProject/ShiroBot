using System.Text.Json;
using ShiroBot.Components.Updates;

internal static class DeferredShadowCleanupVerification
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.ShadowCleanupVerification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shadows = new List<string>();
        string Shadow()
        {
            var path = Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin-reload", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "fixture.dll"), "old code");
            shadows.Add(path);
            return path;
        }
        try
        {
            var normal = Shadow();
            if (DeferredShadowCleanup.DeleteOrStage(root, normal) is not null || Directory.Exists(normal) ||
                Directory.Exists(Path.Combine(root, ".update"))) throw new Exception("Normal cleanup must not stage or require restart.");

            var movable = Shadow();
            var pending = DeferredShadowCleanup.DeleteOrStage(root, movable, _ => throw new IOException("locked deletion"))!;
            if (Directory.Exists(movable) || File.ReadAllText(Path.Combine(pending, "files", "fixture.dll")) != "old code")
                throw new Exception("Failed deletion did not move the old shadow into .update.");
            // Pending cleanup is not an installable update package.
            var staged = StagedComponentUpdates.GetStagingDirectory(root, "Example");
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, "new.dll"), "new code");
            StagedComponentUpdates.WriteTarget(staged, Path.Combine(root, "Example"));
            var update = StagedComponentUpdates.ApplyStaged(root);
            if (update.Applied.Count != 1 || update.Failed.Count != 0 || !Directory.Exists(pending))
                throw new Exception("Shadow cleanup interfered with applying a staged update.");
            var retry = DeferredShadowCleanup.RetryPending(root);
            if (retry.Applied != 1 || retry.Failed.Count != 0 || Directory.Exists(pending))
                throw new Exception("Next-start cleanup did not remove the staged shadow.");

            var immovable = Shadow();
            pending = DeferredShadowCleanup.DeleteOrStage(root, immovable,
                _ => throw new IOException("locked deletion"), (_, _) => throw new IOException("move denied"))!;
            if (!Directory.Exists(immovable) || !File.Exists(Path.Combine(pending, "pending.json")))
                throw new Exception("A locked or cross-volume shadow lost its deferred cleanup record.");
            retry = DeferredShadowCleanup.RetryPending(root);
            if (retry.Applied != 1 || retry.Failed.Count != 0 || Directory.Exists(immovable) || Directory.Exists(pending))
                throw new Exception("Next-start cleanup did not retry an unmoved original shadow.");

            var interrupted = Shadow();
            pending = DeferredShadowCleanup.DeleteOrStage(root, interrupted,
                _ => throw new IOException("locked deletion"), (_, _) => throw new IOException("move denied"))!;
            File.Delete(Path.Combine(pending, "pending.json"));
            retry = DeferredShadowCleanup.RetryPending(root);
            if (retry.Applied != 1 || retry.Failed.Count != 0 || Directory.Exists(interrupted))
                throw new Exception("A partial marker deletion lost the original shadow cleanup path.");

            var invalidSource = Shadow();
            pending = DeferredShadowCleanup.DeleteOrStage(root, invalidSource, _ => throw new IOException("locked deletion"))!;
            var outside = Path.Combine(root, "keep"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "user-data"), "keep");
            File.WriteAllText(Path.Combine(pending, "pending.json"), JsonSerializer.Serialize(new { OriginalPath = outside }));
            retry = DeferredShadowCleanup.RetryPending(root);
            if (retry.Failed.Count != 1 || !Directory.Exists(pending) || !File.Exists(Path.Combine(outside, "user-data")))
                throw new Exception("Invalid cleanup records must remain reported and must not delete user data.");
            Directory.Delete(pending, recursive: true);

            if (OperatingSystem.IsWindows())
            {
                var locked = Shadow();
                using (File.Open(Path.Combine(locked, "fixture.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    pending = DeferredShadowCleanup.DeleteOrStage(root, locked)!;
                    retry = DeferredShadowCleanup.RetryPending(root);
                    if (retry.Failed.Count != 1 || !Directory.Exists(pending))
                        throw new Exception("A live Windows file lock must keep the restart cleanup pending.");
                }
                retry = DeferredShadowCleanup.RetryPending(root);
                if (retry.Applied != 1 || retry.Failed.Count != 0 || Directory.Exists(pending))
                    throw new Exception("Releasing a Windows lock did not allow restart cleanup.");
                Console.WriteLine("PASS: DeferredShadowCleanupWithActualWindowsFileLock");
            }
            Console.WriteLine("PASS: DeferredShadowCleanupMoveFallbackRestartRetryAndStagedUpdateIsolation");
        }
        finally
        {
            foreach (var path in shadows) if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }
}
