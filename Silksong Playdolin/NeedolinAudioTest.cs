using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;

// Instrument prototype: owns hero control and temporarily disables inventory input.
public sealed class NeedolinAudioTest : MonoBehaviour
{
    private readonly KeyCode[][] keys = {
        new[] { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I },
        new[] { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J },
        new[] { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M }
    };
    private readonly int[] semitones = { 0, 2, 4, 5, 7, 9, 11, 12 };
    // Shift selects the five black-key pitches; E/B and the top C remain natural.
    private readonly int[] shiftedSemitones = { 1, 3, 4, 6, 8, 10, 11, 12 };
    private readonly string[] chromaticNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private readonly int[] bankOffsets = { 24, 12, 0 };
    private readonly AudioSource[] voices = new AudioSource[16];
    private ManualLogSource log;
    private readonly AudioClip[] samples = new AudioClip[37];
    private bool testEnabled;
    private bool inputBlocked;
    private HeroController lockedHero;
    private int ownedControlVersion;
    private PlayMakerFSM lockedInventory;
    private bool inventoryWasEnabled;
    private bool ownsAnimation;
    private int ownedAnimationVersion;
    private int nextVoice;
    private readonly AudioClip[] dryClips = new AudioClip[37];
    private readonly float[][] dryBanks = new float[37][];
    private readonly int[] sampleRates = new int[37];
    private readonly int[] sampleChannels = new int[37];
    private ConfigEntry<bool> reverbEnabled;
    private ConfigEntry<float> reverbDecay;
    private ConfigEntry<float> reverbLevel;
    private ConfigEntry<KeyCode> toggleKey;
    private ConfigEntry<float> instrumentVolume;
    private ConfigEntry<bool> playingAnimation;

    private void Awake()
    {
        log = BepInEx.Logging.Logger.CreateLogSource("Needolin Audio Test");
        try
        {
            for (int note = 0; note < dryClips.Length; note++)
            {
                string noteName = chromaticNames[note % 12] + (3 + note / 12);
                dryClips[note] = LoadEmbeddedSample("Playdolin." + noteName + ".wav",
                    out dryBanks[note], out sampleRates[note], out sampleChannels[note]);
            }
            Array.Copy(dryClips, samples, samples.Length);
            var config = GetComponent<PlaydolinPlugin>().Config;
            toggleKey = config.Bind("Instrument", "ToggleKey", KeyCode.F9, "Key to enter or leave free play.");
            instrumentVolume = config.Bind("Instrument", "Volume", 0.7f,
                new ConfigDescription("Instrument volume from 0 to 1.", new AcceptableValueRange<float>(0f, 1f)));
            // Upgrade the old default once, while retaining custom volume settings.
            var volumeDefaultUpdated = config.Bind("Instrument", "VolumeDefaultUpdated", false,
                "Internal marker for the one-time volume default upgrade.");
            if (!volumeDefaultUpdated.Value)
            {
                if (Mathf.Approximately(instrumentVolume.Value, 0.35f))
                    instrumentVolume.Value = 0.7f;
                volumeDefaultUpdated.Value = true;
            }
            playingAnimation = config.Bind("Instrument", "PlayingAnimation", true, "Use the Needolin Play pose during free play.");
            instrumentVolume.SettingChanged += OnVolumeChanged;
            reverbEnabled = config.Bind("Reverb", "Enabled", true, "Enable reverb on the mod's instrument.");
            reverbDecay = config.Bind("Reverb", "DecaySeconds", 2.2f,
                new ConfigDescription("Reverb tail duration in seconds. Try 1.2 for short or 3 for spacious.",
                    new AcceptableValueRange<float>(0.1f, 8f)));
            reverbLevel = config.Bind("Reverb", "Level", -1200f,
                new ConfigDescription("Reverb level in millibels. -2000 is subtle, -1200 medium, -600 stronger. Closer to zero is louder.",
                    new AcceptableValueRange<float>(-10000f, 0f)));
            reverbEnabled.SettingChanged += OnReverbChanged;
            reverbDecay.SettingChanged += OnReverbChanged;
            reverbLevel.SettingChanged += OnReverbChanged;
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].loop = false;
                voices[i].spatialBlend = 0f;
                voices[i].volume = instrumentVolume.Value;
                voices[i].clip = samples[0];
            }
            ApplyReverb();
            log.LogInfo("Ready: F9 in gameplay; Q-I (C5-C6), A-J (C4-B4), Z-M (C3-B3). No duplicate octave-boundary notes. Menu/pause input is blocked.");
        }
        catch (Exception error)
        {
            log.LogError(error);
            enabled = false;
        }
    }

    private void Update()
    {
        HeroController hero = HeroController.instance;
        GameManager manager = GameManager.instance;
        // Use the game's scene classification; gameplay can coexist with a menu scene.
        bool outsideGame = manager == null || !manager.IsGameplayScene()
            || hero == null || !hero.gameObject.activeInHierarchy;
        bool blocked = outsideGame || manager.isPaused || Time.timeScale <= 0f
            || PlayerData.instance == null || PlayerData.instance.isInventoryOpen;
        if (testEnabled && (blocked || hero != lockedHero || lockedHero == null
            || HeroController.ControlVersion != ownedControlVersion || lockedHero.cState.dead
            || lockedHero.cState.hazardDeath || lockedHero.cState.hazardRespawning
            || lockedHero.cState.transitioning || lockedHero.cState.recoiling))
            EndPlaying();
        if (blocked)
        {
            if (!inputBlocked) StopVoices();
            inputBlocked = true;
            return;
        }
        inputBlocked = false;
        if (Input.GetKeyDown(toggleKey.Value))
        {
            if (testEnabled) EndPlaying();
            else BeginPlaying(hero, manager);
            return;
        }
        if (!testEnabled) return;
        for (int bank = 0; bank < keys.Length; bank++)
        for (int i = 0; i < keys[bank].Length; i++)
        {
            if (!Input.GetKeyDown(keys[bank][i])) continue;
            AudioSource voice = voices[nextVoice];
            for (int j = 0; j < voices.Length; j++)
            {
                int index = (nextVoice + j) % voices.Length;
                if (!voices[index].isPlaying) { voice = voices[index]; nextVoice = index; break; }
            }
            voice.Stop();
            // Preserve the existing key and Shift mapping; each pitch has its own WAV.
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int note = shiftHeld ? shiftedSemitones[i] : semitones[i];
            int sampleIndex = bankOffsets[bank] + note;
            voice.clip = samples[sampleIndex];
            // G4 and G#4 use the requested per-note volume; reset pooled voices for other notes.
            voice.volume = sampleIndex == 19 || sampleIndex == 20 ? 0.9f : instrumentVolume.Value;
            voice.pitch = 1f;
            voice.Play();
            nextVoice = (nextVoice + 1) % voices.Length;
        }
    }

    private void StopVoices()
    {
        foreach (AudioSource voice in voices) if (voice != null) voice.Stop();
    }

    private void BeginPlaying(HeroController hero, GameManager manager)
    {
        if (!hero.CanPlayNeedolin() || hero.IsInputBlocked() || PlayerData.instance.atBench)
        {
            log.LogInfo("Cannot start: stand on the ground with control available, outside the bench/inventory.");
            return;
        }
        // Inventory listens separately from movement. Disable its FSM only while
        // this mode owns control, then restore its original enabled state.
        PlayMakerFSM inventory = manager.inventoryFSM;
        if (inventory == null)
        {
            log.LogWarning("Cannot start: inventory FSM unavailable.");
            return;
        }
        lockedHero = hero;
        lockedInventory = inventory;
        inventoryWasEnabled = inventory.enabled;
        ownedControlVersion = HeroController.ControlVersion;
        try
        {
            hero.RelinquishControl();
            ownedControlVersion = HeroController.ControlVersion;
            hero.AddInputBlocker(this);
            hero.HeroLeavingScene += OnHeroLeavingScene;
            inventory.enabled = false;
            if (playingAnimation.Value)
            {
                try
                {
                    if (hero.AnimCtrl != null && hero.AnimCtrl.animator != null
                        && hero.AnimCtrl.animator.GetClipByName("Needolin Play") != null)
                    {
                        ownedAnimationVersion = hero.StopAnimationControlVersioned();
                        ownsAnimation = true;
                        // PlayClip ignores requests when controlEnabled is false.
                        // Forced playback is needed after StopAnimationControl.
                        hero.AnimCtrl.PlayClipForced("Needolin Play");
                        log.LogInfo("Playing pose: Needolin Play (forced playback).");
                    }
                    else log.LogWarning("Needolin Play animation unavailable; audio mode remains usable.");
                }
                catch (Exception error)
                {
                    if (ownsAnimation) hero.StartAnimationControl(ownedAnimationVersion);
                    ownsAnimation = false;
                    log.LogWarning("Could not set playing pose; audio mode remains usable. " + error);
                }
            }
            testEnabled = true;
            log.LogInfo("Instrument ON: Hornet control locked; inventory blocked. " + toggleKey.Value + " exits.");
        }
        catch (Exception error)
        {
            EndPlaying();
            log.LogError(error);
        }
    }

    private void OnHeroLeavingScene() { EndPlaying(); }

    private void EndPlaying()
    {
        testEnabled = false;
        StopVoices();
        HeroController hero = lockedHero;
        lockedHero = null;
        try
        {
            if (hero != null)
            {
                hero.HeroLeavingScene -= OnHeroLeavingScene;
                hero.RemoveInputBlocker(this);
                if (ownsAnimation)
                {
                    try
                    {
                    bool normalExit = HeroController.ControlVersion == ownedControlVersion
                        && !hero.cState.dead && !hero.cState.hazardDeath && !hero.cState.hazardRespawning
                        && !hero.cState.transitioning && !hero.cState.recoiling;
                    if (hero.AnimationControlVersion == ownedAnimationVersion)
                    {
                        try
                        {
                            if (normalExit && hero.AnimCtrl != null) hero.AnimCtrl.PlayClipForced("Idle");
                        }
                        finally { hero.StartAnimationControl(ownedAnimationVersion); }
                    }
                    }
                    catch (Exception error) { log.LogWarning("Could not restore animation: " + error); }
                }
                // Don't unlock control claimed by a cutscene, death or another system.
                if (HeroController.ControlVersion == ownedControlVersion && !hero.cState.dead
                    && !hero.cState.hazardDeath && !hero.cState.hazardRespawning
                    && !hero.cState.transitioning)
                    hero.RegainControl();
            }
        }
        finally
        {
            ownsAnimation = false;
            if (lockedInventory != null) lockedInventory.enabled = inventoryWasEnabled;
            lockedInventory = null;
        }
        if (log != null) log.LogInfo("Instrument OFF: released owned control and inventory lock.");
    }

    private void OnReverbChanged(object sender, EventArgs args) { ApplyReverb(); }

    private void OnVolumeChanged(object sender, EventArgs args)
    {
        foreach (AudioSource voice in voices) if (voice != null) voice.volume = instrumentVolume.Value;
    }

    private void ApplyReverb()
    {
        StopVoices();
        foreach (AudioSource voice in voices) if (voice != null) voice.clip = null;
        for (int bank = 0; bank < samples.Length; bank++) ApplyBankReverb(bank);
    }

    private void ApplyBankReverb(int bank)
    {
        AudioClip dryClip = dryClips[bank];
        float[] drySamples = dryBanks[bank];
        int sampleRate = sampleRates[bank];
        int channels = sampleChannels[bank];
        AudioClip replacement = dryClip;
        try
        {
            if (reverbEnabled.Value)
            {
                float decay = Mathf.Clamp(reverbDecay.Value, 0.1f, 8f);
                float wet = Mathf.Pow(10f, Mathf.Clamp(reverbLevel.Value, -10000f, 0f) / 2000f);
                float[] output = new float[drySamples.Length + (int)(sampleRate * decay * 1.5f) * channels];
                Array.Copy(drySamples, output, drySamples.Length);
                // Parallel damped feedback delays, baked once into the sample.
                // No native Unity audio filter is required at runtime.
                float[] delays = { 0.0437f, 0.0533f, 0.0617f, 0.0719f };
                foreach (float delay in delays)
                {
                    int length = (int)(sampleRate * delay);
                    for (int channel = 0; channel < channels; channel++)
                    {
                    float[] buffer = new float[length];
                    float feedback = Mathf.Pow(0.001f, delay / decay);
                    float damped = 0f;
                    for (int i = channel; i < output.Length; i += channels)
                    {
                        int slot = (i / channels) % length;
                        float echo = buffer[slot];
                        damped += 0.55f * (echo - damped);
                        buffer[slot] = (i < drySamples.Length ? drySamples[i] : 0f) + damped * feedback;
                        output[i] += echo * wet * 0.4f;
                    }
                    }
                }
                // Fade the end and keep individual samples below clipping.
                int fade = Math.Min(sampleRate / 20, output.Length / channels);
                for (int i = 0; i < fade; i++)
                    for (int channel = 0; channel < channels; channel++)
                        output[output.Length - fade * channels + i * channels + channel] *= 1f - i / (float)fade;
                float peak = 0f;
                foreach (float value in output) peak = Mathf.Max(peak, Mathf.Abs(value));
                if (peak > 0.95f)
                    for (int i = 0; i < output.Length; i++) output[i] *= 0.95f / peak;
                replacement = CreateClip("Needolin with reverb", output, sampleRate, channels);
            }
        }
        catch (Exception error)
        {
            log.LogWarning("Reverb failed; using dry audio. " + error);
        }
        AudioClip previous = samples[bank];
        samples[bank] = replacement;
        if (previous != null && previous != dryClip && previous != replacement) Destroy(previous);
        log.LogInfo(chromaticNames[bank % 12] + (3 + bank / 12) + " audio ready: " + (replacement == dryClip ? "dry" : "software reverb"));
    }

    private void OnDisable() { EndPlaying(); }

    private void OnDestroy()
    {
        EndPlaying();
        if (instrumentVolume != null) instrumentVolume.SettingChanged -= OnVolumeChanged;
        if (reverbEnabled != null) reverbEnabled.SettingChanged -= OnReverbChanged;
        if (reverbDecay != null) reverbDecay.SettingChanged -= OnReverbChanged;
        if (reverbLevel != null) reverbLevel.SettingChanged -= OnReverbChanged;
        foreach (AudioSource voice in voices) if (voice != null) Destroy(voice);
        for (int bank = 0; bank < samples.Length; bank++)
        {
            if (samples[bank] != null && samples[bank] != dryClips[bank]) Destroy(samples[bank]);
            if (dryClips[bank] != null) Destroy(dryClips[bank]);
        }
        if (log != null) BepInEx.Logging.Logger.Sources.Remove(log);
    }

    private static AudioClip LoadEmbeddedSample(string resourceName, out float[] dryData, out int sampleRate, out int sampleChannelCount)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (stream == null) throw new InvalidDataException("Embedded Needolin sample missing: " + resourceName);
            using (var reader = new BinaryReader(stream))
            {
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected WAV RIFF.");
                reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE.");
                int channels = 0, rate = 0, bits = 0, format = 0;
                byte[] pcm = null;
                while (stream.Position + 8 <= stream.Length)
                {
                    string id = new string(reader.ReadChars(4));
                    int size = checked((int)reader.ReadUInt32());
                    long end = stream.Position + size;
                    if (size < 0 || end > stream.Length) throw new InvalidDataException("Invalid WAV chunk.");
                    if (id == "fmt ")
                    {
                        format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                        rate = reader.ReadInt32(); reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                    }
                    else if (id == "data") pcm = reader.ReadBytes(size);
                    stream.Position = end + (size & 1);
                }
                if (format != 1 || bits != 16 || (channels != 1 && channels != 2) || rate <= 0 || pcm == null || pcm.Length == 0 || pcm.Length % (channels * 2) != 0)
                    throw new InvalidDataException("Expected mono or stereo 16-bit PCM sample.");
                float[] samples = new float[pcm.Length / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
                dryData = samples;
                sampleRate = rate;
                sampleChannelCount = channels;
                return CreateClip(resourceName, samples, rate, channels);
            }
        }
    }

    private static AudioClip CreateClip(string name, float[] samples, int rate, int channels)
    {
                AudioClip clip = AudioClip.Create(name, samples.Length / channels, channels, rate, false);
                // Select the float[] overload explicitly: Unity also exposes a Span
                // overload that .NET Framework reference assemblies cannot resolve.
                MethodInfo setData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
                if (setData == null || !(bool)setData.Invoke(clip, new object[] { samples, 0 }))
                { Destroy(clip); throw new InvalidDataException("Cannot load sample into AudioClip."); }
                return clip;
    }
}
