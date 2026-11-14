namespace Webwild.Core;

/// <summary>
/// Explicit migration between the legacy schema 1–3 save payload and the
/// schema-4 world manifest.
///
/// Legacy files remain fully readable: a schema ≤ 3 payload is wrapped into a
/// manifest with the player position converted from region-local coordinates to
/// stable world coordinates using the constant landmark origins. Nothing inside
/// the embedded game payload is rewritten, so the existing migration chain
/// (1→2→3) and all committed fixtures keep working unchanged.
/// </summary>
public static class WorldSaveMigrator
{
    public const int LegacySchemaVersion = 3;

    public static WorldManifestSave ToManifest(SaveGameData legacy)
    {
        if (legacy is null) throw new ArgumentNullException(nameof(legacy));
        if (!SaveMigrator.Migrate(legacy, out var error))
            throw new InvalidDataException(error);

        var manifest = new WorldManifestSave
        {
            SchemaVersion = WorldManifestSave.CurrentSchemaVersion,
            SaveId = legacy.SaveId,
            CreatedUtc = legacy.CreatedUtc,
            UpdatedUtc = legacy.UpdatedUtc,
            Seed = legacy.Seed,
            Tick = legacy.Tick,
            ElapsedSeconds = legacy.ElapsedSeconds,
            ActiveLandmarkId = legacy.ActiveRegionId,
            Game = legacy
        };

        var active = LandmarkCatalog.IsKnown(legacy.ActiveRegionId)
            ? legacy.ActiveRegionId
            : LandmarkCatalog.Garden;
        var playerWorld = LandmarkCatalog.ToWorld(active,
            new Vec3(legacy.Player.X, legacy.Player.Y, legacy.Player.Z));
        manifest.Player = WorldPosSave.FromPos(playerWorld);
        manifest.PlayerRotationY = legacy.Player.RotationY;

        var visits = new List<LandmarkVisitSave>();
        var seenLandmarks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in legacy.RegionPlayers)
        {
            if (!LandmarkCatalog.IsKnown(entry.Region) || !seenLandmarks.Add(entry.Region)) continue;
            visits.Add(new LandmarkVisitSave
            {
                LandmarkId = entry.Region,
                LocalX = entry.X, LocalY = entry.Y, LocalZ = entry.Z,
                RotationY = entry.RotationY
            });
        }
        if (!visits.Any(v => v.LandmarkId == active))
        {
            visits.Add(new LandmarkVisitSave
            {
                LandmarkId = active,
                LocalX = legacy.Player.X, LocalY = legacy.Player.Y, LocalZ = legacy.Player.Z,
                RotationY = legacy.Player.RotationY
            });
        }
        manifest.LandmarkVisits = visits;
        return manifest;
    }

    /// <summary>
    /// Rebuild the legacy-shaped payload the existing simulation loads from,
    /// with the active landmark's local player transform restored.
    /// </summary>
    public static SaveGameData ExtractGame(WorldManifestSave manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var game = manifest.Game ?? new SaveGameData();
        game.SaveId = manifest.SaveId;
        game.CreatedUtc = manifest.CreatedUtc;
        game.UpdatedUtc = manifest.UpdatedUtc;
        game.Seed = manifest.Seed;
        game.Tick = manifest.Tick;
        game.ElapsedSeconds = manifest.ElapsedSeconds;
        game.ActiveRegionId = LandmarkCatalog.IsKnown(manifest.ActiveLandmarkId)
            ? manifest.ActiveLandmarkId
            : LandmarkCatalog.Garden;

        var local = LandmarkCatalog.ToLocal(game.ActiveRegionId, manifest.Player.Absolute);
        game.Player = new PlayerSave
        {
            X = local.X, Y = local.Y, Z = local.Z,
            RotationY = manifest.PlayerRotationY
        };

        var visits = manifest.LandmarkVisits
            .Where(v => LandmarkCatalog.IsKnown(v.LandmarkId))
            .Select(v => new RegionPlayerSave
            {
                Region = v.LandmarkId,
                X = v.LocalX, Y = v.LocalY, Z = v.LocalZ,
                RotationY = v.RotationY
            })
            .ToList();
        if (!visits.Any(v => v.Region == game.ActiveRegionId))
            visits.Add(new RegionPlayerSave
            {
                Region = game.ActiveRegionId,
                X = game.Player.X, Y = game.Player.Y, Z = game.Player.Z,
                RotationY = game.Player.RotationY
            });
        game.RegionPlayers = visits;
        game.VisitedRegions = visits.Select(v => v.Region).Distinct(StringComparer.Ordinal).ToList();
        if (!game.VisitedRegions.Contains(LandmarkCatalog.Garden))
            game.VisitedRegions.Insert(0, LandmarkCatalog.Garden);
        return game;
    }

    /// <summary>Deserialize either manifest or legacy bytes into a manifest.</summary>
    public static WorldManifestSave? FromBytes(byte[] bytes, out string error, out bool wasLegacy)
    {
        wasLegacy = false;
        error = "";
        if (bytes.Length == 0)
        {
            error = "The save file is empty.";
            return null;
        }
        try
        {
            // Legacy payload probe: schema 1–3 files carry their schema at the root.
            using (var probe = new MemoryStream(bytes))
            {
                var probeDoc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonDocument>(probe, SaveValidator.JsonOptions);
                using var _ = probeDoc;
                if (probeDoc != null &&
                    probeDoc.RootElement.TryGetProperty("schemaVersion", out var versionEl) &&
                    versionEl.ValueKind == System.Text.Json.JsonValueKind.Number &&
                    versionEl.TryGetInt32(out var version) &&
                    version is >= 1 and <= LegacySchemaVersion)
                {
                    wasLegacy = true;
                    var legacy = SaveValidator.Deserialize(bytes, out var legacyError);
                    if (legacy is null)
                    {
                        error = legacyError;
                        return null;
                    }
                    return ToManifest(legacy);
                }
            }

            var manifest = System.Text.Json.JsonSerializer.Deserialize<WorldManifestSave>(bytes, SaveValidator.JsonOptions);
            if (manifest is null)
            {
                error = "The save file is empty.";
                return null;
            }
            if (manifest.SchemaVersion > WorldManifestSave.CurrentSchemaVersion)
            {
                error = $"Save version {manifest.SchemaVersion} is newer than this build supports.";
                return null;
            }
            if (!Validate(manifest, out error)) return null;
            return manifest;
        }
        catch (Exception ex)
        {
            error = "The save file is not valid JSON: " + ex.Message;
            return null;
        }
    }

    public static byte[] ToBytes(WorldManifestSave manifest) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, SaveValidator.JsonOptions);

    /// <summary>Manifest-level validation. The embedded game payload is validated
    /// with the existing <see cref="SaveValidator"/> rules.</summary>
    public static bool Validate(WorldManifestSave? manifest, out string error)
    {
        error = "";
        if (manifest is null)
        {
            error = "The save file is empty.";
            return false;
        }
        if (manifest.SchemaVersion != WorldManifestSave.CurrentSchemaVersion)
        {
            error = $"World manifest version {manifest.SchemaVersion} is not supported.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.SaveId) || manifest.SaveId.Length > 80)
        {
            error = "The save ID is missing or invalid.";
            return false;
        }
        if (manifest.Seed < 0 || manifest.Tick < 0 ||
            !double.IsFinite(manifest.ElapsedSeconds) || manifest.ElapsedSeconds < 0 ||
            manifest.ElapsedSeconds > 100_000_000)
        {
            error = "Simulation timing is invalid.";
            return false;
        }
        if (!LandmarkCatalog.IsKnown(manifest.ActiveLandmarkId))
        {
            error = "The save names an unknown active landmark.";
            return false;
        }
        if (manifest.Player is null || !manifest.Player.IsValid)
        {
            error = "The player world position is invalid.";
            return false;
        }
        if (manifest.LandmarkVisits is null || manifest.LandmarkVisits.Count == 0 ||
            manifest.LandmarkVisits.Count > 8 ||
            manifest.LandmarkVisits.Any(v => !LandmarkCatalog.IsKnown(v.LandmarkId)) ||
            manifest.LandmarkVisits.Select(v => v.LandmarkId).Distinct(StringComparer.Ordinal).Count() !=
            manifest.LandmarkVisits.Count ||
            manifest.LandmarkVisits.Any(v =>
                !double.IsFinite(v.LocalX) || !double.IsFinite(v.LocalY) || !double.IsFinite(v.LocalZ) ||
                Math.Abs(v.LocalX) > 60 || Math.Abs(v.LocalZ) > 60 || v.LocalY < -3 || v.LocalY > 40))
        {
            error = "Landmark visits are invalid.";
            return false;
        }
        if (manifest.ExploredChunks is null || manifest.ExploredChunks.Count > 50_000 ||
            manifest.ExploredChunks.Any(id => !ChunkCoordinate.TryParse(id ?? "", out _)) ||
            manifest.ExploredChunks.Distinct(StringComparer.Ordinal).Count() != manifest.ExploredChunks.Count)
        {
            error = "The exploration record is invalid.";
            return false;
        }
        if (!SaveValidator.Validate(manifest.Game, out var gameError))
        {
            error = gameError;
            return false;
        }
        manifest.Game.Settings?.Normalise();
        return true;
    }
}
