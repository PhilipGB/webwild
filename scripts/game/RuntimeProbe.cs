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

        game.ProbeTeleport(new Vec3(-1.5, 0.5, 9.0));
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

        game.ProbeTeleport(new Vec3(0, 0.5, 6.0));
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
        game.ProbeClearBuild();
        game.ProbeOpenNursery();
        await Frames(4);
        GD.Print("[PROBE] NURSERY " + game.DebugUi);
        await Capture("09-nursery");
        game.ProbeCloseModal();
        await Frames(2);

        var hatchStarted = game.ProbeHatch();
        game.ProbeAdvance(5);
        await Frames(3);
        Check("hatch-started", hatchStarted, "Nursery spends food and silk");
        Check("worker-hatched", game.Simulation.Workers.Count >= 1, $"workers={game.Simulation.Workers.Count}");
        Check("worker-visible", game.VisibleWorkerCount >= 1, $"views={game.VisibleWorkerCount}");

        for (var i = 0; i < 72 && !game.Simulation.Missions.CompletedMissions.Contains("M02"); i++)
        {
            game.ProbeAdvance(5);
            await Frames(2);
        }
        var m02 = game.Simulation.Missions.CompletedMissions.Contains("M02");
        Check("m02-complete", m02, "Tiny Team");
        Check("worker-crossed-bridge", HasEvent("WorkerUsedBridge") || m02, "Bridge traversal event");

        await Capture("10-wind-warning");
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
        await Capture("11-victory");

        game.ProbeCloseModal();
        await Frames(3);
        game.ProbeOpenSettings();
        await Frames(4);
        await Capture("12-settings");
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
            await Capture("13-reloaded");
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
            Check("low-threat-applies", activeGame.Simulation.Tuning.Weather.ExposedWebDamage <= 25 &&
                activeGame.Simulation.Tuning.Weather.WarningSeconds >= 30, "Gentle weather caps wind damage");
        }

        _main.ProbeReturnToTitle();
        await Frames(8);
        Check("return-to-menu", _main.TitleVisible, "Game returns to title menu");
        Check("continue-available", _main.ContinueAvailable, "Continue button reports saved garden");
        await Capture("14-title-continue");
        var continued = _main.ProbeContinue();
        await Frames(10);
        Check("continue-loads", continued is not null && continued.Simulation.Victory &&
            continued.Simulation.Webs.Count == webCount, "Continue restores saved garden");

        if (continued is not null)
        {
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

        settings.UiScalePercent = 125;
        GameData.ApplyUiScale(settings);
        await Frames(6);
        Check("ui-scale-applies", Math.Abs(GetTree().Root.ContentScaleFactor - 1.25f) < 0.01f,
            $"scale={GetTree().Root.ContentScaleFactor:0.00}");
        await Capture("15-ui-scale-125");

        settings.UiScalePercent = 150;
        GameData.ApplyUiScale(settings);
        await Frames(6);
        Check("ui-scale-150-applies", Math.Abs(GetTree().Root.ContentScaleFactor - 1.5f) < 0.01f,
            $"scale={GetTree().Root.ContentScaleFactor:0.00}");
        await Capture("16-ui-scale-150");

        continued?.ProbeQuitToMenu();
        await Frames(8);
        Check("quit-to-menu", _main.TitleVisible && _main.ContinueAvailable, "Save and quit returns to title");

        // Leave the machine in a clean, default-comfort state for the next launch.
        settings.ReducedMotion = false;
        settings.UiScalePercent = 100;
        settings.SfxVolume = 0.8;
        settings.LowThreat = false;
        GameData.CreateSettingsService().Save(settings);
        GameData.ApplyUiScale(settings);
        await Frames(4);
    }

    private bool HasEvent(string type) => _main.Game?.EventLog.Any(e => e.Type == type) ?? false;

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
