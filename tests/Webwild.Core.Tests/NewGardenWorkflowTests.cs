using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// H0 contracts: every New Garden path saves and archives the old world
/// transactionally, failures never discard it, and restoring the archive
/// brings back its identity, chunks and persistent modifications.
/// </summary>
public class NewGardenWorkflowTests : IDisposable
{
    private readonly string _root;

    public NewGardenWorkflowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "webwild-newgarden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private WorldSaveService Service(ISaveFileSystem? fs = null) => new(
        Path.Combine(_root, "world"), Path.Combine(_root, "legacy.json"), fs);

    private static SaveGameData MakeSave(GameSimulation sim, int seed, int food, int silk,
        params string[] explored)
    {
        var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
        data.Seed = seed;
        data.SaveId = Guid.NewGuid().ToString("N");
        data.Food = food;
        data.Silk = silk;
        data.ExploredChunks = explored.ToList();
        return data;
    }

    private static void AddChunkRecord(WorldSaveService service, int seed, string id = "food:0_2:0:berries")
    {
        var coordinate = new ChunkCoordinate(0, 2);
        var record = new ChunkChangeSave
        {
            ChunkX = coordinate.X,
            ChunkZ = coordinate.Z,
            WorldSeed = seed,
            FoodSources = new List<FoodSourceStateSave>
            {
                new() { Id = id, Kind = "berries", X = 4, Y = 0.2, Z = 50, AvailableAtTick = 900 }
            }
        };
        Assert.True(service.WorldStore.SaveChunkChanges(record).Success);
    }

    [Fact]
    public void InGameNewGarden_SavesArchivesAndRestoresTheWholePreviousWorld()
    {
        var service = Service();
        var sim = TestWorld.NewSim();
        var old = MakeSave(sim, 111, 31, 12, "0_0", "1_0");
        Assert.True(service.Save(old).Success);
        AddChunkRecord(service, 111);

        // The in-game action first saves the live session, then archives it.
        var progressed = MakeSave(sim, 111, 35, 9, "0_0", "1_0", "2_0");
        Assert.True(service.PrepareNewGarden(progressed, out var message), message);
        Assert.True(service.HasArchive);
        Assert.Equal(111, service.LoadArchive().Data!.Seed);

        // Start the replacement garden and save it.
        var replacement = MakeSave(sim, 222, 5, 20, "0_0");
        Assert.True(service.Save(replacement).Success);
        var live = service.Load();
        Assert.True(live.Success, live.Message);
        Assert.Equal(222, live.Data!.Seed);
        Assert.Equal(5, live.Data.Food);

        // The previous garden keeps its identity, economy and chunk changes.
        var restored = service.RestoreArchive();
        Assert.True(restored.Success, restored.Message);
        Assert.Equal(111, restored.Data!.Seed);
        Assert.Equal(35, restored.Data.Food);
        Assert.Contains("2_0", restored.Data.ExploredChunks);
        var current = service.Load();
        Assert.True(current.Success, current.Message);
        Assert.Equal(111, current.Data!.Seed);
        Assert.Equal(35, current.Data.Food);
        var chunk = service.WorldStore.LoadChunkChanges(new ChunkCoordinate(0, 2), out var chunkError);
        Assert.NotNull(chunk);
        Assert.Null(chunkError);
        Assert.Equal(111, chunk!.WorldSeed);
        Assert.Contains(chunk.FoodSources, food => food.Id == "food:0_2:0:berries");
    }

    [Fact]
    public void Archive_ReplacesAnOlderArchiveAndRetiresLiveChunkRecords()
    {
        var service = Service();
        var sim = TestWorld.NewSim();
        Assert.True(service.Save(MakeSave(sim, 10, 1, 1, "0_0")).Success);
        AddChunkRecord(service, 10, "food:0_2:0:old");
        Assert.True(service.ArchiveExisting(out var first), first);

        Assert.True(service.Save(MakeSave(sim, 20, 2, 2, "1_0")).Success);
        AddChunkRecord(service, 20, "food:0_2:0:new");
        Assert.True(service.ArchiveExisting(out var second), second);
        Assert.Equal(20, service.LoadArchive().Data!.Seed);
        Assert.Empty(service.WorldStore.ListChangedChunks());

        var restored = service.RestoreArchive();
        Assert.True(restored.Success, restored.Message);
        Assert.Equal(20, restored.Data!.Seed);
        var chunk = service.WorldStore.LoadChunkChanges(new ChunkCoordinate(0, 2), out _);
        Assert.Contains(chunk!.FoodSources, food => food.Id == "food:0_2:0:new");
        Assert.DoesNotContain(chunk.FoodSources, food => food.Id == "food:0_2:0:old");
    }

    [Theory]
    [InlineData("write")]
    [InlineData("copy")]
    [InlineData("move")]
    [InlineData("corrupt-chunk")]
    public void ArchiveFailure_LeavesTheLiveGardenIntactAndReportsFailure(string failure)
    {
        var inner = new DefaultSaveFileSystem();
        var baseline = Service();
        var sim = TestWorld.NewSim();
        var save = MakeSave(sim, 77, 14, 8, "0_0");
        Assert.True(baseline.Save(save).Success);
        AddChunkRecord(baseline, 77);
        var manifestPath = baseline.WorldStore.ManifestPath;
        var before = File.ReadAllBytes(manifestPath);
        if (failure == "corrupt-chunk")
        {
            var chunkPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "chunks", "chunk_0_2.json");
            File.WriteAllText(chunkPath, "{ not valid json");
        }

        var failing = new SwitchingFileSystem(inner)
        {
            FailWrites = failure == "write",
            FailCopy = failure == "copy",
            FailMove = failure == "move"
        };
        var service = Service(failing);
        Assert.False(service.ArchiveExisting(out var message));
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.False(service.HasArchive);
        var live = service.Load();
        Assert.True(live.Success, live.Message);
        Assert.Equal(77, live.Data!.Seed);
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
        if (failure != "corrupt-chunk")
        {
            var chunk = service.WorldStore.LoadChunkChanges(new ChunkCoordinate(0, 2), out _);
            Assert.NotNull(chunk);
            Assert.Contains(chunk!.FoodSources, food => food.Id == "food:0_2:0:berries");
        }
    }

    [Fact]
    public void RestoreFailure_LeavesTheCurrentLiveGardenReadable()
    {
        var inner = new DefaultSaveFileSystem();
        var service = Service(inner);
        var sim = TestWorld.NewSim();
        Assert.True(service.Save(MakeSave(sim, 303, 3, 3, "0_0")).Success);
        AddChunkRecord(service, 303);
        Assert.True(service.ArchiveExisting(out var message), message);
        var current = MakeSave(sim, 404, 44, 44, "9_9");
        Assert.True(service.Save(current).Success);

        var failing = new SwitchingFileSystem(inner) { FailMove = true };
        var restore = new WorldSaveService(
            Path.Combine(_root, "world"), Path.Combine(_root, "legacy.json"), failing)
            .RestoreArchive();
        Assert.False(restore.Success);
        var live = service.Load();
        Assert.True(live.Success, live.Message);
        Assert.Equal(404, live.Data!.Seed);
    }

    [Fact]
    public void PreparedNewGarden_WithNoExistingSave_IsRefusedWithoutClaimingABackup()
    {
        var service = Service();
        var sim = TestWorld.NewSim();
        Assert.False(service.ArchiveExisting(out var message));
        Assert.Contains("no saved garden", message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.HasArchive);
        // The first in-game New Garden has no earlier save to discard. Saving
        // it creates the live garden, which is then archived safely.
        Assert.True(service.PrepareNewGarden(
            MakeSave(sim, 1, 1, 1, "0_0"), out var prepared), prepared);
        Assert.True(service.HasArchive);
    }

    [Fact]
    public void ArchiveFailureAfterPublishingTheManifest_RestoresThePreviousArchive()
    {
        var inner = new DefaultSaveFileSystem();
        var service = Service(inner);
        var sim = TestWorld.NewSim();

        // World A is kept as the previous garden.
        Assert.True(service.Save(MakeSave(sim, 500, 5, 5, "0_0")).Success);
        AddChunkRecord(service, 500, "food:0_2:0:old");
        Assert.True(service.ArchiveExisting(out var first), first);

        // World B is live. A directory-move failure lands after the new
        // manifest has been published but before its chunks are installed.
        Assert.True(service.Save(MakeSave(sim, 600, 6, 6, "1_0")).Success);
        AddChunkRecord(service, 600, "food:0_2:0:new");
        var failing = new SwitchingFileSystem(inner) { FailNextDirectoryMove = true };
        var attempt = new WorldSaveService(
            Path.Combine(_root, "world"), Path.Combine(_root, "legacy.json"), failing);
        Assert.False(attempt.ArchiveExisting(out var message));
        Assert.False(string.IsNullOrWhiteSpace(message));

        // The verified archive is still world A, manifest and chunks together.
        var archived = service.LoadArchive();
        Assert.True(archived.Success, archived.Message);
        Assert.Equal(500, archived.Data!.Seed);
        var restored = service.RestoreArchive();
        Assert.True(restored.Success, restored.Message);
        Assert.Equal(500, restored.Data!.Seed);
        var chunk = service.WorldStore.LoadChunkChanges(new ChunkCoordinate(0, 2), out _);
        Assert.Contains(chunk!.FoodSources, food => food.Id == "food:0_2:0:old");
        Assert.DoesNotContain(chunk.FoodSources, food => food.Id == "food:0_2:0:new");
    }

    private sealed class SwitchingFileSystem : ISaveFileSystem
    {
        private readonly DefaultSaveFileSystem _inner;
        public SwitchingFileSystem(DefaultSaveFileSystem inner) => _inner = inner;
        public bool FailWrites { get; set; }
        public bool FailCopy { get; set; }
        public bool FailMove { get; set; }
        public bool FailNextDirectoryMove { get; set; }

        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] bytes)
        {
            _inner.WriteAllBytes(path, bytes);
            if (FailWrites) throw new IOException("Simulated write failure");
        }
        public void Move(string source, string destination, bool overwrite)
        {
            if (FailMove) throw new IOException("Simulated move failure");
            _inner.Move(source, destination, overwrite);
        }
        public void Copy(string source, string destination, bool overwrite)
        {
            if (FailCopy) throw new IOException("Simulated copy failure");
            _inner.Copy(source, destination, overwrite);
        }
        public void MoveDirectory(string source, string destination)
        {
            if (FailMove) throw new IOException("Simulated directory move failure");
            if (FailNextDirectoryMove)
            {
                FailNextDirectoryMove = false;
                throw new IOException("Simulated one-shot directory move failure");
            }
            _inner.MoveDirectory(source, destination);
        }
        public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);
        public IReadOnlyList<string> ListFiles(string directory, string pattern) =>
            _inner.ListFiles(directory, pattern);
        public void Delete(string path) => _inner.Delete(path);
        public long Length(string path) => _inner.Length(path);
    }
}
