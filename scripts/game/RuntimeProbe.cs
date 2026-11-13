using System.Text.Json;
using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Headless/rendered acceptance probe. It drives the same public adapter methods
/// as keyboard and mouse input, then writes a machine-readable report.
/// </summary>
public partial class RuntimeProbe : Node
{
    private Main _main = null!;
    private readonly List<object> _checks = new();
    private int _failures;
    private int _passes;

    public async void Run(Main main)
    {
        _main = main;
        try
        {
            await Execute();
        }
        catch (Exception ex)
        {
            Check("probe-exception", false, ex.ToString());
        }
        finally
        {
            WriteReport();
            await Frames(3);
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private async Task Execute()
    {
        await Frames(6);
        Check("title-visible", _main.TitleVisible, "Title menu built");
        Check("title-no-game", _main.Game is null, "No garden before New Game");
        await Capture("01-title");

        var game = _main.StartGame(null);
        await Frames(10);
        Check("new-game-started", game.Simulation is not null, "Simulation created");
        Check("title-hidden", !_main.TitleVisible, "Title hidden in garden");
        foreach (var sceneCheck in game.ProbeSceneValidation())
            Check("scene-" + sceneCheck.Name, sceneCheck.Pass, sceneCheck.Detail);
        Check("anchors-visible", game.VisibleAnchorCount == 12, $"anchors={game.VisibleAnchorCount}");
        Check("insects-visible", game.VisibleInsectCount == 4, $"insects={game.VisibleInsectCount}");
        Check("hud-ready", game.Player is not null, "Player exists");
        Check("audio-buses", AudioServer.GetBusIndex("Music") >= 0 && AudioServer.GetBusIndex("SFX") >= 0,
            "Music and SFX buses");
        await Capture("02-garden");

        var initialSim = game.Simulation!;
        var startingFood = initialSim.Ledger.Food;
        var startingSilk = initialSim.Ledger.Silk;
        var invalidPair = initialSim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigA, WebType.Capture, out _, out var invalidReason);
        Check("invalid-pair-rejected", !invalidPair && initialSim.Ledger.Silk == startingSilk, invalidReason.Reason);
        initialSim.Ledger.Restore(startingFood, 0);
        var lowSilk = initialSim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out var lowReason);
        Check("low-silk-rejected", !lowSilk && initialSim.Ledger.Silk == 0, lowReason.Reason);
        initialSim.Ledger.Restore(startingFood, startingSilk);
        game.ProbeSetTool(WebType.Capture);
        await Frames(2);
        game.ProbeClearBuild();
        Check("cancel-no-charge", !initialSim.Webs.Any() && initialSim.Ledger.Silk == startingSilk,
            "Cancel leaves silk untouched");

        game.ProbeOpenPause();
        await Frames(4);
        Check("pause-opens", game.PauseModalOpen, "Pause menu");
        await Capture("03-pause");
        game.ProbeCloseModal();
        await Frames(2);
        Check("pause-closes", !game.PauseModalOpen, "Resume hides pause");
        game.ProbeOpenHelp();
        await Frames(4);
        Check("help-opens", game.HelpModalOpen, "How to Play");
        await Capture("04-help");
        game.ProbeCloseModal();
        await Frames(2);
        game.ProbeToggleColony();
        await Frames(4);
        Check("colony-opens", game.ColonyModalOpen, "Colony overview");
        await Capture("05-colony");
        game.ProbeCloseModal();
        await Frames(2);

        var movedStart = game.PlayerPosition;
        Input.ActionPress("move_forward");
        await PhysicsFrames(55);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(6);
        var moved = movedStart.DistanceTo(game.PlayerPosition);
        Check("movement-wasd", moved > 0.4f, $"moved={moved:0.00} units");

        var jumpStart = game.PlayerPosition.Y;
        var maxY = jumpStart;
        Input.ActionPress("jump");
        await PhysicsFrames(2);
        Input.ActionRelease("jump");
        for (var i = 0; i < 34; i++)
        {
            await PhysicsFrames(1);
            maxY = Math.Max(maxY, game.PlayerPosition.Y);
        }
        Check("jump-space", maxY > jumpStart + 0.25f, $"rise={maxY - jumpStart:0.00}");

        Input.ActionPress("jump");
        await PhysicsFrames(4);
        Input.ActionRelease("jump");
        await PhysicsFrames(2);
        game.ProbeOpenPause();
        await Frames(5);
        await PhysicsFrames(2);
        var pausedStart = game.PlayerPosition;
        await PhysicsFrames(30);
        var pausedEnd = game.PlayerPosition;
        Check("pause-freezes-airborne", pausedStart.DistanceTo(pausedEnd) < 0.02f,
            $"start={pausedStart.Y:0.00} paused={pausedEnd.Y:0.00}");
        game.ProbeCloseModal();
        await Frames(5);
        await PhysicsFrames(50);
        Check("pause-resumes-physics", !game.PlayerPhysicsPaused &&
            Math.Abs(game.PlayerPosition.Y - pausedEnd.Y) > 0.05f,
            $"resumed={game.PlayerPosition.Y:0.00} paused={pausedEnd.Y:0.00}");

        game.ProbeTeleport(new Vec3(-5.0, 0.5, 9.0));
        await Frames(3);
        Input.ActionPress("move_forward");
        var climbStart = game.PlayerPosition.Y;
        var climbMax = climbStart;
        for (var i = 0; i < 90; i++)
        {
            await PhysicsFrames(1);
            climbMax = Math.Max(climbMax, game.PlayerPosition.Y);
        }
        Input.ActionRelease("move_forward");
        Check("climb-stem", climbMax > climbStart + 1.5f, $"height={climbMax:0.00}");
        game.ProbeTeleport(WorldLayout.PlayerSpawn);
        await Frames(3);

        game.ProbeTeleport(new Vec3(-11.4, 0.5, 0.1));
        game.ProbeLookAt(new Vec3(-9.5, 1.35, 0.1));
        await Frames(4);
        game.ProbeSetTool(WebType.Capture);
        await Frames(3);
        Check("build-tool-open", game.VisibleAnchorCount == 12, "All anchor sockets exposed");
        var twigSuggested = game.ProbeAnchorVisualState(WorldLayout.TwigA);
        var flowerUnrelated = game.ProbeAnchorVisualState(WorldLayout.FlowerA);
        Check("anchor-recommended-highlight",
            twigSuggested.Contains("suggested=True") && twigSuggested.Contains("alpha=0.95"),
            twigSuggested);
        Check("anchor-unrelated-dimmed",
            flowerUnrelated.Contains("suggested=False") && flowerUnrelated.Contains("alpha=0.24"),
            flowerUnrelated);
        Check("anchor-labels-only-near-or-recommended",
            game.ProbeAnchorLabelVisible(WorldLayout.TwigA) &&
            !game.ProbeAnchorLabelVisible(WorldLayout.FlowerA),
            $"twig={game.ProbeAnchorVisualState(WorldLayout.TwigA)} flower={game.ProbeAnchorVisualState(WorldLayout.FlowerA)}");
        await Capture("06-build-mode");
        var silkBeforeWeb = game.Simulation!.Ledger.Silk;
        var twigA = game.Simulation.GetAnchor(WorldLayout.TwigA)!;
        var twigB = game.Simulation.GetAnchor(WorldLayout.TwigB)!;
        var screenA = game.ProbeScreenPosition(twigA.Position);
        var screenB = game.ProbeScreenPosition(twigB.Position);
        GD.Print($"[PROBE] CLICK twinA={screenA} twinB={screenB} viewport={GetViewport().GetVisibleRect().Size}");
        if (DisplayServer.GetName() == "headless")
        {
            game.ProbeClickAnchor(WorldLayout.TwigA);
            game.ProbeClickAnchor(WorldLayout.TwigB);
        }
        else
        {
            await MouseMove(screenA);
            await Click(screenA);
            await MouseMove(screenB);
            await Click(screenB);
        }
        await Frames(4);
        Check("build-confirm-shows-pair",
            game.ProbeBuildConfirmVisible && game.ProbeBuildConfirmText.Contains('→') &&
            game.ProbeBuildConfirmText.Contains("silk"), game.ProbeBuildConfirmText);
        Check("build-confirm-does-not-spend-yet",
            game.Simulation.Ledger.Silk == silkBeforeWeb && !game.Simulation.Webs.Any(),
            $"silk={game.Simulation.Ledger.Silk}");
        await Capture("06b-build-confirm");

        // Cancel must leave both silk and the world untouched.
        game.ProbeCancelPendingBuild();
        await Frames(2);
        Check("build-cancel-no-charge",
            !game.Simulation.Webs.Any() && game.Simulation.Ledger.Silk == silkBeforeWeb,
            $"webs={game.Simulation.Webs.Count()} silk={game.Simulation.Ledger.Silk}");

        game.ProbeSetTool(WebType.Capture);
        await Frames(2);
        if (DisplayServer.GetName() == "headless")
        {
            game.ProbeClickAnchor(WorldLayout.TwigA);
            game.ProbeClickAnchor(WorldLayout.TwigB);
        }
        else
        {
            await MouseMove(screenA);
            await Click(screenA);
            await MouseMove(screenB);
            await Click(screenB);
        }
        await Frames(3);
        game.ProbeConfirmBuild();
        await Frames(4);
        var captureBuilt = game.Simulation.Webs.Any(w => w.Type == WebType.Capture &&
            w.Connects(WorldLayout.TwigA, WorldLayout.TwigB));
        Check("capture-web-built", captureBuilt, "Confirmed build on Twin Twigs");
        Check("build-confirm-hidden-after-build", !game.ProbeBuildConfirmVisible, "Toolbar returns");
        Check("capture-web-visible", game.VisibleWebCount == 1, $"webs={game.VisibleWebCount}");
        Check("capture-web-cost", game.Simulation.Ledger.Silk == silkBeforeWeb - game.Simulation.Tuning.Webs.Capture.SilkCost,
            $"silk {silkBeforeWeb}->{game.Simulation.Ledger.Silk}");
        var silkAfterWeb = game.Simulation.Ledger.Silk;
        game.ProbeConfirmBuild();
        await Frames(2);
        Check("build-confirm-repeated-click-safe",
            game.Simulation.Webs.Count(w => w.Type == WebType.Capture) == 1 &&
            game.Simulation.Ledger.Silk == silkAfterWeb,
            $"webs={game.Simulation.Webs.Count()} silk={game.Simulation.Ledger.Silk}");
        var occupied = game.Simulation.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out var occupiedReason);
        Check("occupied-edge-rejected", !occupied && game.Simulation.Ledger.Silk == silkAfterWeb, occupiedReason.Reason);
        game.ProbeClearBuild();
        await Capture("07-capture-web");

        game.ProbeAdvance(42);
        await Frames(3);
        var trapped = game.Simulation.Insects.FirstOrDefault(i => i.Phase == InsectPhase.Trapped);
        Check("insect-trapped", trapped is not null, "Fly crossed the capture plane");
        await Capture("08-insect-trapped");
        if (trapped is not null)
        {
            var foodBefore = game.Simulation.Ledger.Food;
            game.ProbeTeleport(trapped.Position);
            await Frames(2);
            game.ProbeInteract();
            await Frames(3);
            Check("food-collected", game.Simulation.Ledger.Food > foodBefore,
                $"food {foodBefore}->{game.Simulation.Ledger.Food}");
        }
        await Frames(8);
        Check("status-clears-after-objective",
            !game.ProbeStatusText.Contains("Capture web built") &&
            game.ProbeStatusText.Length > 0,
            $"status='{game.ProbeStatusText}'");
        Check("m01-complete", game.Simulation.Missions.CompletedMissions.Contains("M01"), "First Threads");

        game.ProbeTeleport(new Vec3(-4.5, 0.5, 1.2));
        game.ProbeLookAt(new Vec3(8.0, 0.6, 1.2));
        await Frames(3);
        Input.ActionPress("move_forward");
        await PhysicsFrames(150);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(30);
        Check("ravine-blocks-without-bridge", game.PlayerPosition.X < -2.5f,
            $"x={game.PlayerPosition.X:0.00}");
        game.ProbeTeleport(WorldLayout.PlayerSpawn);
        await Frames(3);

        game.ProbeTeleport(new Vec3(-5.0, 0.5, 6.0));
        game.ProbeLookAt(new Vec3(0, 0.8, 1.2));
        await Frames(4);
        game.ProbeSetTool(WebType.Bridge);
        await Frames(3);
        var gapA = game.Simulation.GetAnchor(WorldLayout.GapWest)!;
        var gapB = game.Simulation.GetAnchor(WorldLayout.GapEast)!;
        var gapScreenA = game.ProbeScreenPosition(gapA.Position);
        var gapScreenB = game.ProbeScreenPosition(gapB.Position);
        GD.Print($"[PROBE] CLICK gapA={gapScreenA} gapB={gapScreenB}");
        if (DisplayServer.GetName() == "headless")
        {
            game.ProbeClickAnchor(WorldLayout.GapWest);
            game.ProbeClickAnchor(WorldLayout.GapEast);
        }
        else
        {
            await MouseMove(gapScreenA);
            await Click(gapScreenA);
            await MouseMove(gapScreenB);
            await Click(gapScreenB);
        }
        await Frames(4);
        Check("bridge-confirm-shows-pair",
            game.ProbeBuildConfirmVisible && game.ProbeBuildConfirmText.Contains('→'),
            game.ProbeBuildConfirmText);
        game.ProbeConfirmBuild();
        await Frames(4);
        var bridgeBuilt = game.Simulation.Webs.Any(w => w.Type == WebType.Bridge &&
            w.Connects(WorldLayout.GapWest, WorldLayout.GapEast));
        Check("bridge-built", bridgeBuilt, "Confirmed build on Pebble gap");
        Check("bridge-visible", game.VisibleWebCount >= 2, $"webs={game.VisibleWebCount}");
        var gapBridge = game.Simulation.Webs.First(w => w.Type == WebType.Bridge &&
            w.Connects(WorldLayout.GapWest, WorldLayout.GapEast));
        Check("bridge-build-animation-starts",
            game.ProbeBridgeConstructionActive(gapBridge.Id) &&
            game.ProbeBridgeConstructionPartCount(gapBridge.Id) > 0,
            $"active={game.ProbeBridgeConstructionActive(gapBridge.Id)} parts={game.ProbeBridgeConstructionPartCount(gapBridge.Id)}");
        await Capture("09a-bridge-construction");
        if (DisplayServer.GetName() != "headless")
        {
            await Frames(100);
            Check("bridge-build-animation-finishes",
                !game.ProbeBridgeConstructionActive(gapBridge.Id),
                "Planks finish appearing; collision was complete from the first frame");
        }
        var gapRamps = game.ProbeBridgeRampGroundPoints(gapBridge.Id).OrderBy(r => r.X).ToList();
        Check("bridge-ramps-west-east", gapRamps.Count == 2 &&
            gapRamps[0].X <= -WorldLayout.RavineHalfWidth &&
            gapRamps[1].X >= WorldLayout.RavineHalfWidth,
            $"rampX={string.Join(",", gapRamps.Select(r => r.X.ToString("0.00")))}");
        Check("bridge-walkway-collision", game.ProbeBridgeWalkwayShapeCount(gapBridge.Id) > 0,
            $"shapes={game.ProbeBridgeWalkwayShapeCount(gapBridge.Id)}");
        CheckBridgeVisual(game, gapBridge.Id, "bridge-gap");
        var previewApproach = BridgeGeometry.ComputeApproachPoints(
            gapA.Position, gapB.Position, gapA.BridgeGroundPoint, gapB.BridgeGroundPoint);
        var preview = WebVisual.CreatePreview(
            WebType.Bridge, GardenClearing.ToVector3(gapA.Position), GardenClearing.ToVector3(gapB.Position),
            true, GardenClearing.ToVector3(previewApproach.A), GardenClearing.ToVector3(previewApproach.B));
        var previewAssembler = preview.GetNodeOrNull<BridgeVisualAssembler>("BridgePreview");
        Check("bridge-preview-uses-assembler", previewAssembler is not null &&
            previewAssembler.PlankCount >= 8 && previewAssembler.RailTriangleCount > 100 &&
            previewAssembler.DetailCount == 0,
            previewAssembler?.ProbeSignature ?? "missing preview assembler");
        Check("bridge-preview-has-no-collision",
            preview.FindChildren("BridgeWalkway", "", true, false).Count == 0,
            "Preview owns no duplicate walkway collision");
        var previewInvalid = WebVisual.CreatePreview(
            WebType.Bridge, GardenClearing.ToVector3(gapA.Position), GardenClearing.ToVector3(gapB.Position),
            false, GardenClearing.ToVector3(previewApproach.A), GardenClearing.ToVector3(previewApproach.B));
        var invalidAssembler = previewInvalid.GetNodeOrNull<BridgeVisualAssembler>("BridgePreview");
        Check("bridge-preview-valid-cue", previewAssembler is not null &&
            previewAssembler.PreviewColor.G > previewAssembler.PreviewColor.R,
            previewAssembler?.PreviewColor.ToString() ?? "missing");
        Check("bridge-preview-invalid-cue", invalidAssembler is not null &&
            invalidAssembler.PreviewColor.R > invalidAssembler.PreviewColor.G,
            invalidAssembler?.PreviewColor.ToString() ?? "missing");
        preview.QueueFree();
        previewInvalid.QueueFree();
        game.ProbeClearBuild();

        game.ProbeTeleport(new Vec3(-4.5, 0.5, 1.2));
        game.ProbeLookAt(new Vec3(8.0, 0.6, 1.2));
        await Frames(4);
        Input.ActionPress("move_forward");
        await PhysicsFrames(180);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(30);
        Check("bridge-enables-player-crossing", game.PlayerPosition.X > 3.2f,
            $"x={game.PlayerPosition.X:0.00}");
        game.ProbeTeleport(new Vec3(8.0, 0.5, 5.0));
        game.ProbeLookAt(new Vec3(0, 0.6, 1.2));
        await Frames(4);
        await Capture("09-bridge-crossing");
        game.ProbeSetSafePosition(new Vec3(5.0, 0.55, 3.3));
        game.ProbeTeleport(new Vec3(5.0, 0.55, 3.3));
        game.ProbeLookAt(new Vec3(0, 0.5, 1.2));
        await Frames(4);
        await Capture("09b-modular-bridge-detail");
        gapBridge.Durability = Math.Max(1, gapBridge.MaxDurability / 4);
        await Frames(3);
        Check("bridge-damage-visual", game.ProbeBridgeDamageOverlayCount(gapBridge.Id) > 0,
            $"overlays={game.ProbeBridgeDamageOverlayCount(gapBridge.Id)} durability={gapBridge.Durability}");
        await Capture("09c-bridge-damaged");
        gapBridge.Durability = gapBridge.MaxDurability;
        await Frames(3);
        game.ProbeTeleport(WorldLayout.PlayerSpawn);
        await Frames(3);

        // The first hatch needs 4 food; ambient silk production can spend the
        // last unit while the player walks. Collect a caught fly first so this
        // check is about the nursery, not about a one-unit race.
        if (!game.Simulation.CanHatch().CanHatch && game.Simulation.Ledger.Silk >= 4)
        {
            for (var i = 0; i < 60 && !game.Simulation.CanHatch().CanHatch; i++)
            {
                var prey = game.Simulation.RegionInsects.FirstOrDefault(insect => insect.CanBeCollected);
                if (prey is not null)
                {
                    game.ProbeTeleport(prey.Position);
                    await Frames(2);
                    game.ProbeInteract();
                    await Frames(2);
                }
                else game.ProbeAdvance(3);
                await Frames(1);
            }
        }
        game.ProbeOpenNursery();
        await Frames(4);
        GD.Print("[PROBE] NURSERY " + game.DebugUi);
        Check("nursery-counts-living-only",
            game.ProbeNurseryCountText.Contains("1 of 5") &&
            !game.ProbeNurseryCountText.Contains("egg"), game.ProbeNurseryCountText);
        Check("nursery-progress-hidden-before-hatch", !game.ProbeNurseryProgressVisible,
            "No progress bar before an egg");
        await Capture("10-nursery");

        // Start and complete a hatch while the panel is open: status, count and
        // progress must update instead of freezing at 0%.
        var hatchStarted = game.ProbeHatch();
        game.ProbeAdvance(1);
        await Frames(3);
        Check("nursery-progress-visible", game.ProbeNurseryProgressVisible,
            game.ProbeNurseryStatusText);
        Check("nursery-progress-advances", game.ProbeNurseryProgressValue > 0.1,
            $"value={game.ProbeNurseryProgressValue:0.00}");
        Check("nursery-pending-count",
            game.ProbeNurseryCountText.Contains("1 of 5") &&
            game.ProbeNurseryCountText.Contains("1 egg hatching"),
            game.ProbeNurseryCountText);
        Check("nursery-neutral-hatching-text",
            game.ProbeNurseryStatusText.StartsWith("A helper is hatching"),
            game.ProbeNurseryStatusText);
        await Capture("10a-nursery-hatching");
        game.ProbeAdvance(4);
        await Frames(4);
        Check("nursery-refreshes-after-hatch",
            !game.ProbeNurseryProgressVisible &&
            game.ProbeNurseryCountText.Contains("2 of 5") &&
            !game.ProbeNurseryCountText.Contains("egg"),
            $"{game.ProbeNurseryCountText} status='{game.ProbeNurseryStatusText}'");
        await Capture("10b-nursery-hatched");
        game.ProbeCloseModal();
        await Frames(3);
        Check("hatch-started", hatchStarted, "Nursery spends food and silk");
        Check("worker-hatched", game.Simulation.Workers.Count >= 1, $"workers={game.Simulation.Workers.Count}");
        Check("worker-visible", game.VisibleWorkerCount >= 1, $"views={game.VisibleWorkerCount}");
        Check("silk-producers", game.Simulation.SilkProducers == 2, $"producers={game.Simulation.SilkProducers}");

        for (var i = 0; i < 72 && !game.Simulation.Missions.CompletedMissions.Contains("M02"); i++)
        {
            game.ProbeAdvance(5);
            await Frames(2);
        }
        var m02 = game.Simulation.Missions.CompletedMissions.Contains("M02");
        Check("m02-complete", m02, "Tiny Team");
        Check("worker-crossed-bridge", HasEvent("WorkerUsedBridge") || m02, "Bridge traversal event");

        await Capture("11-wind-warning");
        if (!game.Simulation.WindCompleted) game.ProbeAdvance(45);
        await Frames(3);
        Check("wind-completed", game.Simulation.WindCompleted, "Deterministic wind event");
        Check("wind-event", HasEvent("WindEventCompleted"), "Wind event emitted");
        Check("web-damaged-or-repaired", game.Simulation.RepairsCompleted > 0 ||
            game.Simulation.Webs.Any(w => w.Durability < w.MaxDurability) || HasEvent("WebRestoredOrSheltered"),
            "Damage or repair observed");

        for (var i = 0; i < 5 && !HasEvent("WebRestoredOrSheltered"); i++)
        {
            var damaged = game.Simulation.Webs
                .Where(w => w.Durability < w.MaxDurability)
                .OrderBy(w => w.Durability)
                .FirstOrDefault();
            if (damaged is not null)
            {
                var a = game.Simulation.GetAnchor(damaged.AnchorA);
                var b = game.Simulation.GetAnchor(damaged.AnchorB);
                if (a is not null && b is not null)
                {
                    game.ProbeTeleport((a.Position + b.Position) * 0.5);
                    await Frames(2);
                    game.ProbeRepairNearest();
                }
            }
            else
            {
                game.ProbeBuildWeb(WorldLayout.ShelterA, WorldLayout.ShelterB, WebType.Capture);
            }
            game.ProbeAdvance(8);
            await Frames(2);
        }
        Check("web-restored", HasEvent("WebRestoredOrSheltered"), "Repair or sheltered alternative");

        for (var i = 0; i < 12 && !game.Simulation.Missions.CompletedMissions.Contains("M03"); i++)
        {
            game.ProbeAdvance(25);
            await Frames(2);
            var caught = game.Simulation.Insects.FirstOrDefault(ins => ins.Phase == InsectPhase.Trapped);
            if (caught is not null)
            {
                game.ProbeTeleport(caught.Position);
                await Frames(2);
                game.ProbeInteract();
                await Frames(2);
            }
            if (game.Simulation.Missions.CompletedMissions.Contains("M03")) break;
        }
        Check("m03-complete", game.Simulation.Missions.CompletedMissions.Contains("M03"), "Storm-Smart");
        Check("victory", game.Simulation.Victory, "Three story missions complete");
        await Frames(5);
        Check("victory-screen", game.VictoryModalOpen, "Victory screen shown");
        await Capture("12-victory");

        game.ProbeCloseModal();
        await Frames(3);
        game.ProbeOpenSettings();
        await Frames(4);
        await Capture("13-settings");
        game.ProbeCloseSettings();
        await Frames(2);

        // Save from the authored spawn so the reload scene contract can compare
        // the restored player with WorldLayout.PlayerSpawn.
        game.ProbeTeleport(WorldLayout.PlayerSpawn);
        await Frames(2);
        var saved = game.ProbeSave();
        Check("save-succeeds", saved, saved ? "Save written atomically" : game.LastSaveError);
        var loaded = _main.Saves.Load();
        Check("save-loads", loaded.Success && loaded.Data is not null, loaded.Message);
        if (loaded.Data is not null)
        {
            Check("save-missions", loaded.Data.Missions.Completed.Count == 3,
                $"completed={loaded.Data.Missions.Completed.Count}");
            Check("save-webs", loaded.Data.Webs.Count == game.Simulation.Webs.Count,
                $"webs={loaded.Data.Webs.Count}");
            Check("save-workers", loaded.Data.Workers.Count == game.Simulation.Workers.Count,
                $"workers={loaded.Data.Workers.Count}");
        }

        var webCount = game.Simulation.Webs.Count;
        var workerCount = game.Simulation.Workers.Count;
        if (loaded.Data is not null)
        {
            var reloaded = _main.StartGame(loaded.Data);
            await Frames(10);
            Check("reload-webs", reloaded.Simulation.Webs.Count == webCount,
                $"webs {webCount}->{reloaded.Simulation.Webs.Count}");
            Check("reload-workers", reloaded.Simulation.Workers.Count == workerCount,
                $"workers {workerCount}->{reloaded.Simulation.Workers.Count}");
            Check("reload-victory", reloaded.Simulation.Victory, "Victory restored");
            Check("reload-player", reloaded.Player is not null, "Player restored");
            var reloadSceneChecks = reloaded.ProbeSceneValidation();
            foreach (var failedSceneCheck in reloadSceneChecks.Where(check => !check.Pass))
                GD.Print($"[PROBE] SCENE-FAIL {failedSceneCheck.Name} — {failedSceneCheck.Detail}");
            Check("reload-world-scene-authored", reloadSceneChecks.All(check => check.Pass),
                $"{reloadSceneChecks.Count(check => check.Pass)}/{reloadSceneChecks.Count} scene checks after load");
            await Capture("14-reloaded");
        }

        var settings = _main.Settings;
        settings.ReducedMotion = true;
        settings.UiScalePercent = 125;
        settings.SfxVolume = 0.4;
        GameData.CreateSettingsService().Save(settings);
        var reloadedSettings = GameData.CreateSettingsService().Load();
        Check("settings-persist", reloadedSettings.ReducedMotion && reloadedSettings.UiScalePercent == 125 &&
            Math.Abs(reloadedSettings.SfxVolume - 0.4) < 0.01, "Accessibility values round-trip");

        if (_main.Game is { } activeGame)
        {
            settings.LowThreat = true;
            activeGame.ApplySettings(settings);
            var gentleDamage = activeGame.Simulation.Tuning.Weather.ExposedWebDamage;
            Check("low-threat-applies", gentleDamage <= 25 &&
                activeGame.Simulation.Tuning.Weather.WarningSeconds >= 30, "Gentle weather caps wind damage");
            settings.LowThreat = false;
            activeGame.ApplySettings(settings);
            Check("low-threat-restores", activeGame.Simulation.Tuning.Weather.ExposedWebDamage > gentleDamage,
                $"damage={activeGame.Simulation.Tuning.Weather.ExposedWebDamage}");
        }

        _main.ProbeReturnToTitle();
        await Frames(8);
        Check("return-to-menu", _main.TitleVisible, "Game returns to title menu");
        Check("continue-available", _main.ContinueAvailable, "Continue button reports saved garden");
        await Capture("15-title-continue");
        var continued = _main.ProbeContinue();
        await Frames(10);
        Check("continue-loads", continued is not null && continued.Simulation.Victory &&
            continued.Simulation.Webs.Count == webCount, "Continue restores saved garden");

        if (continued is not null)
        {
            continued.SimulateSaveFailureForProbe = true;
            continued.ProbeQuitToMenu();
            await Frames(6);
            Check("failed-save-keeps-session", continued.SaveFailedPromptOpen && !_main.TitleVisible,
                "Failed save offers retry instead of quitting");
            continued.ProbeCloseModal();
            await Frames(2);
            continued.ProbeOpenSettings();
            await Frames(3);
            continued.ProbeShowSettingsError();
            await Frames(3);
            Check("settings-save-error-visible", continued.SettingsErrorVisible,
                "Settings failure shows a retry affordance");
            continued.ProbeCloseSettings();
            await Frames(2);
            continued.SimulateSaveFailureForProbe = false;

            var beforeDismantle = continued.Simulation.Webs.Count;
            var tempBuilt = continued.ProbeBuildWeb(WorldLayout.FlowerA, WorldLayout.FlowerB, WebType.Capture);
            var silkAfterBuild = continued.Simulation.Ledger.Silk;
            var flowerA = continued.Simulation.GetAnchor(WorldLayout.FlowerA)!;
            var flowerB = continued.Simulation.GetAnchor(WorldLayout.FlowerB)!;
            continued.ProbeTeleport((flowerA.Position + flowerB.Position) * 0.5);
            await Frames(2);
            continued.ProbeDismantleNearest();
            await Frames(2);
            Check("dismantle-refunds", tempBuilt && continued.Simulation.Webs.Count == beforeDismantle &&
                continued.Simulation.Ledger.Silk > silkAfterBuild, "Dismantle removes web and refunds silk");
        }

        if (continued is not null)
            await RunReedwaterSection(continued);

        if (_main.Game is { } toastGame)
        {
            settings.UiScalePercent = 100;
            GameData.ApplyUiScale(settings);
            await Frames(6);
            toastGame.ProbeShowToast("Clever Crossing earned. Keep exploring or use the trail to return.");
            await Frames(3);
            CheckToastLane(toastGame, "100");
            toastGame.ProbeShowBuildConfirmation();
            await Frames(3);
            CheckBuildConfirmLane(toastGame, "100");
            await Capture("16b-ui-build-confirm-100");
            toastGame.ProbeHideBuildConfirmation();
            await Capture("16a-ui-toast-100");
        }

        settings.UiScalePercent = 125;
        GameData.ApplyUiScale(settings);
        await Frames(6);
        Check("ui-scale-applies", Math.Abs(GetTree().Root.ContentScaleFactor - 1.25f) < 0.01f,
            $"scale={GetTree().Root.ContentScaleFactor:0.00}");
        if (_main.Game is { } toastGame125)
        {
            toastGame125.ProbeShowToast("Clever Crossing earned. Keep exploring or use the trail to return.");
            await Frames(3);
            CheckToastLane(toastGame125, "125");
            toastGame125.ProbeShowBuildConfirmation();
            await Frames(3);
            CheckBuildConfirmLane(toastGame125, "125");
            toastGame125.ProbeHideBuildConfirmation();
        }
        await Capture("16-ui-scale-125");

        settings.UiScalePercent = 150;
        GameData.ApplyUiScale(settings);
        await Frames(6);
        Check("ui-scale-150-applies", Math.Abs(GetTree().Root.ContentScaleFactor - 1.5f) < 0.01f,
            $"scale={GetTree().Root.ContentScaleFactor:0.00}");
        if (_main.Game is { } toastGame150)
        {
            toastGame150.ProbeShowToast("Clever Crossing earned. Keep exploring or use the trail to return.");
            await Frames(3);
            CheckToastLane(toastGame150, "150");
            toastGame150.ProbeShowBuildConfirmation();
            await Frames(3);
            CheckBuildConfirmLane(toastGame150, "150");
            toastGame150.ProbeHideBuildConfirmation();
        }
        await Capture("17-ui-scale-150");

        if (_main.Game is { } gameForQuit) gameForQuit.ProbeQuitToMenu();
        await Frames(8);
        Check("quit-to-menu", _main.TitleVisible && _main.ContinueAvailable, "Save and quit returns to title");

        // Dedicated bridge-geometry regression in a fresh garden: reversed
        // anchor order, a predominantly Z-axis span, and collision removal.
        var edge = _main.StartGame(null)!;
        await Frames(10);
        Check("edge-game-started", edge.Simulation is not null, "Fresh garden for bridge regressions");
        settings.ReducedMotion = true;
        edge.ApplySettings(settings);

        var reverseBuilt = edge.ProbeBuildWeb(WorldLayout.GapEast, WorldLayout.GapWest, WebType.Bridge);
        await Frames(4);
        Check("bridge-reverse-built", reverseBuilt, "East-first bridge selection accepted");
        if (reverseBuilt)
        {
            var reverseWeb = edge.Simulation!.Webs.First(w => w.Type == WebType.Bridge &&
                w.Connects(WorldLayout.GapWest, WorldLayout.GapEast));
            var reverseRamps = edge.ProbeBridgeRampGroundPoints(reverseWeb.Id);
            Check("bridge-reverse-ramps", reverseRamps.Count == 2 &&
                reverseRamps[0].X >= WorldLayout.RavineHalfWidth &&
                reverseRamps[1].X <= -WorldLayout.RavineHalfWidth,
                $"rampX={string.Join(",", reverseRamps.Select(r => r.X.ToString("0.00")))}");
            Check("bridge-reverse-collision", edge.ProbeBridgeWalkwayShapeCount(reverseWeb.Id) > 0,
                $"shapes={edge.ProbeBridgeWalkwayShapeCount(reverseWeb.Id)}");
            Check("bridge-animation-respects-reduced-motion",
                !edge.ProbeBridgeConstructionActive(reverseWeb.Id),
                "Reduced motion skips the grow-in effect");
            CheckBridgeVisual(edge, reverseWeb.Id, "bridge-reverse");

            edge.ProbeTeleport(new Vec3(-4.5, 0.5, 1.2));
            edge.ProbeLookAt(new Vec3(8.0, 0.6, 1.2));
            await Frames(4);
            Input.ActionPress("move_forward");
            await PhysicsFrames(180);
            Input.ActionRelease("move_forward");
            await PhysicsFrames(30);
            Check("bridge-reverse-enables-crossing", edge.PlayerPosition.X > 3.2f,
                $"x={edge.PlayerPosition.X:0.00}");
            edge.ProbeTeleport(new Vec3(-6.5, 0.6, 4.5));
            edge.ProbeLookAt(new Vec3(0, 0.55, 1.2));
            await Frames(4);
            await Capture("18-bridge-reversed");

            edge.ProbeTeleport(new Vec3(0, 0.5, 1.2));
            await Frames(3);
            edge.ProbeDismantleNearest();
            await Frames(5);
            Check("bridge-removal-clears-collision", edge.ProbeBridgeWalkwayShapeCount(reverseWeb.Id) == 0,
                "Dismantled bridge view owns no collision");
            Check("bridge-removal-removes-web", edge.Simulation.GetWeb(reverseWeb.Id) is null,
                "Dismantled bridge left the simulation");
        }

        edge.ProbeTeleport(new Vec3(-4.5, 0.5, 1.2));
        edge.ProbeLookAt(new Vec3(8.0, 0.6, 1.2));
        await Frames(3);
        Input.ActionPress("move_forward");
        await PhysicsFrames(150);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(20);
        Check("bridge-removal-blocks-crossing", edge.PlayerPosition.X < -2.5f,
            $"x={edge.PlayerPosition.X:0.00}");

        var zBuilt = edge.ProbeBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Bridge);
        await Frames(4);
        Check("bridge-zaxis-built", zBuilt, "Predominantly Z-axis bridge accepted");
        if (zBuilt)
        {
            var zWeb = edge.Simulation!.Webs.First(w => w.Type == WebType.Bridge &&
                w.Connects(WorldLayout.TwigA, WorldLayout.TwigB));
            var zRamps = edge.ProbeBridgeRampGroundPoints(zWeb.Id);
            Check("bridge-zaxis-ramps", zRamps.Count == 2 &&
                Math.Abs(zRamps[0].Z - (-2.6)) < 0.15f &&
                Math.Abs(zRamps[1].Z - 2.8) < 0.15f &&
                Math.Abs(zRamps[0].X + 9.5) < 0.05f,
                $"ramps={string.Join(" ", zRamps.Select(r => $"({r.X:0.0},{r.Z:0.0})"))}");
            Check("bridge-zaxis-collision", edge.ProbeBridgeWalkwayShapeCount(zWeb.Id) > 0,
                $"shapes={edge.ProbeBridgeWalkwayShapeCount(zWeb.Id)}");
            CheckBridgeVisual(edge, zWeb.Id, "bridge-zaxis");
            edge.ProbeTeleport(new Vec3(-7.5, 0.5, 0.1));
            edge.ProbeLookAt(new Vec3(-9.5, 1.1, 0.1));
            await Frames(4);
            await Capture("19-bridge-z-axis");
        }
        _main.ProbeReturnToTitle();
        await Frames(8);
        Check("bridge-regression-quit", _main.TitleVisible, "Fresh bridge-regression garden leaves cleanly");

        // New Garden must never silently replace an existing save.
        var saveTickBefore = _main.ProbeSaveTick();
        var archiveTickBefore = _main.ProbeArchiveTick();
        Check("new-garden-confirm-opens", _main.ProbeRequestNewGarden() && _main.TitleVisible &&
            _main.NewGardenConfirmOpen, "Existing save asks before replacing");
        await Frames(4);
        await Capture("30-new-garden-confirm");
        Check("new-garden-confirm-default-safe", _main.ProbeFocusedText.Contains("Cancel"),
            $"focused='{_main.ProbeFocusedText}'");
        Check("new-garden-repeat-click-stays-confirming", _main.ProbeRequestNewGarden() &&
            _main.NewGardenConfirmOpen && _main.TitleVisible,
            "A second click cannot bypass the confirmation");
        Check("new-garden-cancel", _main.ProbeCancelNewGarden() && !_main.NewGardenConfirmOpen &&
            _main.ProbeSaveTick() == saveTickBefore && _main.ProbeArchiveTick() == archiveTickBefore,
            $"save={_main.ProbeSaveTick()} archive={_main.ProbeArchiveTick()}");
        Check("new-garden-confirm-archives-previous", _main.ProbeRequestNewGarden() &&
            _main.ProbeConfirmNewGarden() && _main.Game is not null &&
            _main.Game.Simulation.Missions.CompletedMissions.Count == 0 &&
            _main.ProbeArchiveTick() == saveTickBefore,
            $"archive={_main.ProbeArchiveTick()} expected={saveTickBefore}");
        if (_main.Game is { } freshGame)
        {
            freshGame.ProbeAdvance(40);
            Check("new-garden-autosave", freshGame.ProbeSave(), freshGame.LastSaveError);
            Check("new-garden-autosave-keeps-archive",
                _main.ProbeArchiveTick() == saveTickBefore,
                $"archive={_main.ProbeArchiveTick()} expected={saveTickBefore}");
        }
        _main.ProbeReturnToTitle();
        await Frames(8);
        Check("restore-previous-available", _main.ArchiveAvailable, "Title offers the kept garden");
        var restoredPrevious = _main.ProbeRestorePreviousGarden();
        await Frames(8);
        Check("restore-previous-garden",
            restoredPrevious && _main.Game is not null &&
            _main.Game.Simulation.TickCount >= saveTickBefore,
            $"restoredTick={_main.Game?.Simulation.TickCount} archived={saveTickBefore}");
        _main.ProbeReturnToTitle();
        await Frames(6);

        // Leave the machine in a clean, default-comfort state for the next launch.
        settings.ReducedMotion = false;
        settings.UiScalePercent = 100;
        settings.SfxVolume = 0.8;
        settings.LowThreat = false;
        GameData.CreateSettingsService().Save(settings);
        GameData.ApplyUiScale(settings);
        await Frames(4);
    }

    private void CheckBridgeVisual(GardenClearing game, string webId, string prefix)
    {
        var signature = game.ProbeBridgeVisualSignature(webId);
        Check($"{prefix}-modular-planks", game.ProbeBridgePlankCount(webId) >= 8, signature);
        Check($"{prefix}-modular-variants", game.ProbeBridgeUsesAllPlankVariants(webId), signature);
        Check($"{prefix}-silk-rails", game.ProbeBridgeRailTriangles(webId) > 100, signature);
        Check($"{prefix}-detail-fixtures", game.ProbeBridgeDetailCount(webId) >= 6, signature);
        Check($"{prefix}-vertex-materials", game.ProbeBridgeUnstyledMeshCount(webId) == 0,
            $"unstyled={game.ProbeBridgeUnstyledMeshCount(webId)} {signature}");
        Check($"{prefix}-no-solid-ribbon", signature.Contains("ribbon=0", StringComparison.Ordinal), signature);
        Check($"{prefix}-layout-not-degenerate", signature.Contains("degenerate=0", StringComparison.Ordinal), signature);
    }

    private void CheckToastLane(GardenClearing game, string scaleLabel)
    {
        var toast = game.ProbeToastRect;
        var mission = game.ProbeMissionRect;
        var actions = game.ProbeActionColumnRect;
        var toolbar = game.ProbeToolbarRect;
        var status = game.ProbeStatusRect;
        var viewport = GetViewport().GetVisibleRect().Size;
        GD.Print($"[PROBE] TOAST {scaleLabel} toast={toast} mission={mission} actions={actions} toolbar={toolbar} status={status} viewport={viewport}");
        Check($"toast-visible-{scaleLabel}", game.ProbeToastVisible, game.ProbeToastText);
        Check($"toast-inside-viewport-{scaleLabel}",
            toast.Position.X >= 0 && toast.Position.Y >= 0 &&
            toast.End.X <= viewport.X + 1 && toast.End.Y <= viewport.Y + 1, $"{toast}");
        Check($"toast-avoids-mission-{scaleLabel}", !toast.Intersects(mission), $"{toast} vs {mission}");
        Check($"toast-avoids-actions-{scaleLabel}", !toast.Intersects(actions), $"{toast} vs {actions}");
        Check($"toast-avoids-toolbar-{scaleLabel}", !toast.Intersects(toolbar), $"{toast} vs {toolbar}");
        Check($"toast-avoids-status-{scaleLabel}", !toast.Intersects(status), $"{toast} vs {status}");
        Check($"toast-avoids-camera-centre-{scaleLabel}",
            toast.Position.X >= viewport.X * 0.58f, $"left={toast.Position.X:0.0} viewport={viewport.X:0.0}");
    }

    private void CheckBuildConfirmLane(GardenClearing game, string scaleLabel)
    {
        var rect = game.ProbeBuildConfirmRect;
        var viewport = GetViewport().GetVisibleRect().Size;
        var mission = game.ProbeMissionRect;
        var actions = game.ProbeActionColumnRect;
        Check($"build-confirm-visible-{scaleLabel}", game.ProbeBuildConfirmVisible,
            game.ProbeBuildConfirmText);
        Check($"build-confirm-inside-viewport-{scaleLabel}",
            rect.Position.X >= 0 && rect.Position.Y >= 0 &&
            rect.End.X <= viewport.X + 1 && rect.End.Y <= viewport.Y + 1, $"{rect} viewport={viewport}");
        Check($"build-confirm-avoids-mission-{scaleLabel}", !rect.Intersects(mission),
            $"{rect} vs {mission}");
        Check($"build-confirm-avoids-actions-{scaleLabel}", !rect.Intersects(actions),
            $"{rect} vs {actions}");
    }

    private async Task RunReedwaterSection(GardenClearing game)
    {
        var sim = game.Simulation;
        var gardenWebsBefore = sim.Webs.Count(web => web.Region == RegionIds.GardenClearing);
        Check("reedwater-unlocked", sim.ReedwaterUnlocked, "Story victory opens the east passage");

        game.ProbeOpenTravel();
        await Frames(4);
        Check("travel-dialog-opens", game.TravelModalOpen, "Destination and explanation are shown");
        await Capture("20-travel-dialog");
        game.ProbeCloseModal();
        await Frames(2);

        var supplyFoodBefore = sim.Ledger.Food;
        var supplySilkBefore = sim.Ledger.Silk;
        var travelled = game.ProbeTravel(RegionIds.ReedwaterBank);
        // Read the grant before a real-time silk batch can spend a food unit.
        var supplyFoodAfter = sim.Ledger.Food;
        var supplySilkAfter = sim.Ledger.Silk;
        await Frames(8);
        Check("travel-to-reedwater", travelled && game.ActiveRegionId == RegionIds.ReedwaterBank,
            $"region={game.ActiveRegionId}");
        var supplyEvent = sim.Events.Any(e => e.Type == "RegionSupply" && e.Region == RegionIds.ReedwaterBank);
        Check("reedwater-supply-granted-once",
            supplyFoodAfter >= supplyFoodBefore + 6 && supplySilkAfter >= supplySilkBefore + 24 &&
            sim.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank),
            $"food={supplyFoodBefore}->{supplyFoodAfter} silk={supplySilkBefore}->{supplySilkAfter} " +
            $"claimed={string.Join(",", sim.ClaimedSupplyCaches)} event={supplyEvent}");
        Check("reedwater-player-arrival",
            game.PlayerPosition.DistanceTo(GardenClearing.ToVector3(ReedwaterLayout.ArrivalPoint)) < 0.6f,
            $"player={game.PlayerPosition}");
        Check("reedwater-anchors-visible", game.VisibleAnchorCount == 10,
            $"anchors={game.VisibleAnchorCount}");
        Check("reedwater-insects-visible", game.VisibleInsectCount == 3,
            $"insects={game.VisibleInsectCount}");
        // Hatch at the outpost while the arrival supply cache still covers the
        // cost; later walking/time-based silk production must not make this
        // deterministic feature check depend on a race.
        var hatchCheck = sim.CanHatch();
        var hatched = game.ProbeHatch();
        Check("reedwater-helper-hatched", hatched,
            hatchCheck.CanHatch ? "Outpost hatch uses shared colony resources" : hatchCheck.Reason);
        await Frames(6);
        await Capture("21-reedwater-arrival");

        var startDry = new Vec3(7.0, 0.55, 8.5);
        var farDry = new Vec3(-7.6, 0.55, -1.0);
        var islandADry = sim.GetAnchor(ReedwaterLayout.IslandAPost)!.Position;
        var islandBDry = sim.GetAnchor(ReedwaterLayout.IslandBPost)!.Position;
        Check("shoreline-start-bank-dry",
            !game.ProbeWaterVolumeAt(startDry) && !game.ProbeWaterHazardAt(startDry),
            $"water={game.ProbeWaterVolumeAt(startDry)} hazard={game.ProbeWaterHazardAt(startDry)}");
        Check("shoreline-far-bank-dry",
            !game.ProbeWaterVolumeAt(farDry) && !game.ProbeWaterHazardAt(farDry),
            $"water={game.ProbeWaterVolumeAt(farDry)} hazard={game.ProbeWaterHazardAt(farDry)}");
        Check("shoreline-island-a-safe", !game.ProbeWaterHazardAt(islandADry),
            $"pos={islandADry} hazard={game.ProbeWaterHazardAt(islandADry)}");
        Check("shoreline-island-b-safe", !game.ProbeWaterHazardAt(islandBDry),
            $"pos={islandBDry} hazard={game.ProbeWaterHazardAt(islandBDry)}");

        var openWaterPoints = new[]
        {
            new Vec3(4.2, -0.3, 8.5),
            new Vec3(-2.5, -0.3, -4.0),
            new Vec3(-6.2, -0.3, 5.0)
        };
        Check("shoreline-open-water-is-hazardous",
            openWaterPoints.All(point => game.ProbeWaterHazardAt(point)),
            string.Join(" ", openWaterPoints.Select(point =>
                $"{point}:{game.ProbeWaterHazardAt(point)}")));

        game.ProbeSetSafePosition(startDry);
        game.ProbeTeleport(new Vec3(4.2, -0.3, 8.5));
        await Frames(8);
        Check("shoreline-water-recovery",
            game.PlayerPosition.DistanceTo(GardenClearing.ToVector3(startDry)) < 0.9f &&
            !game.ProbeInWater(),
            $"player={game.PlayerPosition} safe={GardenClearing.ToVector3(startDry)}");

        // The pond is real collision, not a simulation flag. Walk west from the
        // starting bank without a bridge and verify the water returns the player.
        game.ProbeSetSafePosition(ReedwaterLayout.ArrivalPoint);
        game.ProbeTeleport(new Vec3(6.5, 0.55, 6.5));
        game.ProbeLookAt(new Vec3(0, 0.55, 5.8));
        await Frames(3);
        Input.ActionPress("move_forward");
        await PhysicsFrames(110);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(20);
        Check("water-blocks-without-bridge",
            game.PlayerPosition.X > 5.9f,
            $"x={game.PlayerPosition.X:0.00} inWater={game.ProbeInWater()}");
        await Capture("22-reedwater-pond");
        game.ProbeSetCameraZoom(15);
        game.ProbeTeleport(new Vec3(9.5, 1.2, 12.8));
        game.ProbeLookAt(new Vec3(-4.0, 0.4, 2.0));
        await Frames(4);
        await Capture("22b-reedwater-overview");
        game.ProbeTeleport(new Vec3(3.2, 0.8, 9.0));
        game.ProbeLookAt(new Vec3(1.0, 0.2, 5.8));
        await Frames(4);
        await Capture("22c-first-island");
        game.ProbeSetCameraZoom(10);

        game.ProbeSetSafePosition(new Vec3(8.5, 0.55, 12.0));
        game.ProbeTeleport(new Vec3(8.5, 0.55, 12.0));
        game.ProbeLookAt(new Vec3(4.5, 0.25, 8.0));
        await Frames(4);
        await Capture("22d-start-shoreline");

        game.ProbeSetSafePosition(new Vec3(-8.8, 0.55, 3.0));
        game.ProbeTeleport(new Vec3(-8.8, 0.55, 3.0));
        game.ProbeLookAt(new Vec3(-4.5, 0.25, 2.0));
        await Frames(4);
        await Capture("22e-far-shoreline");

        game.ProbeSetSafePosition(islandADry);
        game.ProbeTeleport(islandADry);
        game.ProbeLookAt(new Vec3(3.8, 0.3, 8.8));
        await Frames(4);
        await Capture("22f-island-edges");

        game.ProbeSetSafePosition(new Vec3(7.5, 0.55, 11.5));
        game.ProbeTeleport(new Vec3(7.5, 0.55, 11.5));
        game.ProbeLookAt(new Vec3(2.0, 0.25, 7.5));
        await Frames(4);
        await Capture("22g-low-angle-shoreline");

        var routeBefore = sim.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode);
        Check("pond-route-blocked-before-bridges", routeBefore is null, "No dry route to the lily island");
        game.ProbeSetTool(WebType.Bridge);
        await Frames(3);
        await Capture("22h-reedwater-build-anchors");
        game.ProbeClearBuild();
        await Frames(2);
        Check("bridge-one-built", game.ProbeBuildWeb(ReedwaterLayout.StartPost,
            ReedwaterLayout.IslandAPost, WebType.Bridge), "Reed post to mossy island");
        Check("bridge-two-built", game.ProbeBuildWeb(ReedwaterLayout.IslandAPost,
            ReedwaterLayout.IslandBPost, WebType.Bridge), "Mossy island to lily island");
        var routeAfter = sim.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode);
        Check("pond-route-open-after-bridges", routeAfter is not null && routeAfter.UsesBridge,
            "Two connected bridge decisions");
        Check("r01-complete", sim.RegionalObjectives.CompletedObjectives.Contains("R01"),
            "Across the Water");
        Check("clever-crossing-not-yet-earned", !sim.RegionalObjectives.MasteryAwarded,
            "Building the second bridge is not enough; the player must arrive");
        await Frames(3);
        await Capture("23-bridges-built");
        game.ProbeTeleport(new Vec3(8.8, 1.6, 10.4));
        game.ProbeLookAt(new Vec3(3.4, 0.55, 6.2));
        await Frames(4);
        var islandBScreen = game.ProbeAnchorScreenPosition(ReedwaterLayout.IslandBPost);
        var lilyBScreen = game.ProbeAnchorScreenPosition(ReedwaterLayout.MidB);
        var towardLily = (lilyBScreen - islandBScreen).Normalized();
        var nearestPick = game.ProbePickAnchorAt(islandBScreen + towardLily * 2f);
        Check("anchor-screen-nearest-target",
            nearestPick == ReedwaterLayout.IslandBPost,
            $"pick={nearestPick} islandB={islandBScreen} lilyB={lilyBScreen}");
        var emptyPick = string.Empty;
        var foundEmpty = false;
        foreach (var y in new[] { 40, 120, 680 })
        {
            foreach (var x in new[] { 60, 320, 640, 960, 1220 })
            {
                emptyPick = game.ProbePickAnchorAt(new Vector2(x, y));
                if (emptyPick != "") continue;
                foundEmpty = true;
                break;
            }
            if (foundEmpty) break;
        }
        Check("anchor-screen-empty-space-does-not-select", foundEmpty && emptyPick == "",
            $"pick={emptyPick}");
        await Capture("23b-reedwater-bridge-detail");
        game.ProbeTeleport(new Vec3(7.6, 0.55, 7.6));
        await Frames(2);

        // Real walking traversal. Every run starts on legitimate bank/island
        // ground; the player is never placed on or above a deck.
        var toIslandA = await WalkRoute(game, new Vec3(9.2f, 0.55f, 8.6f),
            ReedwaterToIslandA, 0.7f, 40f);
        Check("player-walks-bank-onto-bridge-one",
            toIslandA.Reached && toIslandA.End.X < 2.6f && !toIslandA.EnteredWater,
            $"reached={toIslandA.Reached} end={toIslandA.End} seconds={toIslandA.Seconds:0.0}");
        Check("player-walks-bridge-one-to-island-a", toIslandA.Reached &&
            Mathf.Abs(toIslandA.End.Z - 6.4f) < 0.9f,
            $"end={toIslandA.End}");
        await Capture("24-reedwater-crossing");

        var toIslandB = await WalkRoute(game, toIslandA.End,
            ReedwaterIslandAToIslandB, 0.7f, 40f);
        Check("player-walks-island-a-onto-bridge-two",
            toIslandB.Reached && toIslandB.End.X < -2.2f && !toIslandB.EnteredWater,
            $"reached={toIslandB.Reached} end={toIslandB.End}");
        Check("clever-crossing-not-awarded-on-bridge",
            !sim.RegionalObjectives.MasteryAwarded,
            $"bridges={sim.ReedwaterBridgeCount} position={toIslandB.End}");

        // Step off the bridge deck onto the lily island itself. The badge is
        // only allowed once the spider is grounded inside the island footprint
        // at its walkable surface height, not while crossing above it.
        var ontoIsland = await WalkRoute(game, toIslandB.End,
            new[] { new Vec3(-4.0, 0.15, 0.8) }, 0.4f, 12f);
        var islandGround = game.ProbeGroundColliderPath();
        Check("player-steps-onto-lily-island",
            ontoIsland.Reached && !ontoIsland.EnteredWater &&
            ReedwaterLayout.IsOnFirstDestinationIsland(ontoIsland.End) &&
            islandGround.Contains("IslandB/MossIsland/Collision", StringComparison.Ordinal),
            $"reached={ontoIsland.Reached} end={ontoIsland.End} ground={islandGround}");
        Check("clever-crossing-earned-by-arrival",
            sim.RegionalObjectives.MasteryAwarded,
            $"bridges={sim.ReedwaterBridgeCount} position={ontoIsland.End}");
        await Capture("24a-clever-crossing-arrival");

        var backToIslandA = await WalkRoute(game, ontoIsland.End,
            Reverse(ReedwaterIslandAToIslandB), 0.7f, 40f);
        Check("player-walks-bridge-two-back-to-island-a", backToIslandA.Reached,
            $"end={backToIslandA.End} seconds={backToIslandA.Seconds:0.0}");
        var backToBank = await WalkRoute(game, backToIslandA.End,
            Reverse(ReedwaterToIslandA), 0.7f, 40f);
        Check("player-walks-bridge-one-back-to-bank",
            backToBank.Reached && backToBank.End.X > 8.2f && !backToBank.EnteredWater,
            $"end={backToBank.End} seconds={backToBank.Seconds:0.0}");

        // Stop and restart on the ramp/anchor joint, then turn around mid-deck.
        var stopped = await WalkRoute(game, new Vec3(9.2f, 0.55f, 8.6f),
            ReedwaterToIslandA, 0.7f, 40f, stopAtWaypoint: 2, stopFrames: 60);
        Check("player-stops-and-resumes-at-deck-joint", stopped.Reached,
            $"end={stopped.End}");
        var midDeck = await WalkRoute(game, new Vec3(9.2f, 0.55f, 8.6f),
            new[]
            {
                new Vec3(7.6, 0, 7.6), new Vec3(6.6, 0.55, 7.0),
                new Vec3(4.0, 0.25, 6.3)
            }, 0.55f, 30f);
        var midX = game.PlayerPosition.X;
        game.ProbeLookAt(new Vec3(9.2, 0.6, 8.6));
        await Frames(2);
        Input.ActionPress("move_forward");
        await PhysicsFrames(14);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(6);
        var turnedBackX = game.PlayerPosition.X;
        game.ProbeLookAt(new Vec3(1.4, 0.6, 5.6));
        await Frames(2);
        Input.ActionPress("move_forward");
        await PhysicsFrames(16);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(6);
        Check("player-turns-around-on-bridge",
            midDeck.Reached && turnedBackX > midX + 0.25f && game.PlayerPosition.X < turnedBackX - 0.25f,
            $"mid={midX:0.00} back={turnedBackX:0.00} forward={game.PlayerPosition.X:0.00}");

        // Capture the straightforward reed flyway and catch three insects.
        Check("reed-capture-web-built", game.ProbeBuildWeb(ReedwaterLayout.EasyA,
            ReedwaterLayout.EasyB, WebType.Capture), "Capture web across the reed stems");
        var caught = 0;
        var playerSpeciesNamed = false;
        for (var i = 0; i < 30 && caught < 3; i++)
        {
            game.ProbeAdvance(5);
            await Frames(2);
            var trapped = sim.RegionInsects.FirstOrDefault(insect => insect.CanBeCollected);
            if (trapped is null) continue;
            game.ProbeTeleport(trapped.Position + new Vec3(1.4, 1.2, 1.4));
            game.ProbeLookAt(trapped.Position);
            await Frames(3);
            if (caught == 0) await Capture("25-reedwater-capture-trapped");
            game.ProbeTeleport(trapped.Position);
            await Frames(2);
            game.ProbeInteract();
            await Frames(2);
            playerSpeciesNamed |= game.ProbeToastText.Contains("You collected a");
            caught++;
            await Frames(2);
        }
        Check("player-collection-names-species", playerSpeciesNamed, game.ProbeToastText);
        Check("reedwater-insects-collected", caught >= 3, $"caught={caught}");
        Check("r02-complete", sim.RegionalObjectives.CompletedObjectives.Contains("R02"),
            "The Reed Harvest");
        await Capture("25-reedwater-capture");

        // The third bridge plus far-bank capture gives a helper a meaningful
        // route; the worker must use bridge edges, not teleport.
        Check("bridge-three-built", game.ProbeBuildWeb(ReedwaterLayout.IslandBPost,
            ReedwaterLayout.FarPost, WebType.Bridge), "Root island to far bank");
        var toFarBank = await WalkRoute(game, new Vec3(-2.7f, 0.15f, 2.7f),
            ReedwaterIslandBToFarBank, 0.7f, 40f);
        Check("player-walks-island-b-onto-bridge-three",
            toFarBank.Reached && toFarBank.End.X < -8.4f && !toFarBank.EnteredWater,
            $"reached={toFarBank.Reached} end={toFarBank.End}");
        var farBankBack = await WalkRoute(game, toFarBank.End,
            Reverse(ReedwaterIslandBToFarBank), 0.7f, 40f);
        Check("player-walks-bridge-three-back-to-island-b",
            farBankBack.Reached && farBankBack.End.X > -3.5f,
            $"end={farBankBack.End} seconds={farBankBack.Seconds:0.0}");
        await Capture("24b-reedwater-far-bank-walk");

        var firstReedwaterBridge = sim.RegionWebs.FirstOrDefault(web => web.Type == WebType.Bridge);
        if (firstReedwaterBridge is not null)
            CheckBridgeVisual(game, firstReedwaterBridge.Id, "reedwater-bridge");
        Check("far-capture-web-built", game.ProbeBuildWeb(ReedwaterLayout.FarA,
            ReedwaterLayout.FarB, WebType.Capture), "Far-bank capture web");
        var workerCrossed = false;
        var regionComplete = false;
        var workerAttributed = false;
        var farBankFeedback = false;
        var dragonflyNamed = false;
        var workerGlow = false;
        var workerGlowCaptured = false;
        for (var i = 0; i < 100 && !regionComplete; i++)
        {
            game.ProbeAdvance(2);
            await Frames(2);
            workerCrossed |= HasRegionEvent("WorkerUsedBridge", RegionIds.ReedwaterBank);
            var toast = game.ProbeToastText;
            workerAttributed |= toast.Contains("collected a") && !toast.StartsWith("You collected");
            farBankFeedback |= toast.Contains("from the far bank");
            dragonflyNamed |= toast.Contains("dragonfly");
            // The halo is visible while collecting/repairing and for three
            // seconds after a collection. Sampling must accept that post-work
            // flash too, otherwise a valid glow can fall between ticks.
            workerGlow |= sim.RegionWorkers.Any(worker =>
                game.ProbeWorkerActivityGlowVisible(worker.Id));
            if (workerGlow && !workerGlowCaptured)
            {
                workerGlowCaptured = true;
                await Capture("26a-worker-collecting");
            }
            regionComplete = sim.RegionalObjectives.CompletedObjectives.Contains("R03");
        }
        Check("worker-used-reedwater-bridge", workerCrossed, "Worker crossed a real bridge edge");
        Check("worker-attributed-collection", workerAttributed, "Worker collection names the helper");
        Check("far-bank-delivery-feedback", farBankFeedback, "Far-bank delivery is announced");
        Check("dragonfly-named-correctly", dragonflyNamed, "Collection toast names the dragonfly");
        Check("worker-activity-glow", workerGlow, "A working helper is visibly marked");
        Check("r03-complete", sim.RegionalObjectives.CompletedObjectives.Contains("R03"),
            "The Far Bank");
        Check("reedwater-completion-feedback", game.RegionCompleteModalOpen,
            "Completion acknowledgement without ending free play");
        await Capture("27-reedwater-complete");
        if (game.RegionCompleteModalOpen)
        {
            game.ProbeCloseModal();
            await Frames(2);
        }
        Check("far-bank-trail-visible", game.ProbeFarBankTrailVisible,
            "Optional reed trail appears after R03");
        game.ProbeSetSafePosition(new Vec3(-9.6, 0.55, -2.8));
        game.ProbeTeleport(new Vec3(-9.6, 0.55, -2.8));
        await Frames(3);
        game.ProbeInteract();
        await Frames(6);
        var outpost = new Vec3(10.2, 0.55, 6.8);
        Check("far-bank-return-shortcut",
            Vec3.Distance(new Vec3(game.PlayerPosition.X, game.PlayerPosition.Y, game.PlayerPosition.Z),
                outpost) < 2.5f,
            $"player={game.PlayerPosition}");
        var farWorker = sim.RegionWorkers.FirstOrDefault();
        if (farWorker is not null)
        {
            game.ProbeSetSafePosition(new Vec3(-8.6, 0.6, -3.6));
            game.ProbeTeleport(new Vec3(-8.6, 0.6, -3.6));
            game.ProbeLookAt(farWorker.Position);
            await Frames(4);
        }
        await Capture("26-reedwater-worker");

        // Save and reload inside the second region.
        Check("reedwater-save", game.ProbeSave(), "Save while standing in Reedwater Bank");
        var loaded = _main.Saves.Load();
        Check("reedwater-save-loads", loaded.Success && loaded.Data is not null, loaded.Message);
        var regionWebs = sim.Webs.Count(web => web.Region == RegionIds.ReedwaterBank);
        var completed = sim.RegionalObjectives.CompletedObjectives.Count;
        if (loaded.Data is not null)
        {
            Check("save-records-active-region", loaded.Data.ActiveRegionId == RegionIds.ReedwaterBank,
                loaded.Data.ActiveRegionId);
            Check("save-records-supply-claim",
                loaded.Data.SchemaVersion == SaveGameData.CurrentSchemaVersion &&
                loaded.Data.Counters.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank),
                $"schema={loaded.Data.SchemaVersion} claimed={string.Join(",", loaded.Data.Counters.ClaimedSupplyCaches)}");
            Check("save-records-reedwater-webs",
                loaded.Data.Webs.Count(web => web.Region == RegionIds.ReedwaterBank) == regionWebs,
                $"webs={regionWebs}");
            Check("save-records-reedwater-objectives",
                loaded.Data.RegionalObjectives.Completed.Count == completed,
                $"completed={completed}");

            var reloadedRegion = _main.StartGame(loaded.Data);
            await Frames(10);
            Check("reload-in-reedwater", reloadedRegion.ActiveRegionId == RegionIds.ReedwaterBank,
                $"region={reloadedRegion.ActiveRegionId}");
            Check("reload-reedwater-construction",
                reloadedRegion.Simulation.Webs.Count(web => web.Region == RegionIds.ReedwaterBank) == regionWebs,
                $"webs={regionWebs}");
            Check("reload-reedwater-objectives",
                reloadedRegion.Simulation.RegionalObjectives.CompletedObjectives.Count == completed,
                $"completed={completed}");
            var reloadWalk = await WalkRoute(reloadedRegion, new Vec3(9.2f, 0.55f, 8.6f),
                ReedwaterToIslandA, 0.7f, 40f);
            Check("reload-reedwater-bridge-walk", reloadWalk.Reached && !reloadWalk.EnteredWater,
                $"end={reloadWalk.End} seconds={reloadWalk.Seconds:0.0}");
            var reloadedBridge = reloadedRegion.Simulation.RegionWebs.FirstOrDefault(web => web.Type == WebType.Bridge);
            if (reloadedBridge is not null)
                CheckBridgeVisual(reloadedRegion, reloadedBridge.Id, "reload-reedwater-bridge");
            await Capture("28-reedwater-reloaded");

            // Return to the garden, verify its own construction is untouched,
            // then re-enter Reedwater and verify persistence.
            var back = reloadedRegion.ProbeTravel(RegionIds.GardenClearing);
            await Frames(8);
            Check("return-to-garden", back && reloadedRegion.ActiveRegionId == RegionIds.GardenClearing,
                $"region={reloadedRegion.ActiveRegionId}");
            Check("garden-state-preserved",
                reloadedRegion.Simulation.Webs.Count(web => web.Region == RegionIds.GardenClearing) ==
                gardenWebsBefore,
                $"gardenWebs={gardenWebsBefore}");
            await Capture("29-returned-garden");

            var returned = reloadedRegion.ProbeTravel(RegionIds.ReedwaterBank);
            await Frames(8);
            Check("reenter-reedwater", returned &&
                reloadedRegion.ActiveRegionId == RegionIds.ReedwaterBank,
                $"region={reloadedRegion.ActiveRegionId}");
            Check("reenter-reedwater-objectives",
                reloadedRegion.Simulation.RegionalObjectives.CompletedObjectives.Count == completed,
                $"completed={completed}");
            Check("reenter-supply-not-regranted",
                reloadedRegion.Simulation.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank),
                $"claimed={string.Join(",", reloadedRegion.Simulation.ClaimedSupplyCaches)} food={reloadedRegion.Simulation.Ledger.Food} silk={reloadedRegion.Simulation.Ledger.Silk}");

            // Remove a critical bridge and prove the worker route becomes
            // invalid; rebuilding it restores the same navigation.
            var critical = reloadedRegion.Simulation.RegionWebs.FirstOrDefault(web =>
                web.Type == WebType.Bridge &&
                web.Connects(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost));
            Check("critical-bridge-present", critical is not null, "Middle bridge exists");
            if (critical is not null)
            {
                Check("critical-bridge-dismantles",
                    reloadedRegion.Simulation.TryDismantleWeb(critical.Id, out _, out _),
                    "Dismantled for navigation regression");
                reloadedRegion.ProbeClearBuild();
                Check("critical-bridge-breaks-route",
                    reloadedRegion.Simulation.Graph.FindRoute(ReedwaterLayout.NurseryNode,
                        ReedwaterLayout.FirstDestinationNode) is null,
                    "Worker route invalid without the middle bridge");
                Check("critical-bridge-rebuilt", reloadedRegion.ProbeBuildWeb(
                    ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost, WebType.Bridge),
                    "Rebuilt the middle bridge");
                Check("critical-bridge-restores-route",
                    reloadedRegion.Simulation.Graph.FindRoute(ReedwaterLayout.NurseryNode,
                        ReedwaterLayout.FirstDestinationNode) is not null,
                    "Navigation restored");
            }
        }
    }

    private bool HasEvent(string type) => _main.Game?.EventLog.Any(e => e.Type == type) ?? false;

    private bool HasRegionEvent(string type, string region) =>
        _main.Game?.EventLog.Any(e => e.Type == type && e.Region == region) ?? false;

    private void Check(string name, bool success, string detail = "")
    {
        if (success) _passes++; else _failures++;
        _checks.Add(new { name, pass = success, detail });
        GD.Print($"[PROBE] {(success ? "PASS" : "FAIL")} {name}" + (string.IsNullOrWhiteSpace(detail) ? "" : $" — {detail}"));
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task PhysicsFrames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>
    /// Walk a route with ordinary forward input, steering toward one waypoint at
    /// a time. The start point must be legitimate walkable ground; the helper
    /// never places the player on a bridge deck.
    /// </summary>
    private async Task<(bool Reached, float Seconds, Vec3 End, bool EnteredWater)> WalkRoute(
        GardenClearing game, Vec3 start, IReadOnlyList<Vec3> route, float tolerance,
        float seconds, int stopAtWaypoint = -1, int stopFrames = 0)
    {
        game.ProbeSetSafePosition(start);
        game.ProbeTeleport(start);
        await Frames(3);
        var began = Time.GetTicksMsec();
        var deadline = began + (ulong)(seconds * 1000);
        var index = 0;
        var water = false;
        while (index < route.Count && Time.GetTicksMsec() < deadline)
        {
            var target = GardenClearing.ToVector3(route[index]);
            var position = game.PlayerPosition;
            var dx = target.X - position.X;
            var dz = target.Z - position.Z;
            if (Mathf.Sqrt(dx * dx + dz * dz) <= tolerance)
            {
                if (index == stopAtWaypoint && stopFrames > 0)
                {
                    Input.ActionRelease("move_forward");
                    await PhysicsFrames(stopFrames);
                }
                index++;
                continue;
            }
            if (game.ProbeWaterHazardAt(new Vec3(position.X, position.Y, position.Z))) water = true;
            game.ProbeLookAt(route[index]);
            Input.ActionPress("move_forward");
            await PhysicsFrames(3);
        }
        Input.ActionRelease("move_forward");
        await PhysicsFrames(12);
        var elapsed = (Time.GetTicksMsec() - began) / 1000f;
        var end = game.PlayerPosition;
        return (index >= route.Count && !water, elapsed, new Vec3(end.X, end.Y, end.Z), water);
    }

    private static readonly Vec3[] ReedwaterToIslandA =
    {
        new(9.2, 0.55, 8.6), new(7.6, 0.0, 7.6), new(6.6, 0.55, 7.0),
        new(5.0, 0.3, 6.6), new(3.4, 0.22, 6.1), new(2.0, 0.4, 5.7),
        new(1.4, 0.55, 5.6), new(2.2, 0.15, 6.4)
    };

    private static readonly Vec3[] ReedwaterIslandAToIslandB =
    {
        new(2.2, 0.15, 6.4), new(1.4, 0.55, 5.6), new(0.0, 0.3, 4.4),
        new(-1.6, 0.25, 3.2), new(-3.0, 0.55, 1.8), new(-2.7, 0.15, 2.7)
    };

    private static readonly Vec3[] ReedwaterIslandBToFarBank =
    {
        new(-2.7, 0.15, 2.7), new(-3.0, 0.55, 1.8), new(-4.4, 0.3, 0.9),
        new(-5.8, 0.25, 0.0), new(-7.4, 0.55, -1.0), new(-8.8, 0.0, -1.9),
        new(-9.4, 0.0, -2.4)
    };

    private static Vec3[] Reverse(Vec3[] route)
    {
        var copy = (Vec3[])route.Clone();
        Array.Reverse(copy);
        return copy;
    }

    private async Task MouseMove(Vector2 position)
    {
        var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position };
        Input.ParseInputEvent(motion);
        await Frames(2);
    }

    private async Task Click(Vector2 position)
    {
        var down = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
            GlobalPosition = position
        };
        Input.ParseInputEvent(down);
        await Frames(2);
        var up = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = position,
            GlobalPosition = position
        };
        Input.ParseInputEvent(up);
        await Frames(2);
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(3);
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.GetWidth() <= 0) return;
            var directory = Path.Combine(GameData.ArtifactsPath, "screenshots");
            Directory.CreateDirectory(directory);
            image.SavePng(Path.Combine(directory, $"{name}.png"));
            GD.Print($"[PROBE] SCREENSHOT {name}.png");
        }
        catch (Exception ex)
        {
            GD.Print($"[PROBE] screenshot {name} skipped: {ex.Message}");
        }
    }

    private void WriteReport()
    {
        var directory = GameData.ArtifactsPath;
        Directory.CreateDirectory(directory);
        var report = new
        {
            generatedUtc = DateTime.UtcNow,
            engine = Engine.GetVersionInfo().ToString(),
            renderer = DisplayServer.GetName(),
            passes = _passes,
            failures = _failures,
            checks = _checks
        };
        var path = Path.Combine(directory, "runtime_probe.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[PROBE] SUMMARY passes={_passes} failures={_failures} report={path}");
    }
}
