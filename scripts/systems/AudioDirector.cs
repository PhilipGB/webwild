using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>Original synthesized audio. Gameplay information is always duplicated visually.</summary>
public sealed partial class AudioDirector : Node
{
    private AudioStreamPlayer? _music;
    private readonly Dictionary<string, AudioStreamPlayer> _players = new();
    private SettingsData _settings = new();

    public override void _Ready()
    {
        EnsureBuses();
        foreach (var name in new[] { "click", "cancel", "build", "catch", "collect", "hatch", "repair", "wind", "success" })
        {
            var player = new AudioStreamPlayer { Name = "Sfx_" + name, Bus = "SFX" };
            var loaded = GD.Load<AudioStream>($"res://art/audio/sfx/{name}.wav");
            if (loaded is not null)
            {
                player.Stream = loaded;
                AddChild(player);
                _players[name] = player;
            }
        }

        _music = new AudioStreamPlayer { Name = "Music", Bus = "Music" };
        var music = GD.Load<AudioStream>("res://art/audio/music/garden_loop.wav");
        if (music is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = 0;
        }
        if (music is not null)
        {
            _music.Stream = music;
            AddChild(_music);
            _music.Play();
        }
        ApplySettings(_settings);
    }

    private static void EnsureBuses()
    {
        foreach (var busName in new[] { "Music", "SFX" })
        {
            if (AudioServer.GetBusIndex(busName) != -1) continue;
            var index = AudioServer.BusCount;
            AudioServer.AddBus(index);
            AudioServer.SetBusName(index, busName);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    public void ApplySettings(SettingsData settings)
    {
        _settings = settings;
        AudioServer.SetBusVolumeDb(0, LinearToDb(settings.MasterVolume));
        var music = AudioServer.GetBusIndex("Music");
        if (music >= 0) AudioServer.SetBusVolumeDb(music, LinearToDb(settings.MusicVolume));
        var sfx = AudioServer.GetBusIndex("SFX");
        if (sfx >= 0) AudioServer.SetBusVolumeDb(sfx, LinearToDb(settings.SfxVolume));
        AudioServer.SetBusMute(0, settings.MasterVolume <= 0.001f);
    }

    private static float LinearToDb(double linear)
    {
        if (linear <= 0.0001) return -80f;
        return Mathf.LinearToDb((float)linear);
    }

    public void Play(string name, float volumeDb = 0f)
    {
        if (!_players.TryGetValue(name, out var player)) return;
        player.VolumeDb = volumeDb;
        player.Play();
    }
}
