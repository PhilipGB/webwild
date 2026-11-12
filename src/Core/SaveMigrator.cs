namespace Webwild.Core;

/// <summary>
/// Explicit version migration for the multi-region save schema.
///
/// Schema 1 (v0.1.0-v0.1.2) describes one garden. It is upgraded
/// deterministically: the saved player becomes the garden player, all existing
/// webs/workers/insects belong to Garden Clearing, and no resource, mission,
/// construction or idempotency history is rewritten. Schema 2 adds the active
/// region, per-region player transforms and the Reedwater objective track.
/// Schema 3 adds durable one-time supply cache claims so the grant cannot be
/// replayed after the rolling 512-key ledger history forgets its event key.
/// </summary>
public static class SaveMigrator
{
    public static bool Migrate(SaveGameData? data, out string error)
    {
        error = "";
        if (data is null)
        {
            error = "The save file is empty.";
            return false;
        }
        if (data.SchemaVersion < 1)
        {
            error = $"Save version {data.SchemaVersion} is not supported.";
            return false;
        }
        if (data.SchemaVersion > SaveGameData.CurrentSchemaVersion)
        {
            error = $"Save version {data.SchemaVersion} is newer than this build supports.";
            return false;
        }

        var guard = 0;
        while (data.SchemaVersion < SaveGameData.CurrentSchemaVersion)
        {
            if (++guard > 8)
            {
                error = "Save version migration did not converge.";
                return false;
            }
            switch (data.SchemaVersion)
            {
                case 1:
                    MigrateSchema1To2(data);
                    break;
                case 2:
                    MigrateSchema2To3(data);
                    break;
                default:
                    error = $"Save version {data.SchemaVersion} could not be migrated.";
                    return false;
            }
        }

        NormaliseCurrentDefaults(data);
        if (data.SchemaVersion != SaveGameData.CurrentSchemaVersion)
        {
            error = $"Save version {data.SchemaVersion} could not be migrated.";
            return false;
        }
        return true;
    }

    private static void MigrateSchema1To2(SaveGameData data)
    {
        // Schema 1 has no region marker anywhere. Garden Clearing is the only
        // region it can describe, so the mapping is unambiguous.
        data.ActiveRegionId = RegionIds.GardenClearing;
        data.VisitedRegions = new List<string> { RegionIds.GardenClearing };
        data.RegionPlayers = new List<RegionPlayerSave>
        {
            FromPlayerSave(RegionIds.GardenClearing, data.Player)
        };
        data.RegionalObjectives ??= new RegionalObjectiveSave();
        data.Counters ??= new SimCountersSave();
        data.Counters.ReedwaterBridgeWebsBuilt = 0;
        data.Counters.ReedwaterCaptureWebsBuilt = 0;
        data.Counters.OpenedRegionRoutes ??= new List<string>();
        if (string.IsNullOrWhiteSpace(data.Counters.PendingHatchRegionId))
            data.Counters.PendingHatchRegionId = RegionIds.GardenClearing;

        foreach (var web in data.Webs ?? new List<WebSave>())
            web.Region = RegionIds.GardenClearing;
        foreach (var worker in data.Workers ?? new List<WorkerSave>())
            worker.Region = RegionIds.GardenClearing;
        foreach (var insect in data.Insects ?? new List<InsectSave>())
            insect.Region = RegionIds.GardenClearing;

        data.SchemaVersion = 2;
    }

    private static void MigrateSchema2To3(SaveGameData data)
    {
        // In P1 the Reedwater cache was granted only inside TryTravelTo. A P1
        // save that already lists Reedwater therefore represents a claimed
        // first entry, even if the ledger's bounded history has since evicted
        // the region_supply key. Seed the durable claim so it cannot re-grant.
        data.Counters ??= new SimCountersSave();
        data.Counters.ClaimedSupplyCaches ??= new List<string>();
        if ((data.VisitedRegions?.Contains(RegionIds.ReedwaterBank) ?? false) &&
            !data.Counters.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank))
        {
            data.Counters.ClaimedSupplyCaches.Add(RegionIds.ReedwaterBank);
        }
        data.SchemaVersion = 3;
    }

    private static void NormaliseCurrentDefaults(SaveGameData data)
    {
        data.ActiveRegionId = string.IsNullOrWhiteSpace(data.ActiveRegionId)
            ? RegionIds.GardenClearing
            : data.ActiveRegionId;
        data.VisitedRegions ??= new List<string> { RegionIds.GardenClearing };
        if (!data.VisitedRegions.Contains(RegionIds.GardenClearing))
            data.VisitedRegions.Insert(0, RegionIds.GardenClearing);
        data.RegionPlayers ??= new List<RegionPlayerSave>();
        if (data.RegionPlayers.Count == 0)
            data.RegionPlayers.Add(FromPlayerSave(RegionIds.GardenClearing, data.Player));
        if (!data.RegionPlayers.Any(entry => entry.Region == RegionIds.GardenClearing))
            data.RegionPlayers.Add(FromPlayerSave(RegionIds.GardenClearing, data.Player));

        data.Player ??= new PlayerSave();
        data.Webs ??= new List<WebSave>();
        data.Workers ??= new List<WorkerSave>();
        data.Insects ??= new List<InsectSave>();
        data.Missions ??= new MissionSave();
        data.Weather ??= new WeatherSave();
        data.Settings ??= new SettingsData();
        data.RegionalObjectives ??= new RegionalObjectiveSave();
        data.Counters ??= new SimCountersSave();
        data.Counters.LedgerKeys ??= new List<string>();
        data.Counters.IssuedEventKeys ??= new List<string>();
        data.Counters.OpenedRegionRoutes ??= new List<string>();
        data.Counters.ClaimedSupplyCaches ??= new List<string>();
        if (string.IsNullOrWhiteSpace(data.Counters.PendingHatchRegionId))
            data.Counters.PendingHatchRegionId = RegionIds.GardenClearing;

        // Keep the claim and visit ledgers coherent in both directions. A
        // claimed cache implies the region was visited, and any P1-shaped
        // Reedwater visit implies the cache was already granted.
        foreach (var regionId in data.Counters.ClaimedSupplyCaches)
            if (RegionIds.IsKnown(regionId) && !data.VisitedRegions.Contains(regionId))
                data.VisitedRegions.Add(regionId);
        if (data.VisitedRegions.Contains(RegionIds.ReedwaterBank) &&
            !data.Counters.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank))
            data.Counters.ClaimedSupplyCaches.Add(RegionIds.ReedwaterBank);

        foreach (var web in data.Webs ?? new List<WebSave>())
            if (string.IsNullOrWhiteSpace(web.Region)) web.Region = RegionIds.GardenClearing;
        foreach (var worker in data.Workers ?? new List<WorkerSave>())
            if (string.IsNullOrWhiteSpace(worker.Region)) worker.Region = RegionIds.GardenClearing;
        foreach (var insect in data.Insects ?? new List<InsectSave>())
            if (string.IsNullOrWhiteSpace(insect.Region)) insect.Region = RegionIds.GardenClearing;
    }

    private static RegionPlayerSave FromPlayerSave(string region, PlayerSave? player)
    {
        player ??= new PlayerSave();
        return new RegionPlayerSave
        {
            Region = region,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            RotationY = player.RotationY
        };
    }
}
