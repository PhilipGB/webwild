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
            ApplyProbeSettings();
            await Execute();
        }
        catch (Exception ex)
        {
            Check("probe-exception", false, ex.ToString());
        }
        finally
        {
            // Never leave accessibility or UI-scale changes behind, even when
            // a check fails mid-run. The next launch starts from the defaults.
            try
            {
                ApplyProbeSettings();
                await Frames(2);
            }
            catch { }
            WriteReport();
            await Frames(3);
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    /// <summary>
    /// Probes must be reproducible on any machine. Settings persist in user://
    /// and an interrupted run can leave reduced motion or a larger UI scale
    /// behind, which would change animation and layout checks on the next run.
    /// </summary>
    private void ApplyProbeSettings()
    {
        var defaults = new SettingsData();
        var settings = _main.Settings;
        settings.MasterVolume = defaults.MasterVolume;
        settings.MusicVolume = defaults.MusicVolume;
        settings.SfxVolume = defaults.SfxVolume;
        settings.UiScalePercent = defaults.UiScalePercent;
        settings.ReducedMotion = defaults.ReducedMotion;
        settings.LowThreat = defaults.LowThreat;
        settings.Fullscreen = defaults.Fullscreen;
        settings.CameraSensitivity = defaults.CameraSensitivity;
        settings.TutorialPrompts = defaults.TutorialPrompts;
        try { GameData.CreateSettingsService().Save(settings); } catch { }
        GameData.ApplyUiScale(settings);
        GameData.ApplyFullscreen(settings);
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
        Check("p2-bramble-gate-closed", game.ProbeBrambleGateBlocking,
            "The Reedwater path stays physically closed until the garden story is complete");
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
            // The confirm panel re-enters layout when the toolbar hides; the
            // larger UI scales relayout the whole bottom bar, so give the
            // container time to settle before measuring it.
            await Frames(8);
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
            await Frames(8);
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
            await Frames(8);
            CheckBuildConfirmLane(toastGame150, "150");
            toastGame150.ProbeHideBuildConfirmation();
        }
        await Capture("17-ui-scale-150");

        // Gameplay evidence after this point uses the default presentation.
        settings.UiScalePercent = 100;
        GameData.ApplyUiScale(settings);
        await Frames(4);

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

        // ---- P1: never-ending world acceptance ----
        // Every exit below is walked with real input. Teleports appear only as
        // documented instrumentation where walking cannot reasonably reach in
        // one probe step (long-range unload/reload, far save/reload).
        var p1Game = _main.Game;
        Check("p1-streamer-present", p1Game?.ChunkStream is not null, "ChunkStreamer wired");
        if (p1Game is not null && p1Game.ChunkStream is not null)
        {
            var chunks = p1Game.ChunkStream;
            // The restored session usually already stands in the garden; travel
            // only when a Reedwater session is still the active one.
            var gardenReturn = p1Game.ActiveRegionId == RegionIds.GardenClearing ||
                p1Game.ProbeTravel(RegionIds.GardenClearing);
            await Frames(8);
            Check("p1-return-to-garden", gardenReturn && p1Game.ActiveRegionId == RegionIds.GardenClearing,
                $"region={p1Game.ActiveRegionId}");
            Check("p1-authored-chunks-never-streamed",
                WorldGenerator.AuthoredFootprint.All(coordinate => !chunks.Nodes.ContainsKey(coordinate)),
                $"loaded={chunks.LoadedCount}");

            // North gate: the corridor between hedge edge and ravine cliff, then
            // the apron, then the chunk boundary at z = -24.
            var north = await WalkRoute(p1Game, new Vec3(-5f, 0.45f, 5f),
                new Vec3[] { new(-5, 0, -11), new(-5, 0, -18), new(-5, 0, -28) }, 0.7f, 60f);
            Check("p1-walks-north-gate-across-boundary",
                north.Reached && !north.EnteredWater && north.End.Y > -4f,
                $"reached={north.Reached} end={north.End} water={north.EnteredWater}");

            // The chunk just across the boundary becomes the determinism watch
            // target: unload it far away, return, and compare content hashes.
            var watched = new ChunkCoordinate(-1, -2);
            var watchedFirst = chunks.Nodes.TryGetValue(watched, out var firstNode) ? firstNode : null;
            var hashBefore = watchedFirst?.GetMeta("content_hash").AsString() ?? "";
            var idBefore = watchedFirst?.GetInstanceId() ?? 0UL;
            Check("p1-boundary-chunk-built", watchedFirst is not null && hashBefore.Length > 0,
                $"hash={hashBefore}");

            // East, south and west gates.
            var east = await WalkRoute(p1Game, new Vec3(12f, 0.45f, 8.5f),
                new Vec3[] { new(17.5, 0, 8.5), new(24, 0, 8.5), new(30, 0, 8.5) }, 0.7f, 60f);
            Check("p1-walks-east-gate-across-boundary",
                east.Reached && !east.EnteredWater && east.End.X > 28f && east.End.Y > -4f,
                $"reached={east.Reached} end={east.End}");

            var south = await WalkRoute(p1Game, new Vec3(4.7f, 0.45f, -3f),
                new Vec3[] { new(4.7, 0, 11), new(4.7, 0, 18), new(4.7, 0, 28) }, 0.7f, 60f);
            Check("p1-walks-south-gate-across-boundary",
                south.Reached && !south.EnteredWater && south.End.Z > 26f && south.End.Y > -4f,
                $"reached={south.Reached} end={south.End}");

            var west = await WalkRoute(p1Game, new Vec3(-12f, 0.45f, 8.5f),
                new Vec3[] { new(-17.5, 0, 8.5), new(-24, 0, 8.5), new(-30, 0, 8.5) }, 0.7f, 60f);
            Check("p1-walks-west-gate-across-boundary",
                west.Reached && !west.EnteredWater && west.End.X < -28f && west.End.Y > -4f,
                $"reached={west.Reached} end={west.End}");

            // Diagonal walk: crosses the second boundary line and loads the
            // corner chunk beyond it.
            var diagonal = await WalkRoute(p1Game, new Vec3(30f, 0.45f, 8.5f),
                new Vec3[] { new(30, 0, 20), new(28, 0, 28) }, 0.7f, 60f);
            Check("p1-walks-diagonal-corner",
                diagonal.Reached && !diagonal.EnteredWater && diagonal.End.Y > -4f,
                $"reached={diagonal.Reached} end={diagonal.End}");
            Check("p1-corner-chunk-loaded", chunks.IsChunkLoaded(new ChunkCoordinate(1, 1)),
                $"loaded={chunks.LoadedCount}");
            await Capture("30-procedural-meadow");

            // Instrumentation: one teleport crosses more chunks than any walk
            // can; the unload and ring rebuild it triggers are the generator's
            // own work and are verified by content hash below.
            p1Game.ProbeSetSafePosition(new Vec3(120f, 2f, -96f));
            p1Game.ProbeTeleport(new Vec3(120f, 2f, -96f));
            await Frames(45);
            Check("p1-far-chunk-unloaded", !chunks.Nodes.ContainsKey(watched),
                $"loaded={chunks.LoadedCount} centre={chunks.Centre.Id}");
            Check("p1-load-ring-complete",
                RingComplete(chunks, ChunkCoordinate.OfWorldPoint(120, -96)),
                $"loaded={chunks.LoadedCount}");

            Check("p1-save-while-far", p1Game.ProbeSave(), "Save beyond the old hedge bounds");
            var farLoaded = _main.Saves.Load();
            Check("p1-far-save-validates", farLoaded.Success && farLoaded.Data is not null, farLoaded.Message);
            Check("p1-far-save-keeps-position", farLoaded.Data is not null &&
                Math.Abs(farLoaded.Data.Player.X - 120.0) < 1.0 &&
                Math.Abs(farLoaded.Data.Player.Z + 96.0) < 1.0,
                $"player={farLoaded.Data?.Player.X:0.0},{farLoaded.Data?.Player.Z:0.0}");

            // Return onto the retired chunk and prove the rebuild is identical.
            p1Game.ProbeSetSafePosition(new Vec3(-5f, 0.45f, -26f));
            p1Game.ProbeTeleport(new Vec3(-5f, 0.45f, -26f));
            await Frames(6);
            var watchedReload = chunks.Nodes.TryGetValue(watched, out var reloadNode) ? reloadNode : null;
            var hashAfter = watchedReload?.GetMeta("content_hash").AsString() ?? "";
            Check("p1-chunk-rebuilt-after-unload",
                watchedReload is not null && watchedReload.GetInstanceId() != idBefore,
                $"fresh-node={watchedReload is not null}");
            Check("p1-chunk-reload-deterministic",
                hashAfter.Length > 0 && hashAfter == hashBefore,
                $"before={hashBefore} after={hashAfter}");
            var regenWalk = await WalkRoute(p1Game, new Vec3(-5f, 0.45f, -26f),
                new Vec3[] { new(-5, 0, -30) }, 0.7f, 30f);
            Check("p1-regenerated-chunk-walkable",
                regenWalk.Reached && !regenWalk.EnteredWater && regenWalk.End.Y > -4f,
                $"reached={regenWalk.Reached} end={regenWalk.End}");

            // Reload from the far save: the spider must arrive standing on
            // ground that the streamer rebuilds synchronously.
            if (farLoaded.Data is not null)
            {
                var farReloaded = _main.StartGame(farLoaded.Data);
                await Frames(10);
                // Probe-space positions are stable logical world coordinates.
                var reloadPos = farReloaded.PlayerPosition;
                Check("p1-reload-far-restores-position",
                    Math.Abs(reloadPos.X - 120f) < 1.5f && Math.Abs(reloadPos.Z + 96f) < 1.5f,
                    $"end={reloadPos}");
                Check("p1-reload-far-grounded", reloadPos.Y > -6f &&
                    farReloaded.ChunkStream is not null &&
                    farReloaded.ChunkStream.IsChunkLoaded(
                        ChunkCoordinate.OfWorldPoint(reloadPos.X, reloadPos.Z)),
                    $"y={reloadPos.Y:0.00}");
                await Capture("31-procedural-far-reload");
            }
        }

        // ---- P2: the bramble gate is a real obstacle before the story ----
        // A fresh garden has not earned the Reedwater path; walking into the
        // gate must stop the spider rather than silently opening the region.
        var gateGame = _main.StartGame(null);
        await Frames(10);
        Check("p2-fresh-gate-closed", gateGame.ProbeBrambleGateBlocking,
            "Fresh garden keeps the Reedwater path closed");
        gateGame.ProbeSetSafePosition(new Vec3(75, 0.55, 6));
        gateGame.ProbeTeleport(new Vec3(75, 0.55, 6));
        gateGame.ProbeLookAt(new Vec3(82, 0.55, 6));
        await Frames(4);
        Input.ActionPress("move_forward");
        await PhysicsFrames(90);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(8);
        Check("p2-fresh-gate-blocks-walking", gateGame.PlayerPosition.X < 76.3f,
            $"x={gateGame.PlayerPosition.X:0.00} region={gateGame.ActiveRegionId}");
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- P3: biomes, terrain movement and origin rebasing ----
        var p3Game = _main.StartGame(null);
        await Frames(10);
        var moss = p3Game.ProbeFindBiome("moss", new Vec3(0, 0, 0), 420);
        var bloom = p3Game.ProbeFindBiome("bloom", new Vec3(0, 0, 0), 420);
        Check("p3-moss-biome-found", p3Game.ProbeBiomeAt(moss) == "moss",
            $"point={moss} biome={p3Game.ProbeBiomeAt(moss)} props={p3Game.ProbeLoadedPropCount}");
        Check("p3-bloom-biome-found", p3Game.ProbeBiomeAt(bloom) == "bloom",
            $"point={bloom} biome={p3Game.ProbeBiomeAt(bloom)}");
        Check("p3-procedural-props", p3Game.ProbeLoadedPropCount > 50,
            $"props={p3Game.ProbeLoadedPropCount}");

        var mossTarget = new Vec3(moss.X + 8, moss.Y, moss.Z);
        var mossWalk = await WalkRoute(p3Game, moss,
            new[] { mossTarget }, 0.9f, 35f);
        Check("p3-walks-moss-terrain",
            mossWalk.Reached && mossWalk.End.Y > -6f,
            $"reached={mossWalk.Reached} end={mossWalk.End}");
        p3Game.ProbeSetCameraZoom(15);
        p3Game.ProbeSetCameraPitch(-12);
        p3Game.ProbeLookAt(new Vec3(moss.X + 30, moss.Y + 2, moss.Z + 6));
        await Frames(5);
        await Capture("33-moss-biome");

        var bloomTarget = new Vec3(bloom.X + 8, bloom.Y, bloom.Z);
        var bloomWalk = await WalkRoute(p3Game, bloom,
            new[] { bloomTarget }, 0.9f, 35f);
        Check("p3-walks-bloom-terrain",
            bloomWalk.Reached && bloomWalk.End.Y > -6f,
            $"reached={bloomWalk.Reached} end={bloomWalk.End}");
        p3Game.ProbeSetCameraZoom(15);
        p3Game.ProbeSetCameraPitch(-12);
        p3Game.ProbeLookAt(new Vec3(bloom.X + 30, bloom.Y + 2, bloom.Z + 6));
        await Frames(5);
        await Capture("34-bloom-biome");

        // Long-range movement: the presentation origin rebases near the player
        // while every logical coordinate, save value and chunk remains stable.
        const double farX = 18_000, farZ = -12_000;
        var farStart = new Vec3(farX, p3Game.ProbeGroundHeightAt(new Vec3(farX, 0, farZ)) + 0.6, farZ);
        p3Game.ProbeTeleport(farStart);
        await Frames(40);
        var engine = p3Game.ProbeEnginePlayerPosition;
        Check("p3-origin-rebases-at-distance",
            Math.Abs(engine.X) < 16_000 && Math.Abs(engine.Z) < 16_000 &&
            p3Game.ProbeOriginOffset.Length() > 0,
            $"engine={engine} offset={p3Game.ProbeOriginOffset}");
        Check("p3-rebase-keeps-logical-position",
            Mathf.Sqrt(Mathf.Pow(p3Game.PlayerPosition.X - farX, 2) +
                       Mathf.Pow(p3Game.PlayerPosition.Z - farZ, 2)) < 2.0,
            $"player={p3Game.PlayerPosition}");
        Check("p3-far-chunk-ring", RingComplete(p3Game.ChunkStream!,
            ChunkCoordinate.OfWorldPoint(farX, farZ)),
            $"loaded={p3Game.ChunkStream?.LoadedCount}");
        p3Game.ProbeSetCameraZoom(15);
        p3Game.ProbeSetCameraPitch(-12);
        p3Game.ProbeLookAt(new Vec3(farX + 28, farStart.Y + 3, farZ + 8));
        await Frames(5);
        await Capture("35-origin-rebase");

        var farTarget = new Vec3(farX + 6, farStart.Y, farZ);
        var farWalk = await WalkRoute(p3Game, farStart, new[] { farTarget }, 1.0f, 40f);
        Check("p3-walks-after-rebase",
            farWalk.Reached && farWalk.End.Y > -6f,
            $"reached={farWalk.Reached} end={farWalk.End}");

        Check("p3-far-save", p3Game.ProbeSave(), "Save 21 km from the garden origin");
        var p3Loaded = _main.Saves.Load();
        Check("p3-far-save-loads", p3Loaded.Success && p3Loaded.Data is not null, p3Loaded.Message);
        if (p3Loaded.Data is not null)
        {
            var p3Reloaded = _main.StartGame(p3Loaded.Data);
            await Frames(20);
            Check("p3-reload-rebases", p3Reloaded.ProbeOriginOffset.Length() > 0,
                $"offset={p3Reloaded.ProbeOriginOffset}");
            Check("p3-reload-keeps-logical-position",
                Mathf.Sqrt(Mathf.Pow(p3Reloaded.PlayerPosition.X - farX, 2) +
                           Mathf.Pow(p3Reloaded.PlayerPosition.Z - farZ, 2)) < 30.0,
                $"player={p3Reloaded.PlayerPosition}");
            Check("p3-reload-chunk-loaded",
                p3Reloaded.ChunkStream is not null &&
                p3Reloaded.ChunkStream.IsChunkLoaded(ChunkCoordinate.OfWorldPoint(farX, farZ)),
                $"loaded={p3Reloaded.ChunkStream?.LoadedCount}");
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- P4: rivers and generated crossings ----
        var p4Game = _main.StartGame(null);
        await Frames(10);
        var crossing = p4Game.ProbeRiverCrossing(0, 0);
        Check("p4-bridge-resident",
            p4Game.ProbeRiverBridgeAt(0, 0) && p4Game.ProbeLoadedBridgeCount >= 1,
            $"bridges={p4Game.ProbeLoadedBridgeCount} crossing={crossing}");
        Check("p4-water-quads-loaded", p4Game.ProbeLoadedWaterQuadCount > 50,
            $"quads={p4Game.ProbeLoadedWaterQuadCount}");
        Check("p4-river-mask",
            p4Game.ProbeRiverMaskAt(new Vec3(crossing.X, crossing.Y, crossing.Z)) &&
            !p4Game.ProbeRiverMaskAt(new Vec3(crossing.X, crossing.Y, crossing.Z - 12)),
            $"center={crossing}");
        Check("p4-props-in-own-chunks", p4Game.ProbePropsInOwnChunks(),
            "Rendered props stay inside their chunk bounds");

        // Stand on the north bank and show the crossing in context.
        var northZ = crossing.Z - 14.0;
        var northY = p4Game.ProbeGroundHeightAt(new Vec3(0, 0, northZ)) + 0.6;
        var northBank = new Vec3(0, northY, northZ);
        p4Game.ProbeSetSafePosition(northBank);
        p4Game.ProbeTeleport(northBank);
        p4Game.ProbeLookAt(new Vec3(0, crossing.Y + 0.5, crossing.Z + 8));
        p4Game.ProbeSetCameraZoom(15);
        p4Game.ProbeSetCameraPitch(-22);
        await Frames(10);
        await Capture("36-river-crossing");

        var bridgeWalk = await WalkRoute(p4Game, northBank, new[]
        {
            new Vec3(0, crossing.Y + 0.6, crossing.Z),
            new Vec3(0, crossing.Y + 0.6, crossing.Z + 13)
        }, 1.1f, 45f);
        Check("p4-crosses-river-on-bridge",
            bridgeWalk.Reached && !bridgeWalk.EnteredWater &&
            bridgeWalk.End.Z > crossing.Z + 9 &&
            bridgeWalk.End.Y > crossing.Y + 0.2,
            $"reached={bridgeWalk.Reached} end={bridgeWalk.End} water={bridgeWalk.EnteredWater}");

        // Show the water ribbon from the far bank, looking along the river.
        var southZ = crossing.Z + 13.0;
        var southY = p4Game.ProbeGroundHeightAt(new Vec3(-10, 0, southZ)) + 0.6;
        p4Game.ProbeTeleport(new Vec3(-10, southY, southZ));
        p4Game.ProbeLookAt(new Vec3(24, crossing.Y + 0.4, crossing.Z - 2));
        p4Game.ProbeSetCameraZoom(15);
        p4Game.ProbeSetCameraPitch(-18);
        await Frames(8);
        await Capture("37-river-water");

        // Wading is refused: walking into the channel recovers to the last
        // safe bank position and explains where crossings are.
        var hazard = p4Game.ProbeRiverPoint(0, 22);
        var hazardApproachZ = hazard.Z - 12.0;
        var hazardApproach = new Vec3(22,
            p4Game.ProbeGroundHeightAt(new Vec3(22, 0, hazardApproachZ)) + 0.6,
            hazardApproachZ);
        p4Game.ProbeClearToastHistory();
        p4Game.ProbeSetSafePosition(hazardApproach);
        p4Game.ProbeTeleport(hazardApproach);
        p4Game.ProbeLookAt(new Vec3(22, hazard.Y + 0.2, hazard.Z));
        await Frames(4);
        Input.ActionPress("move_forward");
        await PhysicsFrames(210);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(10);
        Check("p4-river-recovery",
            !p4Game.ProbeRiverDangerAt(new Vec3(
                p4Game.PlayerPosition.X, p4Game.PlayerPosition.Y, p4Game.PlayerPosition.Z)) &&
            p4Game.PlayerPosition.Z < hazard.Z - 3.0,
            $"player={p4Game.PlayerPosition} river={hazard}");
        Check("p4-river-guidance-toast",
            p4Game.ProbeToastHistory.Any(text =>
                text.Contains("river", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("crossing", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", p4Game.ProbeToastHistory));

        // Save and reload on the far bank: the crossing and water regenerate
        // from the seed while the saved logical position stays stable.
        var farBank = new Vec3(0, southY, southZ);
        p4Game.ProbeSetSafePosition(farBank);
        p4Game.ProbeTeleport(farBank);
        await Frames(6);
        Check("p4-far-bank-save", p4Game.ProbeSave(), "Save beyond the river");
        var p4Loaded = _main.Saves.Load();
        Check("p4-far-bank-loads", p4Loaded.Success && p4Loaded.Data is not null, p4Loaded.Message);
        if (p4Loaded.Data is not null)
        {
            var p4Reloaded = _main.StartGame(p4Loaded.Data);
            await Frames(20);
            Check("p4-reload-bridge-resident", p4Reloaded.ProbeRiverBridgeAt(0, 0),
                $"bridges={p4Reloaded.ProbeLoadedBridgeCount}");
            Check("p4-reload-keeps-river-position",
                Mathf.Sqrt(Mathf.Pow(p4Reloaded.PlayerPosition.X - farBank.X, 2) +
                           Mathf.Pow(p4Reloaded.PlayerPosition.Z - farBank.Z, 2)) < 6.0,
                $"player={p4Reloaded.PlayerPosition} expected={farBank}");
            Check("p4-reload-far-bank-grounded",
                p4Reloaded.PlayerPosition.Y > crossing.Y + 0.2,
                $"y={p4Reloaded.PlayerPosition.Y:0.00} water={crossing.Y:0.00}");
        }

        // A save written before rivers existed can now sit in deep water. The
        // load path must place the spider on the nearest dry bank instead of
        // leaving them to an endless recovery loop.
        var rescueSource = _main.Saves.Load();
        Check("p4-rescue-source-loads",
            rescueSource.Success && rescueSource.Data is not null, rescueSource.Message);
        if (rescueSource.Data is not null)
        {
            var rescue = rescueSource.Data;
            rescue.ActiveRegionId = RegionIds.GardenClearing;
            rescue.Player.X = crossing.X;
            rescue.Player.Y = crossing.Y - 0.5;
            rescue.Player.Z = crossing.Z;
            var gardenEntry = rescue.RegionPlayers.FirstOrDefault(
                entry => entry.Region == RegionIds.GardenClearing);
            if (gardenEntry is null)
            {
                gardenEntry = new RegionPlayerSave { Region = RegionIds.GardenClearing };
                rescue.RegionPlayers.Add(gardenEntry);
            }
            gardenEntry.X = crossing.X;
            gardenEntry.Y = crossing.Y - 0.5;
            gardenEntry.Z = crossing.Z;
            Check("p4-rescue-save-written", _main.Saves.Save(rescue).Success,
                "Save intentionally placed in the river");
            var rescueLoaded = _main.Saves.Load();
            Check("p4-rescue-save-loads",
                rescueLoaded.Success && rescueLoaded.Data is not null, rescueLoaded.Message);
            if (rescueLoaded.Data is not null)
            {
                var rescued = _main.StartGame(rescueLoaded.Data);
                await Frames(15);
                Check("p4-river-save-recovers-to-bank",
                    !rescued.ProbeRiverDangerAt(new Vec3(
                        rescued.PlayerPosition.X, rescued.PlayerPosition.Y,
                        rescued.PlayerPosition.Z)) &&
                    rescued.PlayerPosition.Y > crossing.Y + 0.3,
                    $"player={rescued.PlayerPosition} water={crossing.Y:0.00}");
                Check("p4-river-save-recovery-feedback",
                    rescued.ProbeToastHistory.Any(text => text.Contains("riverbank")),
                    string.Join(" | ", rescued.ProbeToastHistory));
            }
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- P5: exploration map and Return Home ----
        var p5Game = _main.StartGame(null);
        await Frames(10);
        var spawnChunk = new Vec3(0, 0, 8.5);
        Check("p5-spawn-chunks-explored",
            p5Game.ExploredChunkCount >= 9 && p5Game.ProbeExploredContains(spawnChunk),
            $"explored={p5Game.ExploredChunkCount}");

        var mapKeyDown = new InputEventKey
        {
            PhysicalKeycode = Key.M, Keycode = Key.M, Pressed = true
        };
        var mapKeyUp = new InputEventKey
        {
            PhysicalKeycode = Key.M, Keycode = Key.M, Pressed = false
        };
        Input.ParseInputEvent(mapKeyDown);
        await Frames(3);
        Input.ParseInputEvent(mapKeyUp);
        await Frames(4);
        Check("p5-map-opens-with-m", p5Game.ProbeMapOpen, "M opens the wilderness map");
        Check("p5-map-draws-explored-cells",
            p5Game.ProbeMapCells >= 9, $"cells={p5Game.ProbeMapCells}");
        Check("p5-map-button-fits",
            p5Game.ProbeMapButtonRect.Size.X > 0 &&
            p5Game.ProbeMapButtonRect.End.X <= GetViewport().GetVisibleRect().Size.X + 1,
            $"button={p5Game.ProbeMapButtonRect}");
        await Capture("38-wilderness-map");

        // M closes the map again.
        Input.ParseInputEvent(mapKeyDown);
        await Frames(3);
        Input.ParseInputEvent(mapKeyUp);
        await Frames(3);
        Check("p5-map-closes-with-m", !p5Game.ProbeMapOpen, "M toggles the map");

        var exploredBeforeWalk = p5Game.ExploredChunkCount;
        var exploredWalk = await WalkRoute(p5Game, new Vec3(0, 0.55, 30),
            new[] { new Vec3(0, 0.55, 44) }, 1.0f, 25f);
        Check("p5-walking-records-new-chunks",
            exploredWalk.Reached && p5Game.ExploredChunkCount > exploredBeforeWalk,
            $"before={exploredBeforeWalk} after={p5Game.ExploredChunkCount}");

        const double farExploreX = 200, farExploreZ = 200;
        var farExploreY = p5Game.ProbeGroundHeightAt(
            new Vec3(farExploreX, 0, farExploreZ)) + 0.6;
        p5Game.ProbeTeleport(new Vec3(farExploreX, farExploreY, farExploreZ));
        await Frames(12);
        Check("p5-far-exploration-recorded",
            p5Game.ProbeExploredContains(new Vec3(farExploreX, 0, farExploreZ)),
            $"explored={p5Game.ExploredChunkCount}");
        var exploredAtSave = p5Game.ExploredChunkCount;
        Check("p5-save", p5Game.ProbeSave(), "Save with exploration history");
        var p5Loaded = _main.Saves.Load();
        Check("p5-save-loads", p5Loaded.Success && p5Loaded.Data is not null, p5Loaded.Message);
        if (p5Loaded.Data is not null)
        {
            Check("p5-fresh-save-garden-only",
                !p5Loaded.Data.VisitedRegions.Contains(RegionIds.ReedwaterBank),
                string.Join(",", p5Loaded.Data.VisitedRegions));
            Check("p5-save-carries-exploration",
                p5Loaded.Data.ExploredChunks.Count >= exploredAtSave,
                $"saved={p5Loaded.Data.ExploredChunks.Count} live={exploredAtSave}");
            var p5Reloaded = _main.StartGame(p5Loaded.Data);
            await Frames(20);
            Check("p5-reload-restores-exploration",
                p5Reloaded.ProbeExploredContains(new Vec3(farExploreX, 0, farExploreZ)) &&
                p5Reloaded.ProbeExploredContains(spawnChunk) &&
                p5Reloaded.ExploredChunkCount >= exploredAtSave,
                $"explored={p5Reloaded.ExploredChunkCount}");

            p5Reloaded.ProbeOpenMap();
            await Frames(4);
            Check("p5-map-opens-from-button", p5Reloaded.ProbeMapOpen,
                $"cells={p5Reloaded.ProbeMapCells}");
            await Capture("39-wilderness-map-explored");
            var homeRect = p5Reloaded.ProbeMapHomeRect;
            var viewport = GetViewport().GetVisibleRect().Size;
            Check("p5-return-home-available",
                homeRect.Size.X > 0 && homeRect.Position.X >= 0 &&
                homeRect.End.X <= viewport.X + 1 && homeRect.End.Y <= viewport.Y + 1,
                $"home={homeRect} viewport={viewport}");
            p5Reloaded.ProbeReturnHome();
            await Frames(12);
            var home = GardenClearing.LandmarkWorld(RegionIds.GardenClearing,
                WorldLayout.PlayerSpawn);
            Check("p5-return-home-teleports",
                p5Reloaded.ActiveRegionId == RegionIds.GardenClearing &&
                p5Reloaded.PlayerPosition.DistanceTo(home) < 2.0 &&
                !p5Reloaded.ProbeMapOpen,
                $"player={p5Reloaded.PlayerPosition} home={home} region={p5Reloaded.ActiveRegionId}");
            Check("p5-return-home-toast",
                p5Reloaded.ProbeToastHistory.Any(text => text.Contains("returned", StringComparison.OrdinalIgnoreCase)),
                string.Join(" | ", p5Reloaded.ProbeToastHistory));
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- P6: wild food, per-chunk persistence and regeneration ----
        // Probe runs share an application-data directory and the fixed probe
        // seed, so clear old chunk records before testing baseline food.
        foreach (var oldChunk in _main.Saves.WorldStore.ListChangedChunks().ToList())
            _main.Saves.WorldStore.DeleteChunkChanges(oldChunk);

        var p6Game = _main.StartGame(null);
        await Frames(12);
        var p6Source = p6Game.ProbeNearestFood(new Vec3(0, 0, 8.5), 500);
        Check("p6-wild-food-generated",
            p6Source is not null && p6Game.ProbeFoodViewCount > 0,
            $"source={p6Source?.Id} views={p6Game.ProbeFoodViewCount}");
        if (p6Source is not null)
        {
            var p6Id = p6Source.Id;
            var p6Chunk = p6Source.Chunk;
            var p6Amount = p6Source.Amount;
            var p6SourcePos = p6Source.Position;
            var p6Approach = new Vec3(p6SourcePos.X - 1.4,
                p6Game.ProbeGroundHeightAt(new Vec3(p6SourcePos.X - 1.4, 0, p6SourcePos.Z)) + 0.6,
                p6SourcePos.Z);
            p6Game.ProbeTeleport(p6Approach);
            p6Game.ProbeLookAt(new Vec3(p6SourcePos.X, p6SourcePos.Y + 0.55, p6SourcePos.Z));
            p6Game.ProbeSetCameraZoom(9);
            p6Game.ProbeSetCameraPitch(-16);
            await Frames(8);
            Check("p6-forage-prompt",
                p6Game.ProbeFoodPrompt().Contains("Forage", StringComparison.OrdinalIgnoreCase),
                p6Game.ProbeFoodPrompt());
            await Capture("40-wild-food");

            var foodBefore = p6Game.Simulation!.Ledger.Food;
            Check("p6-harvests-wild-food", p6Game.ProbeForageNearest(),
                $"food={foodBefore}");
            await Frames(4);
            Check("p6-food-granted-once",
                p6Game.Simulation.Ledger.Food == foodBefore + p6Amount &&
                !p6Game.ProbeFoodAvailable(p6Id),
                $"before={foodBefore} after={p6Game.Simulation.Ledger.Food} amount={p6Amount}");
            var foodAfterHarvest = p6Game.Simulation.Ledger.Food;
            Check("p6-second-harvest-refused", !p6Game.ProbeForageNearest() &&
                p6Game.Simulation.Ledger.Food == foodAfterHarvest,
                $"food={p6Game.Simulation.Ledger.Food}");
            Check("p6-depleted-view-hidden", !p6Game.ProbeFoodViewVisible(p6Id) &&
                p6Game.ProbeFoodViewPrompt(p6Id).Contains("regrow", StringComparison.OrdinalIgnoreCase),
                p6Game.ProbeFoodViewPrompt(p6Id));
            await Capture("41-wild-food-depleted");

            Check("p6-save", p6Game.ProbeSave(), "Save with a harvested food source");
            var p6Record = _main.Saves.WorldStore.LoadChunkChanges(p6Chunk, out var p6RecordError);
            Check("p6-chunk-record-written",
                p6Record is not null && p6Record.WorldSeed == p6Game.Simulation.Tuning.Seed &&
                p6Record.FoodSources.Any(food => food.Id == p6Id),
                $"record={(p6Record is null ? "missing" : p6Record.FoodSources.Count.ToString())} error={p6RecordError}");
            var p6Loaded = _main.Saves.Load();
            Check("p6-save-loads", p6Loaded.Success && p6Loaded.Data is not null, p6Loaded.Message);
            if (p6Loaded.Data is not null)
            {
                var p6Reloaded = _main.StartGame(p6Loaded.Data);
                await Frames(20);
                Check("p6-reload-restores-depletion", !p6Reloaded.ProbeFoodAvailable(p6Id),
                    $"remaining={p6Reloaded.ProbeFoodSecondsRemaining(p6Id):0.0}s");
                Check("p6-reload-keeps-food-views",
                    p6Reloaded.ProbeFoodViewCount > 0 &&
                    !p6Reloaded.ProbeFoodViewVisible(p6Id),
                    $"views={p6Reloaded.ProbeFoodViewCount}");

                // Regeneration follows the simulation clock while the chunk is
                // far outside the loaded ring.
                p6Reloaded.ProbeTeleport(new Vec3(3200, 0.6, 3200));
                await Frames(30);
                p6Reloaded.ProbeAdvance(p6Reloaded.Simulation!.Tuning.Food.RegenerationSeconds + 2);
                await Frames(4);
                p6Reloaded.ProbeTeleport(p6Approach);
                await Frames(25);
                Check("p6-regenerates-offscreen",
                    p6Reloaded.ProbeFoodAvailable(p6Id) &&
                    p6Reloaded.ProbeFoodViewVisible(p6Id),
                    $"available={p6Reloaded.ProbeFoodAvailable(p6Id)} views={p6Reloaded.ProbeFoodViewCount}");
                Check("p6-regrown-prompt",
                    p6Reloaded.ProbeFoodPrompt().Contains("Forage", StringComparison.OrdinalIgnoreCase),
                    p6Reloaded.ProbeFoodPrompt());
                await Capture("42-wild-food-regrown");
                Check("p6-regrow-save", p6Reloaded.ProbeSave(), "Save after regrowth");
                var clearedRecord = _main.Saves.WorldStore.LoadChunkChanges(p6Chunk, out _);
                Check("p6-regrown-record-cleared",
                    clearedRecord is null ||
                    !clearedRecord.FoodSources.Any(food => food.Id == p6Id),
                    $"food={clearedRecord?.FoodSources.Count ?? 0}");
            }
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- H1/P7: independent helpers, individual summon and release ----
        var p7Game = _main.StartGame(null);
        await Frames(10);
        // Build the bridge and trap before hatching so the helper starts with a
        // real owned job rather than finishing a patrol first.
        p7Game.ProbeBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge);
        p7Game.ProbeBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture);
        p7Game.ProbeAdvance(12);
        Check("p7-helper-hatched", p7Game.ProbeHatch(), "Hatch a helper for the journey");
        p7Game.ProbeAdvance(5);
        await Frames(6);
        var p7Worker = p7Game.Simulation!.Workers.FirstOrDefault();
        Check("p7-worker-ready", p7Worker is not null, $"workers={p7Game.Simulation.Workers.Count}");
        if (p7Worker is not null)
        {
            var workerId = p7Worker.Id;
            var gardenNurseryWorld = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
                WorldLayout.NurseryPosition).Absolute;
            var reedwaterNurseryWorld = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
                ReedwaterLayout.NurseryPosition).Absolute;
            Check("p7-worker-starts-at-garden",
                p7Game.ProbeWorkerRegion(workerId) == RegionIds.GardenClearing &&
                Vec3.Distance(p7Game.ProbeWorkerWorldPosition(workerId), gardenNurseryWorld) < 8,
                $"region={p7Game.ProbeWorkerRegion(workerId)} " +
                $"worker={p7Game.ProbeWorkerWorldPosition(workerId)}");

            // Walking to Reedwater must not reassign the helper's own job.
            p7Game.ProbeAdvance(2);
            var taskBefore = p7Game.ProbeWorkerActivity(workerId);
            var targetBefore = p7Game.ProbeWorkerTargetInsect(workerId);
            var workerWorldBefore = p7Game.ProbeWorkerWorldPosition(workerId);
            Check("p7-helper-has-task", !string.IsNullOrEmpty(taskBefore) &&
                taskBefore != "Resting", $"task={taskBefore} target={targetBefore}");

            Check("p7-enter-reedwater", p7Game.ProbeEnterLandmark(RegionIds.ReedwaterBank),
                "Move the player/story frame to Reedwater");
            await Frames(4);
            Check("p7-landmark-does-not-redirect-helper",
                p7Game.ProbeWorkerRegion(workerId) == RegionIds.GardenClearing &&
                !p7Game.ProbeWorkerTravelling(workerId) &&
                p7Game.ProbeWorkerActivity(workerId) == taskBefore &&
                p7Game.ProbeWorkerTargetInsect(workerId) == targetBefore,
                $"region={p7Game.ProbeWorkerRegion(workerId)} task={p7Game.ProbeWorkerActivity(workerId)} " +
                $"travelling={p7Game.ProbeWorkerTravelling(workerId)}");
            p7Game.ProbeAdvance(8);
            Check("p7-helper-keeps-working",
                p7Game.ProbeWorkerRegion(workerId) == RegionIds.GardenClearing &&
                !p7Game.ProbeWorkerTravelling(workerId) &&
                (p7Game.ProbeWorkerCollectCount(workerId) >= 1 ||
                 p7Game.ProbeWorkerActivity(workerId) is "Collecting" or "Patrolling"),
                $"task={p7Game.ProbeWorkerActivity(workerId)} " +
                $"collect={p7Game.ProbeWorkerCollectCount(workerId)}");

            // Individual summon: choose this one helper in the colony panel.
            var beforeSummon = p7Game.ProbeWorkerWorldPosition(workerId);
            Check("p7-summon-single-helper",
                p7Game.ProbeSummonWorker(workerId),
                "The named helper is called");
            await Frames(4);
            Check("p7-summon-does-not-teleport",
                p7Game.ProbeWorkerTravelling(workerId) &&
                p7Game.ProbeWorkerSummoned(workerId) &&
                Vec3.Distance(p7Game.ProbeWorkerWorldPosition(workerId), beforeSummon) < 3,
                $"worker={p7Game.ProbeWorkerWorldPosition(workerId)}");
            p7Game.ProbeAdvance(5);
            Check("p7-summoned-helper-walks",
                p7Game.ProbeWorkerTravelling(workerId) &&
                Vec3.Distance(p7Game.ProbeWorkerWorldPosition(workerId), beforeSummon) > 5,
                $"worker={p7Game.ProbeWorkerWorldPosition(workerId)}");

            for (var i = 0; i < 400 && p7Game.ProbeWorkerTravelling(workerId); i++)
            {
                p7Game.ProbeAdvance(1);
                await Frames(1);
            }
            Check("p7-summoned-arrives-at-player",
                !p7Game.ProbeWorkerTravelling(workerId) &&
                p7Game.ProbeWorkerSummoned(workerId) &&
                Vec3.Distance(p7Game.ProbeWorkerWorldPosition(workerId),
                    reedwaterNurseryWorld) < 6,
                $"region={p7Game.ProbeWorkerRegion(workerId)} " +
                $"world={p7Game.ProbeWorkerWorldPosition(workerId)}");

            var arrivedPos = p7Game.ProbeWorkerWorldPosition(workerId);
            p7Game.ProbeLookAt(new Vec3(arrivedPos.X, arrivedPos.Y + 0.35, arrivedPos.Z));
            p7Game.ProbeSetCameraZoom(5.5f);
            p7Game.ProbeSetCameraPitch(-14);
            await Frames(4);
            await Capture("43-worker-journey");

            // Release sends the helper home physically. Save the return journey
            // from the garden frame (a pre-story Reedwater save is correctly
            // refused by the validator) and continue it after reload.
            Check("p7-release-helper",
                p7Game.ProbeReleaseWorker(workerId), "Release the summoned helper");
            await Frames(4);
            Check("p7-release-heads-home",
                !p7Game.ProbeWorkerSummoned(workerId) &&
                p7Game.ProbeWorkerTravelling(workerId) &&
                p7Game.ProbeWorkerJourneyTarget(workerId) == RegionIds.GardenClearing,
                $"target={p7Game.ProbeWorkerJourneyTarget(workerId)}");
            Check("p7-enter-garden", p7Game.ProbeEnterLandmark(RegionIds.GardenClearing),
                "Return the player/story frame to the garden");
            p7Game.ProbeAdvance(4);
            Check("p7-save-mid-return", p7Game.ProbeSave(),
                "Save the return journey: " + p7Game.LastSaveError);
            var p7Loaded = _main.Saves.Load();
            Check("p7-mid-return-loads",
                p7Loaded.Success && p7Loaded.Data is not null, p7Loaded.Message);
            if (p7Loaded.Data is not null)
            {
                var p7Reloaded = _main.StartGame(p7Loaded.Data);
                await Frames(20);
                Check("p7-reload-resumes-return",
                    p7Reloaded.ProbeWorkerTravelling(workerId) &&
                    p7Reloaded.ProbeWorkerJourneyTarget(workerId) == RegionIds.GardenClearing,
                    $"travelling={p7Reloaded.ProbeWorkerTravelling(workerId)} " +
                    $"target={p7Reloaded.ProbeWorkerJourneyTarget(workerId)}");
                for (var i = 0; i < 400 && p7Reloaded.ProbeWorkerTravelling(workerId); i++)
                {
                    p7Reloaded.ProbeAdvance(1);
                    await Frames(1);
                }
                Check("p7-return-arrives-home",
                    !p7Reloaded.ProbeWorkerTravelling(workerId) &&
                    p7Reloaded.ProbeWorkerRegion(workerId) == RegionIds.GardenClearing &&
                    Vec3.Distance(p7Reloaded.ProbeWorkerWorldPosition(workerId),
                        gardenNurseryWorld) < 12,
                    $"region={p7Reloaded.ProbeWorkerRegion(workerId)} " +
                    $"world={p7Reloaded.ProbeWorkerWorldPosition(workerId)}");

                // Instrument a start south of the river so the navigator must
                // use the visible generated bridge.
                var p7Crossing = p7Reloaded.ProbeRiverCrossing(0, 0);
                var southBank = new Vec3(0, 0, p7Crossing.Z + 13);
                southBank = new Vec3(0,
                    p7Reloaded.ProbeGroundHeightAt(southBank) + 0.4, southBank.Z);
                Check("p7-start-bridge-journey",
                    p7Reloaded.ProbeStartWorkerJourney(workerId,
                        RegionIds.GardenClearing, southBank),
                    $"southBank={southBank}");
                Check("p7-route-uses-bridge",
                    p7Reloaded.ProbeWorkerTravelling(workerId) &&
                    p7Reloaded.ProbeWorkerRouteUsesBridge(workerId),
                    $"travelling={p7Reloaded.ProbeWorkerTravelling(workerId)}");

                var p7NorthBank = new Vec3(0, 0, p7Crossing.Z - 14);
                p7NorthBank = new Vec3(0,
                    p7Reloaded.ProbeGroundHeightAt(p7NorthBank) + 0.6, p7NorthBank.Z);
                p7Reloaded.ProbeTeleport(p7NorthBank);
                p7Reloaded.ProbeLookAt(new Vec3(0, p7Crossing.Y + 0.6, p7Crossing.Z));
                p7Reloaded.ProbeSetCameraZoom(12);
                p7Reloaded.ProbeSetCameraPitch(-14);
                await Frames(6);
                for (var i = 0; i < 40; i++)
                {
                    p7Reloaded.ProbeAdvance(0.5);
                    await Frames(2);
                    var pos = p7Reloaded.ProbeWorkerWorldPosition(workerId);
                    if (pos.Z < p7Crossing.Z - 3) break;
                }
                var bridgeWorker = p7Reloaded.ProbeWorkerWorldPosition(workerId);
                p7Reloaded.ProbeLookAt(new Vec3(
                    bridgeWorker.X, bridgeWorker.Y + 0.3, bridgeWorker.Z));
                p7Reloaded.ProbeSetCameraZoom(8);
                p7Reloaded.ProbeSetCameraPitch(-12);
                await Frames(2);
                await Capture("44-worker-crossing");
                for (var i = 0; i < 120 && p7Reloaded.ProbeWorkerTravelling(workerId); i++)
                {
                    p7Reloaded.ProbeAdvance(1);
                    await Frames(1);
                }
                Check("p7-bridge-arrival",
                    !p7Reloaded.ProbeWorkerTravelling(workerId) &&
                    p7Reloaded.ProbeWorkerRegion(workerId) == RegionIds.GardenClearing,
                    $"region={p7Reloaded.ProbeWorkerRegion(workerId)}");
            }
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- H1/P8: one named summoned helper, following and UI state ----
        var p8Game = _main.StartGame(null);
        await Frames(10);
        p8Game.Simulation!.Ledger.Restore(50, 50);
        Check("p8-first-helper", p8Game.ProbeHatch(), "Hatch the first helper");
        p8Game.ProbeAdvance(4);
        await Frames(4);
        Check("p8-second-helper", p8Game.ProbeHatch(), "Hatch the second helper");
        p8Game.ProbeAdvance(4);
        await Frames(6);
        var p8Workers = p8Game.Simulation.Workers.Take(2).ToList();
        Check("p8-two-helpers", p8Workers.Count == 2, $"workers={p8Game.Simulation.Workers.Count}");
        if (p8Workers.Count == 2)
        {
            var pip = p8Workers[0];
            var bramble = p8Workers[1];
            var farSummon = new Vec3(300, 0, -120);
            farSummon = new Vec3(farSummon.X,
                p8Game.ProbeGroundHeightAt(farSummon) + 0.6, farSummon.Z);
            p8Game.ProbeTeleport(farSummon);
            await Frames(10);
            Check("p8-offscreen-views-culled",
                !p8Game.ProbeWorkerViewVisible(pip.Id) &&
                !p8Game.ProbeWorkerViewVisible(bramble.Id),
                $"visible={p8Game.ProbeWorkerViewVisible(pip.Id)}/" +
                $"{p8Game.ProbeWorkerViewVisible(bramble.Id)}");

            var headlessInput = DisplayServer.GetName() == "headless";
            if (headlessInput) p8Game.ProbeToggleColony();
            else await ClickUi(p8Game.ProbeColonyButtonRect.GetCenter());
            await Frames(4);
            Check("p8-colony-opens", p8Game.ColonyModalOpen, "The named helper list is available");
            var row = p8Game.ProbeWorkerRowText(pip.Id);
            Check("p8-colony-row-shows-location-task",
                row.Contains("m away") && (row.Contains("Patrolling") ||
                    row.Contains("Exploring") || row.Contains("Collecting") ||
                    row.Contains("Repairing") ||
                    row.Contains("Resting") || row.Contains("Available") ||
                    row.Contains("Working")),
                row);
            var summonRect = p8Game.ProbeWorkerSummonRect(pip.Id);
            Check("p8-summon-button", summonRect.Size.X > 0, $"{summonRect}");
            await Capture("45a-colony-helpers");
            var pipBefore = pip.WorldPosition;
            if (headlessInput) p8Game.ProbeSummonWorker(pip.Id);
            else await ClickUi(summonRect.GetCenter());
            await Frames(5);
            p8Game.ProbeCloseModal();
            await Frames(3);
            Check("p8-only-pip-responds",
                p8Game.ProbeWorkerTravelling(pip.Id) &&
                p8Game.ProbeWorkerSummoned(pip.Id) &&
                !p8Game.ProbeWorkerTravelling(bramble.Id) &&
                !p8Game.ProbeWorkerSummoned(bramble.Id),
                $"pip={p8Game.ProbeWorkerTravelling(pip.Id)} " +
                $"bramble={p8Game.ProbeWorkerTravelling(bramble.Id)}");
            Check("p8-summon-does-not-teleport",
                Vec3.Distance(p8Game.ProbeWorkerWorldPosition(pip.Id), pipBefore) < 3 &&
                Vec3.Distance(p8Game.ProbeWorkerJourneyDestination(pip.Id), farSummon) < 6,
                $"worker={p8Game.ProbeWorkerWorldPosition(pip.Id)} " +
                $"destination={p8Game.ProbeWorkerJourneyDestination(pip.Id)}");

            p8Game.ProbeAdvance(10);
            await Frames(4);
            Check("p8-offscreen-journey-progresses",
                p8Game.ProbeWorkerTravelling(pip.Id) &&
                Vec3.Distance(p8Game.ProbeWorkerWorldPosition(pip.Id), pipBefore) > 3 &&
                !p8Game.ProbeWorkerViewVisible(pip.Id),
                $"worker={p8Game.ProbeWorkerWorldPosition(pip.Id)}");

            for (var i = 0; i < 400 && p8Game.ProbeWorkerTravelling(pip.Id); i++)
            {
                p8Game.ProbeAdvance(1);
                await Frames(1);
            }
            Check("p8-summoned-helper-arrives",
                !p8Game.ProbeWorkerTravelling(pip.Id) &&
                p8Game.ProbeWorkerSummoned(pip.Id) &&
                Vec3.Distance(p8Game.ProbeWorkerWorldPosition(pip.Id), farSummon) < 6,
                $"worker={p8Game.ProbeWorkerWorldPosition(pip.Id)}");
            await Frames(8);
            Check("p8-arrived-view-visible", p8Game.ProbeWorkerViewVisible(pip.Id),
                "The summoned helper reappears near the player");

            var arrived = p8Game.ProbeWorkerWorldPosition(pip.Id);
            var mid = (arrived + farSummon) * 0.5;
            p8Game.ProbeLookAt(new Vec3(mid.X, mid.Y + 0.3, mid.Z));
            p8Game.ProbeSetCameraZoom(9);
            p8Game.ProbeSetCameraPitch(-20);
            await Frames(6);
            await Capture("45-summoned-helper");

            // Genuine keyboard movement: the helper must walk after the player,
            // not teleport to the new position.
            Input.ActionPress("move_forward");
            await PhysicsFrames(70);
            Input.ActionRelease("move_forward");
            await PhysicsFrames(10);
            p8Game.ProbeAdvance(0.4);
            await Frames(4);
            Check("p8-summoned-helper-follows",
                p8Game.ProbeWorkerSummoned(pip.Id) &&
                (p8Game.ProbeWorkerTravelling(pip.Id) ||
                 Vec3.Distance(p8Game.ProbeWorkerWorldPosition(pip.Id), arrived) > 2),
                $"worker={p8Game.ProbeWorkerWorldPosition(pip.Id)} arrived={arrived}");

            if (headlessInput) p8Game.ProbeToggleColony();
            else await ClickUi(p8Game.ProbeColonyButtonRect.GetCenter());
            await Frames(4);
            var releaseRect = p8Game.ProbeWorkerReleaseRect(pip.Id);
            Check("p8-release-button", releaseRect.Size.X > 0, $"{releaseRect}");
            if (headlessInput) p8Game.ProbeReleaseWorker(pip.Id);
            else await ClickUi(releaseRect.GetCenter());
            await Frames(4);
            p8Game.ProbeCloseModal();
            await Frames(3);
            Check("p8-release-returns-home",
                !p8Game.ProbeWorkerSummoned(pip.Id) &&
                p8Game.ProbeWorkerJourneyTarget(pip.Id) == RegionIds.GardenClearing,
                $"target={p8Game.ProbeWorkerJourneyTarget(pip.Id)}");

            if (headlessInput) p8Game.ProbeToggleColony();
            else await ClickUi(p8Game.ProbeColonyButtonRect.GetCenter());
            await Frames(4);
            var secondRect = p8Game.ProbeWorkerSummonRect(bramble.Id);
            if (headlessInput) p8Game.ProbeSummonWorker(bramble.Id);
            else await ClickUi(secondRect.GetCenter());
            await Frames(4);
            p8Game.ProbeCloseModal();
            await Frames(3);
            Check("p8-second-summon-releases-first",
                !p8Game.ProbeWorkerSummoned(pip.Id) &&
                p8Game.ProbeWorkerSummoned(bramble.Id) &&
                p8Game.ProbeWorkerTravelling(bramble.Id),
                $"pip={p8Game.ProbeWorkerSummoned(pip.Id)} " +
                $"bramble={p8Game.ProbeWorkerSummoned(bramble.Id)}");

            Check("p8-save", p8Game.ProbeSave(), "Save with a summoned helper in the wild");
            var p8Loaded = _main.Saves.Load();
            Check("p8-save-loads", p8Loaded.Success && p8Loaded.Data is not null, p8Loaded.Message);
            if (p8Loaded.Data is not null)
            {
                var p8Reloaded = _main.StartGame(p8Loaded.Data);
                await Frames(20);
                var reloaded = p8Reloaded.Simulation!.Workers.FirstOrDefault(w => w.Id == bramble.Id);
                Check("p8-reload-keeps-summon-state",
                    reloaded is not null && reloaded.Summoned &&
                    reloaded.HomeRegion == RegionIds.GardenClearing,
                    $"summoned={reloaded?.Summoned} home={reloaded?.HomeRegion}");
            }
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- H2: mark wild food and send a named helper ----
        var h2Game = _main.StartGame(null);
        await Frames(10);
        h2Game.Simulation!.Ledger.Restore(50, 50);
        h2Game.Simulation.Tuning.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
        Check("h2-helper", h2Game.ProbeHatch(), "Hatch a forager");
        h2Game.ProbeAdvance(4);
        await Frames(6);
        var h2Worker = h2Game.Simulation.Workers.FirstOrDefault();
        Check("h2-worker-ready", h2Worker is not null, $"workers={h2Game.Simulation.Workers.Count}");
        if (h2Worker is not null)
        {
            var h2Crossing = h2Game.ProbeRiverCrossing(0, 0);
            var search = new Vec3(0, 0, 90);
            var h2Source = h2Game.ProbeNearestFood(search, 140);
            if (h2Source is null || h2Source.Position.Z < h2Crossing.Z + 8)
            {
                h2Game.ProbeTeleport(new Vec3(0, 0, 110));
                await Frames(8);
                h2Source = h2Game.ProbeNearestFood(new Vec3(0, 0, 110), 140);
            }
            Check("h2-food-found", h2Source is not null,
                $"source={h2Source?.Id} z={h2Source?.Position.Z:0.0} river={h2Crossing.Z:0.0}");
            if (h2Source is not null)
            {
                var sourcePos = h2Source.Position;
                var approach = new Vec3(sourcePos.X - 1.6, 0, sourcePos.Z);
                approach = new Vec3(approach.X,
                    h2Game.ProbeGroundHeightAt(approach) + 0.6, approach.Z);
                h2Game.ProbeTeleport(approach);
                h2Game.ProbeLookAt(new Vec3(sourcePos.X, sourcePos.Y + 0.2, sourcePos.Z));
                await Frames(8);
                // Real movement into the procedural biome before marking.
                var walkedFrom = h2Game.PlayerPosition;
                Input.ActionPress("move_forward");
                await PhysicsFrames(25);
                Input.ActionRelease("move_forward");
                await PhysicsFrames(10);
                Check("h2-walks-procedural-biome",
                    h2Game.PlayerPosition.DistanceTo(walkedFrom) > 0.5f &&
                    h2Game.PlayerPosition.Z > 40,
                    $"from={walkedFrom} to={h2Game.PlayerPosition}");

                var headlessH2 = DisplayServer.GetName() == "headless";
                if (headlessH2) h2Game.ProbeMarkFood();
                else await TapKey(Key.R);
                Check("h2-food-marked", h2Game.Simulation.MapMarkers.Any(marker =>
                    marker.FoodId == h2Source.Id),
                    string.Join("|", h2Game.Simulation.MapMarkers.Select(marker => marker.Id)));

                if (headlessH2) h2Game.ProbeOpenMap();
                else await TapKey(Key.M);
                await Frames(6);
                Check("h2-map-opens", h2Game.ProbeMapOpen, "The map opens");
                var markerId = h2Game.ProbeFoodMarkerId(h2Source.Id);
                var markerRect = h2Game.ProbeMapMarkerRect(markerId);
                Check("h2-map-shows-food-marker",
                    h2Game.ProbeMapMarkerCount >= 1 && markerRect.Size.X > 0,
                    $"markers={h2Game.ProbeMapMarkerCount} rect={markerRect}");
                if (!headlessH2) await Capture("48-food-marker-map");

                if (headlessH2) h2Game.ProbeSelectMapMarker(markerId);
                else await ClickUi(markerRect.GetCenter());
                await Frames(6);
                Check("h2-assignment-opens", h2Game.ProbeFoodTaskOpen,
                    "Marked food opens the named-helper assignment");
                var sendRect = h2Game.ProbeFoodSendRect(h2Worker.Id);
                Check("h2-send-button", sendRect.Size.X > 0, $"{sendRect}");
                var foodBeforeForage = h2Game.Simulation.Ledger.Food;
                if (headlessH2) h2Game.ProbeAssignFood(h2Worker.Id);
                else await ClickUi(sendRect.GetCenter());
                await Frames(4);
                Check("h2-helper-assigned",
                    h2Game.ProbeWorkerFoodTask(h2Worker.Id) &&
                    h2Game.ProbeWorkerFoodPhase(h2Worker.Id) is "ToSource" or "WaitingForRegrowth",
                    $"task={h2Game.ProbeWorkerFoodTask(h2Worker.Id)} " +
                    $"phase={h2Game.ProbeWorkerFoodPhase(h2Worker.Id)}");
                Check("h2-return-route-uses-bridge",
                    h2Game.ProbeWorkerFoodRouteUsesBridge(h2Worker.Id) ||
                    h2Source.Position.Z < h2Crossing.Z,
                    $"usesBridge={h2Game.ProbeWorkerFoodRouteUsesBridge(h2Worker.Id)} " +
                    $"sourceZ={h2Source.Position.Z:0.0} riverZ={h2Crossing.Z:0.0}");

                for (var i = 0; i < 240 && h2Game.ProbeWorkerCarriedFood(h2Worker.Id) == 0; i++)
                {
                    h2Game.ProbeAdvance(0.5);
                    await Frames(1);
                }
                Check("h2-source-depleted",
                    !h2Game.ProbeFoodAvailable(h2Source.Id),
                    $"available={h2Game.ProbeFoodAvailable(h2Source.Id)}");
                Check("h2-helper-carries-food",
                    h2Game.ProbeWorkerCarriedFood(h2Worker.Id) == h2Source.Amount &&
                    h2Game.Simulation.Ledger.Food == foodBeforeForage,
                    $"carried={h2Game.ProbeWorkerCarriedFood(h2Worker.Id)} " +
                    $"food={h2Game.Simulation.Ledger.Food} before={foodBeforeForage}");
                Check("h2-return-route-uses-bridge",
                    h2Game.ProbeWorkerFoodPhase(h2Worker.Id) == "ToHome" &&
                    h2Game.ProbeWorkerFoodRouteUsesBridge(h2Worker.Id),
                    $"phase={h2Game.ProbeWorkerFoodPhase(h2Worker.Id)} " +
                    $"usesBridge={h2Game.ProbeWorkerFoodRouteUsesBridge(h2Worker.Id)}");

                var gatherPos = h2Game.ProbeWorkerWorldPosition(h2Worker.Id);
                var gatherCam = new Vec3(gatherPos.X - 2.2, 0, gatherPos.Z + 2.2);
                gatherCam = new Vec3(gatherCam.X,
                    h2Game.ProbeGroundHeightAt(gatherCam) + 0.6, gatherCam.Z);
                h2Game.ProbeTeleport(gatherCam);
                h2Game.ProbeLookAt(new Vec3(gatherPos.X, gatherPos.Y + 0.3, gatherPos.Z));
                h2Game.ProbeSetCameraZoom(6);
                h2Game.ProbeSetCameraPitch(-14);
                await Frames(6);
                await Capture("49-worker-foraging");

                // Player is far away and the helper is off screen for the return.
                h2Game.ProbeTeleport(new Vec3(300, 0, -120));
                await Frames(6);
                for (var i = 0; i < 500 && h2Game.ProbeWorkerFoodTask(h2Worker.Id); i++)
                {
                    h2Game.ProbeAdvance(1);
                    await Frames(1);
                }
                Check("h2-delivered-home",
                    !h2Game.ProbeWorkerFoodTask(h2Worker.Id) &&
                    h2Game.ProbeWorkerCarriedFood(h2Worker.Id) == 0,
                    $"task={h2Game.ProbeWorkerFoodTask(h2Worker.Id)} " +
                    $"carried={h2Game.ProbeWorkerCarriedFood(h2Worker.Id)}");
                Check("h2-credits-exactly-once",
                    h2Game.Simulation.Ledger.Food == foodBeforeForage + h2Source.Amount &&
                    h2Game.EventLog.Count(e => e.Type == "WorkerFoodDelivered") == 1,
                    $"food={h2Game.Simulation.Ledger.Food} expected={foodBeforeForage + h2Source.Amount} " +
                    $"events={h2Game.EventLog.Count(e => e.Type == "WorkerFoodDelivered")}");
                var h2DeliveredFood = h2Game.Simulation.Ledger.Food;
                h2Game.ProbeAdvance(60);
                await Frames(4);
                Check("h2-no-second-credit", h2Game.Simulation.Ledger.Food == h2DeliveredFood,
                    $"food={h2Game.Simulation.Ledger.Food}");
                var deliveredAt = h2Game.ProbeWorkerWorldPosition(h2Worker.Id);
                var deliveryCam = new Vec3(deliveredAt.X - 2.4, 0, deliveredAt.Z + 2.4);
                deliveryCam = new Vec3(deliveryCam.X,
                    h2Game.ProbeGroundHeightAt(deliveryCam) + 0.6, deliveryCam.Z);
                h2Game.ProbeTeleport(deliveryCam);
                h2Game.ProbeLookAt(new Vec3(deliveredAt.X, deliveredAt.Y + 0.3, deliveredAt.Z));
                h2Game.ProbeSetCameraZoom(6);
                h2Game.ProbeSetCameraPitch(-16);
                await Frames(6);
                await Capture("50-food-delivered");
                Check("h2-save", h2Game.ProbeSave(), h2Game.LastSaveError);
                var h2Loaded = _main.Saves.Load();
                Check("h2-save-loads", h2Loaded.Success && h2Loaded.Data is not null, h2Loaded.Message);
                if (h2Loaded.Data is not null)
                {
                    var h2Reloaded = _main.StartGame(h2Loaded.Data);
                    await Frames(20);
                    Check("h2-reload-keeps-credit-and-marker",
                        h2Reloaded.Simulation!.Ledger.Food == h2DeliveredFood &&
                        h2Reloaded.Simulation.MapMarkers.Any(marker => marker.FoodId == h2Source.Id),
                        $"food={h2Reloaded.Simulation.Ledger.Food} " +
                        $"markers={h2Reloaded.Simulation.MapMarkers.Count}");
                }
            }
        }
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- H0: transactional in-game New Garden and verified restore ----
        // Clear any archive left by the earlier title-screen section so this
        // phase proves the in-game action archived the garden itself.
        try { if (System.IO.File.Exists(_main.Saves.ArchivePath)) System.IO.File.Delete(_main.Saves.ArchivePath); } catch { }
        var h0Game = _main.StartGame(null);
        await Frames(10);
        Check("h0-helper", h0Game.ProbeHatch(), "Hatch a helper to keep in the archive");
        h0Game.ProbeAdvance(4);
        await Frames(4);
        var h0Source = h0Game.ProbeNearestFood(new Vec3(0, 0, 8.5), 500);
        Check("h0-food-found", h0Source is not null, "A wild source for archive evidence");
        if (h0Source is not null)
        {
            var sourcePos = h0Source.Position;
            var approach = new Vec3(sourcePos.X - 1.4,
                h0Game.ProbeGroundHeightAt(new Vec3(sourcePos.X - 1.4, 0, sourcePos.Z)) + 0.6,
                sourcePos.Z);
            h0Game.ProbeTeleport(approach);
            await Frames(6);
            Check("h0-forage", h0Game.ProbeForageNearest(), "Deplete a source before archiving");
        }
        var h0Seed = h0Game.ProbeWorldSeed;
        var h0SaveId = h0Game.ProbeSaveId;
        var h0Food = h0Game.Simulation!.Ledger.Food;
        var h0Explored = h0Game.ExploredChunkCount;
        h0Game.ProbeTeleport(new Vec3(300, 0.6, -120));
        await Frames(8);
        Check("h0-progress-saved", h0Game.ProbeSave(), "Save progress before New Garden");

        // Open the pause menu with the real cancel action, then click the real
        // New game button and its confirmation with mouse input.
        var headless = DisplayServer.GetName() == "headless";
        if (headless)
        {
            h0Game.ProbeOpenPause();
            await Frames(4);
        }
        else
        {
            var escapeDown = new InputEventKey
            {
                PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true
            };
            var escapeUp = new InputEventKey
            {
                PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = false
            };
            Input.ParseInputEvent(escapeDown);
            await Frames(3);
            Input.ParseInputEvent(escapeUp);
            await Frames(4);
        }
        Check("h0-pause-opens", h0Game.PauseModalOpen, "Esc opens the pause menu");
        var newGameRect = h0Game.ProbePauseNewGameRect;
        Check("h0-new-game-button", newGameRect.Size.X > 0, $"{newGameRect}");
        if (headless)
        {
            h0Game.ProbeOpenNewGardenConfirm();
            await Frames(5);
        }
        else
        {
            await ClickUi(newGameRect.GetCenter());
            await Frames(5);
        }
        Check("h0-confirm-visible", h0Game.ProbeNewGardenConfirmVisible,
            "In-game New Garden requires confirmation");
        await Capture("46-new-garden-confirm");
        var confirmRect = h0Game.ProbeConfirmNewGardenRect;
        Check("h0-confirm-button", confirmRect.Size.X > 0, $"{confirmRect}");
        if (headless)
        {
            h0Game.ProbePressConfirmNewGarden();
            await Frames(15);
        }
        else
        {
            await ClickUi(confirmRect.GetCenter());
            await Frames(15);
        }
        Check("h0-new-world-identity",
            _main.Game is not null && _main.Game.ProbeSaveId != h0SaveId &&
            _main.Game.ProbeWorldSeed != 0,
            $"old={h0SaveId} new={_main.Game?.ProbeSaveId} seed={_main.Game?.ProbeWorldSeed}");
        var h0New = _main.Game;
        Check("h0-new-garden-starts-fresh",
            h0New is not null && h0New.Simulation!.Workers.Count == 0 &&
            h0New.Simulation.Missions.CompletedMissions.Count == 0,
            $"workers={h0New?.Simulation?.Workers.Count}");
        Check("h0-archive-available", h0New!.ProbeArchiveAvailable,
            "The previous garden is kept");
        Check("h0-new-save", h0New.ProbeSave(), h0New.LastSaveError);
        _main.ProbeReturnToTitle();
        await Frames(8);

        Check("h0-title-after-new-garden", _main.TitleVisible, "Returned to the title screen");
        var restoreRect = _main.ProbeRestoreButtonRect;
        Check("h0-restore-button",
            restoreRect.Size.X > 0 && _main.ArchiveAvailable, $"{restoreRect}");
        if (headless)
        {
            _main.ProbeRestorePreviousGarden();
            await Frames(20);
        }
        else
        {
            await ClickUi(restoreRect.GetCenter());
            await Frames(20);
        }
        var h0Restored = _main.Game;
        Check("h0-restores-previous-world",
            h0Restored is not null && h0Restored.ProbeWorldSeed == h0Seed,
            $"seed={h0Restored?.ProbeWorldSeed} expected={h0Seed}");
        Check("h0-restores-resources-and-workers",
            h0Restored is not null && h0Restored.Simulation!.Ledger.Food == h0Food &&
            h0Restored.Simulation.Workers.Count == 1,
            $"food={h0Restored?.Simulation?.Ledger.Food} workers={h0Restored?.Simulation?.Workers.Count}");
        Check("h0-restores-exploration",
            h0Restored is not null && h0Restored.ExploredChunkCount >= h0Explored,
            $"explored={h0Restored?.ExploredChunkCount} expected={h0Explored}");
        if (h0Source is not null && h0Restored is not null)
        {
            // Chunk-owned depletion arrives with its chunk, exactly as during
            // normal play: walk back to the source and verify it is still bare.
            var restoredSourcePos = h0Source.Position;
            h0Restored.ProbeTeleport(new Vec3(restoredSourcePos.X - 1.4,
                h0Restored.ProbeGroundHeightAt(new Vec3(restoredSourcePos.X - 1.4, 0,
                    restoredSourcePos.Z)) + 0.6, restoredSourcePos.Z));
            await Frames(20);
            Check("h0-restores-food-depletion",
                !h0Restored.ProbeFoodAvailable(h0Source.Id),
                $"available={h0Restored.ProbeFoodAvailable(h0Source.Id)} id={h0Source.Id}");
        }
        await Capture("47-restored-garden");
        _main.ProbeReturnToTitle();
        await Frames(6);

        // ---- H3: player-built silk crossings over generated rivers ----
        var h3Game = _main.StartGame(null);
        await Frames(10);
        h3Game.Simulation!.Ledger.Restore(50, 50);
        h3Game.Simulation.Tuning.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
        var h3Headless = DisplayServer.GetName() == "headless";

        // 1) Beside an existing generated crossing the planner refuses and
        // offers guidance instead of a second deck.
        var h3NearX = 4.0;
        var h3NearBankZ = RiverMap.CenterAt(0, h3NearX) -
                          RiverMap.HalfWidthAt(0, h3NearX) - 1.0;
        var h3NearBank = new Vec3(h3NearX,
            h3Game.ProbeGroundHeightAt(new Vec3(h3NearX, 0, h3NearBankZ)) + 0.6, h3NearBankZ);
        h3Game.ProbeTeleport(h3NearBank);
        await Frames(10);
        if (h3Headless) h3Game.ProbeToggleRiverBuild();
        else await TapKey(Key.Key3);
        await Frames(8);
        var h3NearPlan = h3Game.ProbeRiverPlan!;
        Check("h3-plan-refused-near-existing-crossing",
            !h3NearPlan.Acceptable && h3NearPlan.Nearby is not null &&
            !h3NearPlan.Nearby.IsPlayerBuilt,
            $"{h3NearPlan.Reason} nearby={h3NearPlan.Nearby?.Describe()}");
        Check("h3-guidance-panel", h3Game.ProbeRiverGuidanceVisible,
            "A nearby bridge already crosses the water");
        if (h3Headless) h3Game.ProbeUseNearbyBridge();
        else await ClickUi(h3Game.ProbeRiverGuidanceUseRect.GetCenter());
        await Frames(8);
        Check("h3-guidance-beacon", h3Game.ProbeRiverBeaconVisible,
            "Beacon over the existing crossing");
        if (!h3Headless) await Capture("51-river-bridge-guidance");

        // Follow the guidance with real walking; it leads to the bank end,
        // never into the water, and clears on arrival.
        var h3EndZ = RiverMap.CenterAt(0, 0) -
                     (RiverMap.HalfWidthAt(0, 0) + RiverMap.BankTaper + 0.8 + 1.5);
        h3Game.ProbeLookAt(new Vec3(2.2,
            h3Game.ProbeGroundHeightAt(new Vec3(2.2, 0, h3EndZ)) + 0.6, h3EndZ));
        Input.ActionPress("move_forward");
        await PhysicsFrames(140);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(10);
        Check("h3-guidance-followed-to-bridge",
            !h3Game.ProbeRiverGuidanceVisible && !h3Game.ProbeRiverBeaconVisible,
            $"pos={h3Game.PlayerPosition} guidance={h3Game.ProbeRiverGuidanceVisible}");

        // 2) A genuine build midway between the generated crossings.
        var h3BuildX = 75.0;
        var h3BuildBankZ = RiverMap.CenterAt(0, h3BuildX) -
                           RiverMap.HalfWidthAt(0, h3BuildX) - 1.0;
        var h3BuildBank = new Vec3(h3BuildX,
            h3Game.ProbeGroundHeightAt(new Vec3(h3BuildX, 0, h3BuildBankZ)) + 0.6,
            h3BuildBankZ);
        h3Game.ProbeTeleport(h3BuildBank);
        await Frames(10);
        var h3SilkBefore = h3Game.Simulation.Ledger.Silk;
        if (h3Headless) h3Game.ProbeToggleRiverBuild();
        else await TapKey(Key.Key3);
        await Frames(8);
        var h3Plan = h3Game.ProbeRiverPlan!;
        Check("h3-plan-accepted-mid-river",
            h3Plan.Acceptable && h3Plan.Site is not null &&
            h3Plan.SilkCost == h3Game.Simulation.Tuning.RiverBridges.SilkCost,
            $"{h3Plan.Reason} site x={h3Plan.Site?.X:0.0}");
        Check("h3-preview-visible", h3Game.ProbeRiverPreviewVisible,
            "Ghost silk bridge spans the water before spending");
        Check("h3-confirm-panel",
            h3Game.ProbeBuildConfirmVisible &&
            h3Game.ProbeBuildConfirmText.Contains("North bank") &&
            h3Game.ProbeBuildConfirmText.Contains("silk"),
            h3Game.ProbeBuildConfirmText);
        if (!h3Headless) await Capture("52-river-bridge-preview");
        if (h3Headless) h3Game.ProbeConfirmRiverBuild();
        else await ClickUi(h3Game.ProbeBuildConfirmButtonRect.GetCenter());
        await Frames(12);
        Check("h3-built-charged-once",
            h3Game.Simulation.RiverBridges.Count == 1 &&
            h3Game.Simulation.Ledger.Silk ==
                h3SilkBefore - h3Game.Simulation.Tuning.RiverBridges.SilkCost,
            $"bridges={h3Game.Simulation.RiverBridges.Count} " +
            $"silk={h3Game.Simulation.Ledger.Silk} before={h3SilkBefore}");
        var h3Bridge = h3Game.Simulation.RiverBridges.First();
        Check("h3-built-view-and-collision",
            h3Game.ProbeRiverBridgeViewCount == 1 &&
            h3Game.ProbeRiverBridgeWalkwayShapes(h3Bridge.Id) > 0 &&
            h3Game.ProbeRiverBridgeDetailCount(h3Bridge.Id) >= 6,
            $"views={h3Game.ProbeRiverBridgeViewCount} " +
            $"shapes={h3Game.ProbeRiverBridgeWalkwayShapes(h3Bridge.Id)} " +
            $"details={h3Game.ProbeRiverBridgeDetailCount(h3Bridge.Id)}");

        // Admire the built deck before crossing it.
        var h3Cam = new Vec3(h3Bridge.X - 10, 0, h3Bridge.CenterZ - 3);
        h3Cam = new Vec3(h3Cam.X, h3Game.ProbeGroundHeightAt(h3Cam) + 0.8, h3Cam.Z);
        h3Game.ProbeTeleport(h3Cam);
        h3Game.ProbeLookAt(new Vec3(h3Bridge.X,
            (h3Bridge.NorthGroundY + h3Bridge.SouthGroundY) * 0.5 + 0.5, h3Bridge.CenterZ));
        h3Game.ProbeSetCameraZoom(9);
        h3Game.ProbeSetCameraPitch(-12);
        await Frames(8);
        if (!h3Headless) await Capture("53-river-bridge-built");

        // 3) Cross the deck with real walking, both directions.
        var h3NorthStand = new Vec3(h3Bridge.X,
            h3Game.ProbeGroundHeightAt(
                new Vec3(h3Bridge.X, 0, h3Bridge.NorthZ - 2.5)) + 0.6, h3Bridge.NorthZ - 2.5);
        h3Game.ProbeTeleport(h3NorthStand);
        await Frames(8);
        h3Game.ProbeLookAt(new Vec3(h3Bridge.X, h3Bridge.SouthGroundY + 0.8, h3Bridge.SouthZ + 2));
        var h3OnDeck = false;
        Input.ActionPress("move_forward");
        for (var i = 0; i < 900; i++)
        {
            await PhysicsFrames(1);
            if (h3Game.ProbePlayerOnRiverBridge(h3Bridge.Id)) h3OnDeck = true;
            if (h3Game.PlayerPosition.Z > h3Bridge.SouthZ + 1.2) break;
        }
        Input.ActionRelease("move_forward");
        await PhysicsFrames(10);
        Check("h3-cross-south",
            h3Game.PlayerPosition.Z > h3Bridge.SouthZ + 0.8 && h3OnDeck,
            $"z={h3Game.PlayerPosition.Z:0.0} south={h3Bridge.SouthZ:0.0} onDeck={h3OnDeck}");
        if (!h3Headless) await Capture("54-river-bridge-crossing");
        h3Game.ProbeLookAt(new Vec3(h3Bridge.X, h3Bridge.NorthGroundY + 0.8, h3Bridge.NorthZ - 2));
        Input.ActionPress("move_forward");
        for (var i = 0; i < 900 && h3Game.PlayerPosition.Z > h3Bridge.NorthZ - 1.2; i++)
            await PhysicsFrames(1);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(10);
        Check("h3-cross-north-again",
            h3Game.PlayerPosition.Z < h3Bridge.NorthZ - 0.8,
            $"z={h3Game.PlayerPosition.Z:0.0} north={h3Bridge.NorthZ:0.0}");

        // 4) The same water cannot be bridged twice side by side. Stand a few
        // metres along the bank — on the deck itself the guidance would be
        // already satisfied, which is correct but not the case under test.
        var h3RedundantStand = new Vec3(h3Bridge.X + 10, 0, h3Bridge.NorthZ - 1.0);
        h3Game.ProbeTeleport(new Vec3(h3RedundantStand.X,
            h3Game.ProbeGroundHeightAt(h3RedundantStand) + 0.6, h3RedundantStand.Z));
        await Frames(8);
        if (h3Headless) h3Game.ProbeToggleRiverBuild();
        else await TapKey(Key.Key3);
        await Frames(8);
        var h3Redundant = h3Game.ProbeRiverPlan!;
        Check("h3-redundant-refused",
            !h3Redundant.Acceptable && h3Redundant.Nearby is not null &&
            h3Redundant.Nearby.IsPlayerBuilt,
            h3Redundant.Reason);
        Check("h3-redundant-guidance", h3Game.ProbeRiverGuidanceVisible,
            "Guidance points at your own bridge");
        if (h3Headless) h3Game.ProbeDismissRiverGuidance();
        else await ClickUi(h3Game.ProbeRiverGuidanceDismissRect.GetCenter());
        await Frames(4);
        Check("h3-redundant-guidance-dismisses", !h3Game.ProbeRiverGuidanceVisible,
            "Keep exploring clears the refusal");

        // 5) A second bridge where the existing crossing needs an unreasonable
        // detour (the mission acceptance's distant site).
        var h3SecondX = 125.0;
        var h3SecondBankZ = RiverMap.CenterAt(0, h3SecondX) -
                             RiverMap.HalfWidthAt(0, h3SecondX) - 1.0;
        var h3SecondBank = new Vec3(h3SecondX,
            h3Game.ProbeGroundHeightAt(new Vec3(h3SecondX, 0, h3SecondBankZ)) + 0.6,
            h3SecondBankZ);
        h3Game.ProbeTeleport(h3SecondBank);
        await Frames(10);
        var h3SecondSilk = h3Game.Simulation.Ledger.Silk;
        if (h3Headless) h3Game.ProbeToggleRiverBuild();
        else await TapKey(Key.Key3);
        await Frames(8);
        var h3SecondPlan = h3Game.ProbeRiverPlan!;
        Check("h3-second-plan-accepted",
            h3SecondPlan.Acceptable && h3SecondPlan.Site is not null,
            $"{h3SecondPlan.Reason} x={h3SecondPlan.Site?.X:0.0}");
        if (h3SecondPlan.Acceptable)
        {
            if (h3Headless) h3Game.ProbeConfirmRiverBuild();
            else await ClickUi(h3Game.ProbeBuildConfirmButtonRect.GetCenter());
            await Frames(12);
            Check("h3-second-built",
                h3Game.Simulation.RiverBridges.Count == 2 &&
                h3Game.Simulation.Ledger.Silk ==
                    h3SecondSilk - h3Game.Simulation.Tuning.RiverBridges.SilkCost,
                $"bridges={h3Game.Simulation.RiverBridges.Count} " +
                $"silk={h3Game.Simulation.Ledger.Silk}");
        }

        // 6) A helper summoned from the far side crosses the new bridge.
        Check("h3-helper", h3Game.ProbeHatch(), "Hatch a helper for the crossing");
        h3Game.ProbeAdvance(4);
        await Frames(6);
        var h3Worker = h3Game.Simulation.Workers.FirstOrDefault();
        Check("h3-worker-ready", h3Worker is not null,
            $"workers={h3Game.Simulation.Workers.Count}");
        if (h3Worker is not null)
        {
            var h3SummonSpot = new Vec3(h3Bridge.X + 3, 0, h3Bridge.CenterZ + 22);
            h3SummonSpot = new Vec3(h3SummonSpot.X,
                h3Game.ProbeGroundHeightAt(h3SummonSpot) + 0.6, h3SummonSpot.Z);
            h3Game.ProbeTeleport(h3SummonSpot);
            await Frames(10);
            if (h3Headless) h3Game.ProbeToggleColony();
            else await ClickUi(h3Game.ProbeColonyButtonRect.GetCenter());
            await Frames(4);
            var h3SummonRect = h3Game.ProbeWorkerSummonRect(h3Worker.Id);
            Check("h3-summon-button", h3SummonRect.Size.X > 0, $"{h3SummonRect}");
            if (h3SummonRect.Size.X > 0)
            {
                if (h3Headless) h3Game.ProbeSummonWorker(h3Worker.Id);
                else await ClickUi(h3SummonRect.GetCenter());
                await Frames(5);
                h3Game.ProbeCloseModal();
                await Frames(4);
                Check("h3-summon-route-uses-player-bridge",
                    h3Game.ProbeWorkerJourneyCrossesRiverAt(h3Worker.Id, h3Bridge.X),
                    $"route={string.Join("→", h3Worker.JourneyRoute.Select(p => $"{p.X:0.0}/{p.Z:0.0}"))}");
                for (var i = 0; i < 700 && h3Game.ProbeWorkerTravelling(h3Worker.Id); i++)
                {
                    h3Game.ProbeAdvance(0.5);
                    await Frames(1);
                }
                Check("h3-summoned-helper-arrives",
                    !h3Game.ProbeWorkerTravelling(h3Worker.Id) &&
                    Vec3.Distance(h3Game.ProbeWorkerWorldPosition(h3Worker.Id), h3SummonSpot) < 8,
                    $"worker={h3Game.ProbeWorkerWorldPosition(h3Worker.Id)} spot={h3SummonSpot}");
            }
        }

        // 7) Persistence: save, relaunch, revisit the crossing.
        var h3BridgeId = h3Bridge.Id;
        Check("h3-save", h3Game.ProbeSave(), h3Game.LastSaveError);
        var h3Loaded = _main.Saves.Load();
        Check("h3-save-loads", h3Loaded.Success && h3Loaded.Data is not null, h3Loaded.Message);
        GardenClearing? h3Reloaded = null;
        if (h3Loaded.Data is not null)
        {
            h3Reloaded = _main.StartGame(h3Loaded.Data);
            // The reloaded simulation keeps default silk production, which
            // would make economy checks nondeterministic over real render
            // time; the live tuning is zeroed exactly like the H2 pattern.
            h3Reloaded.Simulation!.Tuning.SilkProduction.SilkPerFedWorkerEverySeconds = 0;
            await Frames(20);
            var h3Restored = h3Reloaded.Simulation!.RiverBridges
                .FirstOrDefault(bridge => bridge.Id == h3BridgeId);
            Check("h3-reload-keeps-bridge",
                h3Restored is not null &&
                Math.Abs(h3Restored.X - h3Bridge.X) < 0.01 &&
                Math.Abs(h3Restored.CenterZ - h3Bridge.CenterZ) < 0.01,
                $"bridge={h3Restored?.Id} bridges={h3Reloaded.Simulation.RiverBridges.Count}");
            Check("h3-reload-view-rebuilt",
                h3Reloaded.ProbeRiverBridgeViewCount == 2 &&
                h3Reloaded.ProbeRiverBridgeWalkwayShapes(h3BridgeId) > 0,
                $"views={h3Reloaded.ProbeRiverBridgeViewCount}");

            // Walk onto the restored deck with real input.
            h3Reloaded.ProbeTeleport(new Vec3(h3Bridge.X,
                h3Reloaded.ProbeGroundHeightAt(
                    new Vec3(h3Bridge.X, 0, h3Bridge.NorthZ - 2.5)) + 0.6, h3Bridge.NorthZ - 2.5));
            await Frames(10);
            h3Reloaded.ProbeLookAt(new Vec3(h3Bridge.X, h3Bridge.SouthGroundY + 0.8, h3Bridge.SouthZ));
            Input.ActionPress("move_forward");
            for (var i = 0; i < 300; i++)
            {
                await PhysicsFrames(1);
                if (h3Reloaded.ProbePlayerOnRiverBridge(h3BridgeId)) break;
            }
            Input.ActionRelease("move_forward");
            await PhysicsFrames(6);
            Check("h3-reload-deck-walkable", h3Reloaded.ProbePlayerOnRiverBridge(h3BridgeId),
                $"pos={h3Reloaded.PlayerPosition}");
        }

        // 8) Dismantle from the bank: refund, view removal and safe rerouting.
        if (h3Reloaded is not null)
        {
            // The deck refuses removal while the player stands on it.
            var h3DismantleSilk = h3Reloaded.Simulation!.Ledger.Silk;
            if (h3Headless) h3Reloaded.ProbeDismantleNearest();
            else await TapKey(Key.X);
            await Frames(8);
            Check("h3-dismantle-refuses-underfoot",
                h3Reloaded.ProbePlayerOnRiverBridge(h3BridgeId) &&
                h3Reloaded.Simulation.RiverBridges.Count == 2,
                "Standing on the deck blocks removal");

            // From the bank the same action removes it with a refund.
            var h3BankOff = new Vec3(h3Bridge.X + 2.4, 0, h3Bridge.NorthZ - 1.0);
            h3Reloaded.ProbeTeleport(new Vec3(h3BankOff.X,
                h3Reloaded.ProbeGroundHeightAt(h3BankOff) + 0.6, h3BankOff.Z));
            await Frames(8);
            if (h3Headless) h3Reloaded.ProbeDismantleNearest();
            else await TapKey(Key.X);
            await Frames(8);
            var h3Refund = (int)Math.Floor(
                h3Reloaded.Simulation.Tuning.RiverBridges.SilkCost *
                h3Reloaded.Simulation.Tuning.RiverBridges.DismantleRefundFraction);
            Check("h3-dismantle-refund",
                h3Reloaded.Simulation.RiverBridges.Count == 1 &&
                h3Reloaded.Simulation.Ledger.Silk == h3DismantleSilk + h3Refund,
                $"bridges={h3Reloaded.Simulation.RiverBridges.Count} " +
                $"silk={h3Reloaded.Simulation.Ledger.Silk} refund={h3Refund}");
            Check("h3-dismantle-view-removed",
                h3Reloaded.ProbeRiverBridgeViewCount == 1 &&
                h3Reloaded.ProbeRiverBridgeWalkwayShapes(h3BridgeId) == 0,
                $"views={h3Reloaded.ProbeRiverBridgeViewCount}");

            // A journey that would have used the removed deck now routes
            // around the river instead of walking a ghost bridge.
            var h3RerouteWorker = h3Reloaded.Simulation.Workers.FirstOrDefault();
            if (h3RerouteWorker is not null)
            {
                Check("h3-dismantle-reroutes-journeys",
                    h3Reloaded.ProbeStartWorkerJourney(h3RerouteWorker.Id,
                        RegionIds.GardenClearing,
                        new Vec3(h3Bridge.X, 0, h3Bridge.CenterZ + 20)) &&
                    !h3Reloaded.ProbeWorkerJourneyCrossesRiverAt(h3RerouteWorker.Id, h3Bridge.X),
                    $"route={string.Join("→", h3RerouteWorker.JourneyRoute.Select(p => $"{p.X:0.0}"))}");
            }
        }
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
        Check($"toolbar-inside-viewport-{scaleLabel}",
            toolbar.Position.X >= -1 && toolbar.End.X <= viewport.X + 1, $"{toolbar} viewport={viewport}");
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

        var supplyAlreadyClaimed = sim.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank);
        // The adapter drains simulation events into its durable EventLog every
        // frame, so the supply event is observed there, not in sim.Events.
        var supplyEventsBefore = game.EventLog.Count(e =>
            e.Type == "RegionSupply" && e.Region == RegionIds.ReedwaterBank);
        // Real-input acceptance for P2: leave the garden's east gate, cross the
        // generated meadows and step through the (now open) bramble gate. No
        // teleport and no travel dialog are involved.
        var toReedwater = await WalkRoute(game, new Vec3(23, 0.55, 8.5),
            new[]
            {
                new Vec3(40, 0.45, 8.5), new Vec3(55, 0.5, 8.0),
                new Vec3(68, 0.45, 7.0), new Vec3(74, 0.4, 6.2),
                new Vec3(80, 0.45, 6.0)
            }, 0.9f, 100f);
        await Frames(4);
        Check("p2-walks-garden-to-reedwater",
            toReedwater.Reached && game.ActiveRegionId == RegionIds.ReedwaterBank &&
            !toReedwater.EnteredWater,
            $"reached={toReedwater.Reached} region={game.ActiveRegionId} end={toReedwater.End}");
        Check("p2-bramble-gate-open", game.ProbeBrambleGateOpen,
            "The garden story physically removed the barrier");
        Check("p2-both-landmarks-resident",
            game.ResidentAnchorCount == 22 && game.ResidentInsectCount == 7,
            $"anchors={game.ResidentAnchorCount} insects={game.ResidentInsectCount}");
        Check("p2-reedwater-chunks-excluded",
            game.ChunkStream is not null &&
            !game.ChunkStream.IsChunkLoaded(new ChunkCoordinate(4, 0)) &&
            !game.ChunkStream.IsChunkLoaded(new ChunkCoordinate(3, -1)),
            $"loaded={game.ChunkStream?.LoadedCount}");
        var supplyEventsAfter = game.EventLog.Count(e =>
            e.Type == "RegionSupply" && e.Region == RegionIds.ReedwaterBank);
        // A continued save may already hold the durable outpost claim. Either
        // way, the transition must never grant the cache a second time.
        Check("reedwater-supply-granted-once",
            sim.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank) &&
            (supplyAlreadyClaimed
                ? supplyEventsAfter == supplyEventsBefore
                : supplyEventsAfter == supplyEventsBefore + 1),
            $"claimed={string.Join(",", sim.ClaimedSupplyCaches)} already={supplyAlreadyClaimed} " +
            $"events={supplyEventsBefore}->{supplyEventsAfter}");
        Check("p2-entered-reedwater-on-foot",
            game.PlayerPosition.DistanceTo(GardenClearing.ToVector3(new Vec3(80, 0.45, 6))) < 2.5f,
            $"player={game.PlayerPosition}");
        // Evidence of the physical connection: stand in the connecting meadow
        // and look through the opened bramble gate into Reedwater.
        game.ProbeTeleport(new Vec3(70.5, 0.55, 6));
        game.ProbeLookAt(new Vec3(80, 0.7, 6));
        await Frames(4);
        await Capture("20b-reedwater-path");
        // Step back through the gate: the arrival capture must show the
        // landmark the player actually reached.
        game.ProbeTeleport(Rw(7.2, 0.55, 7.0));
        game.ProbeLookAt(Rw(-2, 0.4, 2.0));
        await Frames(6);
        Check("reedwater-anchors-visible", game.VisibleAnchorCount == 10,
            $"anchors={game.VisibleAnchorCount}");
        Check("reedwater-insects-visible", game.VisibleInsectCount == 3,
            $"insects={game.VisibleInsectCount}");
        // Hatch at the outpost while the arrival supply cache still covers the
        // cost; later walking/time-based silk production must not make this
        // deterministic feature check depend on a race.
        // A continued save may have spent the arrival cache on an earlier run;
        // this check covers the hatch decision, not economy endurance.
        if (sim.Ledger.Food < 4 || sim.Ledger.Silk < 4)
            sim.Ledger.Restore(Math.Max(sim.Ledger.Food, 4), Math.Max(sim.Ledger.Silk, 4));
        var hatchCheck = sim.CanHatch();
        var hatched = game.ProbeHatch();
        Check("reedwater-helper-hatched", hatched,
            hatchCheck.CanHatch ? "Outpost hatch uses shared colony resources" : hatchCheck.Reason);
        await Frames(6);
        await Capture("21-reedwater-arrival");

        var startDry = Rw(7.0, 0.55, 8.5);
        var farDry = Rw(-7.6, 0.55, -1.0);
        var islandADry = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            sim.GetAnchor(ReedwaterLayout.IslandAPost)!.Position).Absolute;
        var islandBDry = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            sim.GetAnchor(ReedwaterLayout.IslandBPost)!.Position).Absolute;
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
            Rw(4.2, -0.3, 8.5),
            Rw(-2.5, -0.3, -4.0),
            Rw(-6.2, -0.3, 5.0)
        };
        Check("shoreline-open-water-is-hazardous",
            openWaterPoints.All(point => game.ProbeWaterHazardAt(point)),
            string.Join(" ", openWaterPoints.Select(point =>
                $"{point}:{game.ProbeWaterHazardAt(point)}")));

        game.ProbeSetSafePosition(startDry);
        game.ProbeTeleport(Rw(4.2, -0.3, 8.5));
        await Frames(8);
        Check("shoreline-water-recovery",
            game.PlayerPosition.DistanceTo(GardenClearing.ToVector3(startDry)) < 0.9f &&
            !game.ProbeInWater(),
            $"player={game.PlayerPosition} safe={GardenClearing.ToVector3(startDry)}");

        // The pond is real collision, not a simulation flag. Walk west from the
        // starting bank without a bridge and verify the water returns the player.
        game.ProbeSetSafePosition(Rw(10, 0.55, 8.5));
        game.ProbeTeleport(Rw(6.5, 0.55, 6.5));
        game.ProbeLookAt(Rw(0, 0.55, 5.8));
        await Frames(3);
        Input.ActionPress("move_forward");
        await PhysicsFrames(110);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(20);
        Check("water-blocks-without-bridge",
            game.PlayerPosition.X > 101.9f,
            $"x={game.PlayerPosition.X:0.00} inWater={game.ProbeInWater()}");
        await Capture("22-reedwater-pond");
        game.ProbeSetCameraZoom(15);
        game.ProbeTeleport(Rw(9.5, 1.2, 12.8));
        game.ProbeLookAt(Rw(-4.0, 0.4, 2.0));
        await Frames(4);
        await Capture("22b-reedwater-overview");
        game.ProbeTeleport(Rw(3.2, 0.8, 9.0));
        game.ProbeLookAt(Rw(1.0, 0.2, 5.8));
        await Frames(4);
        await Capture("22c-first-island");
        game.ProbeSetCameraZoom(10);

        game.ProbeSetSafePosition(Rw(8.5, 0.55, 12.0));
        game.ProbeTeleport(Rw(8.5, 0.55, 12.0));
        game.ProbeLookAt(Rw(4.5, 0.25, 8.0));
        await Frames(4);
        await Capture("22d-start-shoreline");

        game.ProbeSetSafePosition(Rw(-8.8, 0.55, 3.0));
        game.ProbeTeleport(Rw(-8.8, 0.55, 3.0));
        game.ProbeLookAt(Rw(-4.5, 0.25, 2.0));
        await Frames(4);
        await Capture("22e-far-shoreline");

        game.ProbeSetSafePosition(islandADry);
        game.ProbeTeleport(islandADry);
        game.ProbeLookAt(Rw(3.8, 0.3, 8.8));
        await Frames(4);
        await Capture("22f-island-edges");

        game.ProbeSetSafePosition(Rw(7.5, 0.55, 11.5));
        game.ProbeTeleport(Rw(7.5, 0.55, 11.5));
        game.ProbeLookAt(Rw(2.0, 0.25, 7.5));
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
        // Same reasoning: guarantee the regional objective sequence is not
        // derailed by resources spent in an earlier probe session.
        if (sim.Ledger.Silk < 40 || sim.Ledger.Food < 8)
            sim.Ledger.Restore(Math.Max(sim.Ledger.Food, 8), Math.Max(sim.Ledger.Silk, 40));
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
        game.ProbeTeleport(Rw(8.8, 1.6, 10.4));
        game.ProbeLookAt(Rw(3.4, 0.55, 6.2));
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
        game.ProbeTeleport(Rw(7.6, 0.55, 7.6));
        await Frames(2);

        // Real walking traversal. Every run starts on legitimate bank/island
        // ground; the player is never placed on or above a deck.
        var toIslandA = await WalkRoute(game, Rw(9.2f, 0.55f, 8.6f),
            ReedwaterToIslandA, 0.7f, 40f);
        Check("player-walks-bank-onto-bridge-one",
            toIslandA.Reached && toIslandA.End.X < 98.6f && !toIslandA.EnteredWater,
            $"reached={toIslandA.Reached} end={toIslandA.End} seconds={toIslandA.Seconds:0.0}");
        Check("player-walks-bridge-one-to-island-a", toIslandA.Reached &&
            Mathf.Abs(toIslandA.End.Z - 6.4f) < 0.9f,
            $"end={toIslandA.End}");
        await Capture("24-reedwater-crossing");

        var toIslandB = await WalkRoute(game, toIslandA.End,
            ReedwaterIslandAToIslandB, 0.7f, 40f);
        Check("player-walks-island-a-onto-bridge-two",
            toIslandB.Reached && toIslandB.End.X < 93.8f && !toIslandB.EnteredWater,
            $"reached={toIslandB.Reached} end={toIslandB.End}");
        Check("clever-crossing-not-awarded-on-bridge",
            !sim.RegionalObjectives.MasteryAwarded,
            $"bridges={sim.ReedwaterBridgeCount} position={toIslandB.End}");

        // Step off the bridge deck onto the lily island itself. The badge is
        // only allowed once the spider is grounded inside the island footprint
        // at its walkable surface height, not while crossing above it.
        var ontoIsland = await WalkRoute(game, toIslandB.End,
            new[] { Rw(-4.0, 0.15, 0.8) }, 0.4f, 12f);
        var islandGround = game.ProbeGroundColliderPath();
        Check("player-steps-onto-lily-island",
            ontoIsland.Reached && !ontoIsland.EnteredWater &&
            ReedwaterLayout.IsOnFirstDestinationIsland(
                new Vec3(ontoIsland.End.X - 96, ontoIsland.End.Y, ontoIsland.End.Z)) &&
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
            backToBank.Reached && backToBank.End.X > 104.2f && !backToBank.EnteredWater,
            $"end={backToBank.End} seconds={backToBank.Seconds:0.0}");

        // Stop and restart on the ramp/anchor joint, then turn around mid-deck.
        var stopped = await WalkRoute(game, Rw(9.2f, 0.55f, 8.6f),
            ReedwaterToIslandA, 0.7f, 40f, stopAtWaypoint: 2, stopFrames: 60);
        Check("player-stops-and-resumes-at-deck-joint", stopped.Reached,
            $"end={stopped.End}");
        var midDeck = await WalkRoute(game, Rw(9.2f, 0.55f, 8.6f),
            new[]
            {
                Rw(7.6, 0, 7.6), Rw(6.6, 0.55, 7.0),
                Rw(4.0, 0.25, 6.3)
            }, 0.55f, 30f);
        var midX = game.PlayerPosition.X;
        game.ProbeLookAt(Rw(9.2, 0.6, 8.6));
        await Frames(2);
        Input.ActionPress("move_forward");
        await PhysicsFrames(14);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(6);
        var turnedBackX = game.PlayerPosition.X;
        game.ProbeLookAt(Rw(1.4, 0.6, 5.6));
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
            game.ProbeTeleport(LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank, trapped.Position).Absolute);
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
        // Start where the spider actually stands on the island after earning
        // the badge, then walk to the shared landing and across bridge three.
        // Teleporting onto the landing itself could wedge the capsule in the
        // bridge two/three joint and made the check intermittently time out.
        var toFarBank = await WalkRoute(game, Rw(-2.78f, 0.24f, 2.55f),
            ReedwaterIslandBToFarBank, 0.7f, 60f);
        Check("player-walks-island-b-onto-bridge-three",
            toFarBank.Reached && toFarBank.End.X < 87.6f && !toFarBank.EnteredWater,
            $"reached={toFarBank.Reached} end={toFarBank.End}");
        var farBankBack = await WalkRoute(game, toFarBank.End,
            Reverse(ReedwaterIslandBToFarBank), 0.7f, 60f);
        Check("player-walks-bridge-three-back-to-island-b",
            farBankBack.Reached && farBankBack.End.X > 92.5f,
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
        game.ProbeClearToastHistory();
        for (var i = 0; i < 100 && !regionComplete; i++)
        {
            game.ProbeAdvance(2);
            await Frames(2);
            workerCrossed |= HasRegionEvent("WorkerUsedBridge", RegionIds.ReedwaterBank);
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
        // Check all toasts raised during the window, not just whichever one is
        // still on screen after a two-second simulation step.
        var toasts = game.ProbeToastHistory;
        workerAttributed = toasts.Any(text =>
            text.Contains("collected a") && !text.StartsWith("You collected"));
        farBankFeedback = toasts.Any(text =>
            text.Contains("from the far bank") || text.Contains("brought far-bank prey home"));
        dragonflyNamed = toasts.Any(text => text.Contains("dragonfly"));
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
        game.ProbeSetSafePosition(Rw(-9.6, 0.55, -2.8));
        game.ProbeTeleport(Rw(-9.6, 0.55, -2.8));
        await Frames(3);
        game.ProbeInteract();
        await Frames(6);
        var outpost = Rw(10.2, 0.55, 6.8);
        Check("far-bank-return-shortcut",
            Vec3.Distance(new Vec3(game.PlayerPosition.X, game.PlayerPosition.Y, game.PlayerPosition.Z),
                outpost) < 2.5f,
            $"player={game.PlayerPosition}");
        var farWorker = sim.RegionWorkers.FirstOrDefault();
        if (farWorker is not null)
        {
            game.ProbeSetSafePosition(Rw(-8.6, 0.6, -3.6));
            game.ProbeTeleport(Rw(-8.6, 0.6, -3.6));
            game.ProbeLookAt(farWorker.Position);
            await Frames(4);
        }
        await Capture("26-reedwater-worker");

        // Save and reload inside the second region.
        Check("reedwater-save", game.ProbeSave(), "Save while standing in Reedwater Bank");
        var loaded = _main.Saves.Load();
        Check("reedwater-save-loads", loaded.Success && loaded.Data is not null, loaded.Message);
        var manifestResult = _main.Saves.WorldStore.LoadManifest();
        var manifest = manifestResult.Manifest;
        Check("p2-manifest-written",
            File.Exists(_main.Saves.WorldStore.ManifestPath) && manifestResult.Success,
            _main.Saves.WorldStore.ManifestPath);
        Check("p2-manifest-schema-and-landmark",
            manifest is { SchemaVersion: WorldManifestSave.CurrentSchemaVersion } &&
            manifest.ActiveLandmarkId == RegionIds.ReedwaterBank,
            $"schema={manifest?.SchemaVersion} landmark={manifest?.ActiveLandmarkId}");
        Check("p2-manifest-seed",
            manifest is not null && manifest.Seed == game.Simulation.Tuning.Seed &&
            manifest.Seed > 0,
            $"manifest={manifest?.Seed} simulation={game.Simulation.Tuning.Seed}");
        Check("p2-manifest-world-position",
            manifest is not null &&
            Math.Abs(manifest.Player.Absolute.X - game.PlayerPosition.X) < 1.0 &&
            Math.Abs(manifest.Player.Absolute.Z - game.PlayerPosition.Z) < 1.0,
            $"manifest={manifest?.Player.Absolute} player={game.PlayerPosition}");
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
            Check("p2-reload-keeps-world-seed",
                manifest is not null &&
                reloadedRegion.Simulation.Tuning.Seed == manifest.Seed,
                $"simulation={reloadedRegion.Simulation.Tuning.Seed} manifest={manifest?.Seed}");
            Check("reload-reedwater-construction",
                reloadedRegion.Simulation.Webs.Count(web => web.Region == RegionIds.ReedwaterBank) == regionWebs,
                $"webs={regionWebs}");
            Check("reload-reedwater-objectives",
                reloadedRegion.Simulation.RegionalObjectives.CompletedObjectives.Count == completed,
                $"completed={completed}");
            var reloadWalk = await WalkRoute(reloadedRegion, Rw(9.2f, 0.55f, 8.6f),
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
                // A helper may be mid-crossing; stand down before testing that
                // removing the bridge itself invalidates the graph route.
                foreach (var helper in reloadedRegion.Simulation.Workers)
                    reloadedRegion.Simulation.ReleaseWorkerTask(helper, "bridge regression setup");
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

    /// <summary>The load ring around a centre must always be fully materialised.</summary>
    private static bool RingComplete(ChunkStreamer chunks, ChunkCoordinate centre)
    {
        for (var dx = -3; dx <= 3; dx++)
        for (var dz = -3; dz <= 3; dz++)
        {
            var candidate = centre.Offset(dx, dz);
            if (WorldGenerator.IsAuthoredChunk(candidate)) continue;
            if (!chunks.IsChunkLoaded(candidate)) return false;
        }
        return true;
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

    /// <summary>Reedwater-local authored X plus its constant world origin.
    /// Routes and teleports in the Reedwater section are written in that
    /// landmark's own frame for readability, then anchored in world space.</summary>
    private static Vec3 Rw(double x, double y, double z) => new(96 + x, y, z);

    private static readonly Vec3[] ReedwaterToIslandA =
    {
        Rw(9.2, 0.55, 8.6), Rw(7.6, 0.0, 7.6), Rw(6.6, 0.55, 7.0),
        Rw(5.0, 0.3, 6.6), Rw(3.4, 0.22, 6.1), Rw(2.0, 0.4, 5.7),
        Rw(1.4, 0.55, 5.6), Rw(2.2, 0.15, 6.4)
    };

    private static readonly Vec3[] ReedwaterIslandAToIslandB =
    {
        Rw(2.2, 0.15, 6.4), Rw(1.4, 0.55, 5.6), Rw(0.0, 0.3, 4.4),
        Rw(-1.6, 0.25, 3.2), Rw(-3.0, 0.55, 1.8), Rw(-2.7, 0.15, 2.7)
    };

    private static readonly Vec3[] ReedwaterIslandBToFarBank =
    {
        Rw(-2.7, 0.15, 2.7), Rw(-3.0, 0.55, 1.8), Rw(-4.4, 0.3, 0.9),
        Rw(-5.8, 0.25, 0.0), Rw(-7.4, 0.55, -1.0), Rw(-8.8, 0.0, -1.9),
        Rw(-9.4, 0.0, -2.4)
    };

    private static Vec3[] Reverse(Vec3[] route)
    {
        var copy = (Vec3[])route.Clone();
        Array.Reverse(copy);
        return copy;
    }

    /// <summary>Viewport coordinates are logical; OS input is in window
    /// coordinates. The final transform includes the content scale, so clicks
    /// stay accurate on scaled desktops and under Xvfb.</summary>
    private Vector2 ToWindowPoint(Vector2 position) =>
        GetViewport().GetFinalTransform() * position;

    private async Task MouseMove(Vector2 position)
    {
        var window = ToWindowPoint(position);
        var motion = new InputEventMouseMotion { Position = window, GlobalPosition = window };
        Input.ParseInputEvent(motion);
        await Frames(2);
    }

    /// <summary>Send a real mouse click through the viewport GUI path.</summary>
    private async Task ClickUi(Vector2 position)
    {
        var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position };
        GetViewport().PushInput(motion);
        await Frames(2);
        var down = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
            GlobalPosition = position
        };
        GetViewport().PushInput(down);
        await Frames(2);
        var up = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = position,
            GlobalPosition = position
        };
        GetViewport().PushInput(up);
        await Frames(3);
    }

    private async Task TapKey(Key key)
    {
        var down = new InputEventKey
        {
            PhysicalKeycode = key, Keycode = key, Pressed = true
        };
        Input.ParseInputEvent(down);
        await Frames(3);
        var up = new InputEventKey
        {
            PhysicalKeycode = key, Keycode = key, Pressed = false
        };
        Input.ParseInputEvent(up);
        await Frames(4);
    }

    private async Task Click(Vector2 position)
    {
        var window = ToWindowPoint(position);
        var down = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = window,
            GlobalPosition = window
        };
        Input.ParseInputEvent(down);
        await Frames(2);
        var up = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = window,
            GlobalPosition = window
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
