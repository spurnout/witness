using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationOperatorPackServiceTests
{
    [TestMethod]
    public async Task CreateAsync_GeneratesRequiredDesktopLanePacket()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationOperatorPackService().CreateAsync(new ManualValidationOperatorPackRequest
            {
                RootPath = root,
                CliPath = @"C:\Tools\GoatShot.Cli.exe",
                AppPath = @"C:\Tools\GoatShot.exe"
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Keyboard Traversal", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual(7, result.RequiredOpenCount);
            Assert.AreEqual(6, result.Lanes.Count);
            Assert.IsTrue(File.Exists(result.ChecklistPath));
            Assert.IsTrue(File.Exists(result.CommandReferencePath));
            Assert.IsTrue(File.Exists(result.ManifestPath));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "keyboard-traversal",
                    "screen-reader",
                    "text-scaling",
                    "high-contrast",
                    "live-region-drag",
                    "clean-machine-install"
                },
                result.Lanes.Select(lane => lane.Id).ToArray());

            var checklist = await File.ReadAllTextAsync(result.ChecklistPath);
            StringAssert.Contains(checklist, "Do not mark a lane `Passed` unless the named human interaction was actually performed.");
            StringAssert.Contains(checklist, "--proof-scene");

            var commands = await File.ReadAllTextAsync(result.CommandReferencePath);
            StringAssert.Contains(commands, "This script prints command templates. It does not execute record-lane commands.");
            StringAssert.Contains(commands, "manual-validation record-lane");
            StringAssert.Contains(commands, "--status");
            StringAssert.Contains(commands, "passed");
            StringAssert.Contains(commands, "failed");
            StringAssert.Contains(commands, "blocked");

            var keyboard = result.Lanes.Single(lane => lane.Id == "keyboard-traversal");
            Assert.IsTrue(File.Exists(keyboard.NotesPath));
            var keyboardNotes = await File.ReadAllTextAsync(keyboard.NotesPath);
            StringAssert.Contains(keyboardNotes, "Keyboard Traversal Operator Notes");
            StringAssert.Contains(keyboardNotes, "This is observed behavior only, not accessibility compliance certification.");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_ExcludesAlreadyPassedRequiredDesktopLane()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var update = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "keyboard",
                Status = "passed",
                Note = "Keyboard traversal completed on safe demo content."
            });
            Assert.IsTrue(update.Succeeded, update.Message);

            var result = await new ManualValidationOperatorPackService().CreateAsync(new ManualValidationOperatorPackRequest
            {
                RootPath = root
            });

            Assert.IsFalse(result.Lanes.Any(lane => lane.Id == "keyboard-traversal"));
            Assert.AreEqual(5, result.Lanes.Count);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-operator-pack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
