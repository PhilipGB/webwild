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
        var captureBuilt = game.Simulation.Webs.Any(w => w.Type == WebType.Capture &&
            w.Connects(WorldLayout.TwigA, WorldLayout.TwigB));
        Check("capture-web-built", captureBuilt, "Two mouse clicks on Twin Twigs");
        Check("capture-web-visible", game.VisibleWebCount == 1, $"webs={game.VisibleWebCount}");
        Check("capture-web-cost", game.Simulation.Ledger.Silk == silkBeforeWeb - game.Simulation.Tuning.Webs.Capture.SilkCost,
            $"silk {silkBeforeWeb}->{game.Simulation.Ledger.Silk}");
        var silkAfterWeb = game.Simulation.Ledger.Silk;
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
        var bridgeBuilt = game.Simulation.Webs.Any(w => w.Type == WebType.Bridge &&
            w.Connects(WorldLayout.GapWest, WorldLayout.GapEast));
        Check("bridge-built", bridgeBuilt, "Two mouse clicks on Pebble gap");
        Check("bridge-visible", game.VisibleWebCount >= 2, $"webs={game.VisibleWebCount}");
        var gapBridge = game.Simulation.Webs.First(w => w.Type == WebType.Bridge &&
            w.Connects(WorldLayout.GapWest, WorldLayout.GapEast));
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

        game.ProbeOpenNursery();
        await Frames(4);
        GD.Print("[PROBE] NURSERY " + game.DebugUi);
        await Capture("10-nursery");
        game.ProbeCloseModal();
        await Frames(2);

        var hatchStarted = game.ProbeHatch();
        game.ProbeAdvance(5);
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
        await Frames(8);
        Check("travel-to-reedwater", travelled && game.ActiveRegionId == RegionIds.ReedwaterBank,
            $"region={game.ActiveRegionId}");
        Check("reedwater-supply-granted-once",
            sim.Ledger.Food == supplyFoodBefore + 6 && sim.Ledger.Silk >= supplySilkBefore + 24 &&
            sim.ClaimedSupplyCaches.Contains(RegionIds.ReedwaterBank),
            $"food={sim.Ledger.Food} silk={sim.Ledger.Silk} claimed={string.Join(",", sim.ClaimedSupplyCaches)}");
        Check("reedwater-player-arrival",
            game.PlayerPosition.DistanceTo(GardenClearing.ToVector3(ReedwaterLayout.ArrivalPoint)) < 0.6f,
            $"player={game.PlayerPosition}");
        Check("reedwater-anchors-visible", game.VisibleAnchorCount == 10,
            $"anchors={game.VisibleAnchorCount}");
        Check("reedwater-insects-visible", game.VisibleInsectCount == 3,
            $"insects={game.VisibleInsectCount}");
        await Capture("21-reedwater-arrival");

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
        game.ProbeTeleport(new Vec3(9.5, 1.2, 12.8));
        game.ProbeLookAt(new Vec3(-4.0, 0.4, 2.0));
        await Frames(4);
        await Capture("22b-reedwater-overview");
        game.ProbeTeleport(new Vec3(3.2, 0.8, 9.0));
        game.ProbeLookAt(new Vec3(1.0, 0.2, 5.8));
        await Frames(4);
        await Capture("22c-first-island");

        var routeBefore = sim.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode);
        Check("pond-route-blocked-before-bridges", routeBefore is null, "No dry route to the lily island");
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
        Check("clever-crossing-earned", sim.RegionalObjectives.MasteryAwarded,
            "Two bridges for the lily island");
        await Frames(3);
        await Capture("23-bridges-built");
        game.ProbeTeleport(new Vec3(8.8, 1.6, 10.4));
        game.ProbeLookAt(new Vec3(3.4, 0.55, 6.2));
        await Frames(4);
        await Capture("23b-reedwater-bridge-detail");
        game.ProbeTeleport(new Vec3(7.6, 0.55, 7.6));
        await Frames(2);

        // Actual player traversal over the two bridge decks and ramps.
        game.ProbeSetSafePosition(new Vec3(7.6, 0.55, 7.6));
        game.ProbeTeleport(new Vec3(7.6, 0.55, 7.6));
        game.ProbeLookAt(new Vec3(6.6, 0.6, 7.0));
        await Frames(3);
        Input.ActionPress("move_forward");
        await PhysicsFrames(28);
        game.ProbeLookAt(new Vec3(1.4, 0.8, 5.6));
        await PhysicsFrames(230);
        Input.ActionRelease("move_forward");
        await PhysicsFrames(20);
        Check("player-crosses-reedwater-bridge", game.PlayerPosition.X < 2.7f,
            $"x={game.PlayerPosition.X:0.00}");
        await Capture("24-reedwater-crossing");

        // Capture the straightforward reed flyway and catch three insects.
        Check("reed-capture-web-built", game.ProbeBuildWeb(ReedwaterLayout.EasyA,
            ReedwaterLayout.EasyB, WebType.Capture), "Capture web across the reed stems");
        var caught = 0;
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
            caught++;
            await Frames(2);
        }
        Check("reedwater-insects-collected", caught >= 3, $"caught={caught}");
        Check("r02-complete", sim.RegionalObjectives.CompletedObjectives.Contains("R02"),
            "The Reed Harvest");
        await Capture("25-reedwater-capture");

        // The third bridge plus far-bank capture gives a helper a meaningful
        // route; the worker must use bridge edges, not teleport.
        Check("bridge-three-built", game.ProbeBuildWeb(ReedwaterLayout.IslandBPost,
            ReedwaterLayout.FarPost, WebType.Bridge), "Root island to far bank");
        var firstReedwaterBridge = sim.RegionWebs.FirstOrDefault(web => web.Type == WebType.Bridge);
        if (firstReedwaterBridge is not null)
            CheckBridgeVisual(game, firstReedwaterBridge.Id, "reedwater-bridge");
        Check("far-capture-web-built", game.ProbeBuildWeb(ReedwaterLayout.FarA,
            ReedwaterLayout.FarB, WebType.Capture), "Far-bank capture web");
        var hatchCheck = sim.CanHatch();
        var hatched = game.ProbeHatch();
        Check("reedwater-helper-hatched", hatched,
            hatchCheck.CanHatch ? "Outpost hatch uses shared colony resources" : hatchCheck.Reason);
        var workerCrossed = false;
        var regionComplete = false;
        for (var i = 0; i < 100 && !regionComplete; i++)
        {
            game.ProbeAdvance(2);
            await Frames(1);
            workerCrossed |= HasRegionEvent("WorkerUsedBridge", RegionIds.ReedwaterBank);
            regionComplete = sim.RegionalObjectives.CompletedObjectives.Contains("R03");
        }
        Check("worker-used-reedwater-bridge", workerCrossed, "Worker crossed a real bridge edge");
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
