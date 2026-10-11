using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using RoR2;
using UnityEngine;
// RoR2 declares its own Path type, which shadows System.IO.Path once RoR2 is imported.
using Path = System.IO.Path;

namespace KrisMod.Modules
{
    /// <summary>
    /// Kris's sound clips.
    ///
    /// Loose .wav files under <c>[plugin folder]/Assets/Sounds/&lt;category&gt;/</c>. Folder names
    /// are the categories, nesting included, so <c>Sounds/Special/Transform</c> is the category
    /// "Special/Transform". A category with several files picks one at random each time.
    ///
    /// Playback is the awkward part. Risk of Rain 2 ships with **Unity's audio engine disabled**
    /// - it runs entirely on Wwise - so every <see cref="AudioSource"/> is silent no matter how
    /// it is configured (<c>AudioSettings.outputSampleRate</c> reads 0). So:
    ///
    ///   1. Try to wake Unity's audio system up. If that works, clips play through a real
    ///      AudioSource with 3D positioning and volume control.
    ///   2. Otherwise fall back to the Windows waveform API, which goes straight to the OS and
    ///      ignores Unity entirely. Flat, one clip at a time, and local-player only - but
    ///      audible, and it needs nothing from Wwise.
    ///
    /// The proper third option is a Wwise soundbank through R2API.SoundAPI, which would need the
    /// clips built into a .bnk with Wwise Authoring.
    /// </summary>
    internal static class Sounds
    {
        // Folder names under Assets/Sounds. Empty or missing folders are simply silent.
        internal const string Primary = "Primary";
        internal const string PrimaryOccasional = "Primary/Occasional";
        internal const string Secondary = "Secondary";
        internal const string Utility = "Utility";
        internal const string UtilityPull = "Utility/Pull";
        internal const string UtilityHurt = "Utility/Hurt";
        internal const string Special = "Special";
        internal const string ActSelect = "Special/Select";
        internal const string ActItem = "Special/Item";
        internal const string ActDeny = "Special/Deny";
        internal const string ActBasic = "Special/Basic";
        internal const string XSlash = "Special/XSlash";
        internal const string Pirouette = "Special/Pirouette";
        internal const string PetalFeather = "Special/PetalFeather";
        internal const string SoulShine = "Special/Soul";
        internal const string Proceed = "Special/Proceed";
        internal const string Selected = "Selected";
        internal const string SelectedLook = "Selected/Look";

        private const float MinDistance = 12f;
        private const float MaxDistance = 110f;
        private const float SpatialBlend = 0.55f;

        private sealed class Clip
        {
            internal AudioClip audioClip;
            internal string filePath;

            // Decoded once so the Windows path can re-encode at whatever volume is configured.
            internal float[] samples;
            internal int channels;
            internal int sampleRate;

            // Seconds until the last audible moment, trailing silence cut off - see MeasureAudible.
            internal float audibleLength;

            // A pinned WAV image at builtVolume, whose samples waveOut plays in place.
            internal GCHandle pinnedWav;
            internal IntPtr wavPointer;
            internal float builtVolume = -1f;
        }

        private static readonly Dictionary<string, List<Clip>> Categories =
            new Dictionary<string, List<Clip>>(StringComparer.OrdinalIgnoreCase);

        private static bool unityAudioUsable;
        private static bool audioChecked;
        private static int cachedSampleRate = -1;

        // ---------------------------------------------------------------------
        // Windows waveform fallback: waveOut, not PlaySound.
        //
        // PlaySound was simpler and could only start and stop, so a line spoken just before the
        // pause menu opened carried on over it. The game pauses its own audio there (Wwise
        // Pause_All) and picks it up on unpause, and waveOutPause/waveOutRestart are the same
        // thing for this path. It keeps what PlaySound gave: one line at a time, straight to the
        // default output device, nothing needed from Unity or Wwise.
        // ---------------------------------------------------------------------

        private const uint WaveMapper = 0xFFFFFFFF;
        private const uint CallbackNull = 0;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WaveFormat
        {
            internal ushort formatTag;
            internal ushort channels;
            internal uint samplesPerSec;
            internal uint avgBytesPerSec;
            internal ushort blockAlign;
            internal ushort bitsPerSample;
            internal ushort extraSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            internal IntPtr data;
            internal uint bufferLength;
            internal uint bytesRecorded;
            internal IntPtr user;
            internal uint flags;
            internal uint loops;
            internal IntPtr next;
            internal IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr device, uint deviceId, ref WaveFormat format,
                                              IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern int waveOutPause(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveOutRestart(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);

        /// <summary>The open output device, or zero. At most one line plays at a time.</summary>
        private static IntPtr waveDevice;

        /// <summary>
        /// The playing line's header, in unmanaged memory: winmm writes its progress flags into it
        /// for as long as the line plays, so it cannot be a managed struct the GC may move.
        /// </summary>
        private static IntPtr waveHeader;

        private static readonly uint WaveHeaderSize = (uint)Marshal.SizeOf(typeof(WaveHeader));

        internal static void Init()
        {
            string root = FindSoundsFolder();
            if (root == null)
            {
                Log.Info("No Sounds folder found - Kris will be silent. Expected " +
                         "[plugin folder]/Assets/Sounds/<category>/*.wav");
                return;
            }

            int loaded = 0;
            foreach (string file in Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories))
            {
                string category = CategoryOf(root, file);

                // The AudioClip is only needed for the Unity path, but the decoded samples are
                // what the Windows path re-encodes at the configured volume, so every file is
                // decoded up front - which also reports a bad file at startup rather than mid-run.
                Clip clip = LoadWav(file);
                if (clip == null) continue;
                clip.audibleLength = MeasureAudible(clip);

                if (!Categories.TryGetValue(category, out List<Clip> list))
                {
                    list = new List<Clip>();
                    Categories[category] = list;
                }
                list.Add(clip);
                loaded++;
            }

            if (loaded == 0)
            {
                Log.Warning("Sounds folder found at " + root + " but no .wav decoded.");
                return;
            }

            // In name order, so a clip's index means the same file on every machine. A taunt picks
            // its line on the owner's machine and sends only the index (see PickIndex), and the
            // order Directory.GetFiles hands files back in is not promised to be anything.
            foreach (List<Clip> list in Categories.Values)
            {
                list.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a.filePath),
                                                          Path.GetFileName(b.filePath)));
            }

            var summary = new List<string>();
            foreach (var pair in Categories) summary.Add(pair.Key + " x" + pair.Value.Count);
            Log.Info("Loaded " + loaded + " voice clips: " + string.Join(", ", summary.ToArray()));

            // The same two events the game pauses and resumes its own Wwise audio on.
            PauseManager.onPauseStartGlobal += OnPauseStart;
            PauseManager.onPauseEndGlobal += OnPauseEnd;
        }

        /// <summary>
        /// Holds a line mid-word while the game is paused, as the game does with everything it
        /// plays itself. Only a pause the game actually takes counts - in multiplayer the menu
        /// opens without pausing, and so does not raise these.
        /// </summary>
        private static void OnPauseStart()
        {
            if (unityAudioUsable) AudioListener.pause = true;
            if (waveDevice != IntPtr.Zero) waveOutPause(waveDevice);
        }

        private static void OnPauseEnd()
        {
            if (unityAudioUsable) AudioListener.pause = false;
            if (waveDevice != IntPtr.Zero) waveOutRestart(waveDevice);
        }

        private static string FindSoundsFolder()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;

                string[] candidates =
                {
                    Path.Combine(Path.Combine(dir, "Assets"), "Sounds"),
                    Path.Combine(dir, "Sounds"),
                };

                foreach (string candidate in candidates)
                {
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not locate the Sounds folder: " + e.Message);
            }

            return null;
        }

        /// <summary>Relative folder path, forward-slashed: "Special/Transform".</summary>
        private static string CategoryOf(string root, string file)
        {
            string relative = Path.GetDirectoryName(file).Substring(root.Length).Trim('\\', '/');
            return relative.Replace('\\', '/');
        }

        private static Clip Pick(string category)
        {
            if (string.IsNullOrEmpty(category)) return null;
            if (!Categories.TryGetValue(category, out List<Clip> list) || list.Count == 0) return null;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        /// <summary>
        /// Plays for a character. Entity states run on every client, so this is called once per
        /// client for the same activation - no networking needed.
        /// </summary>
        internal static void PlayAt(string category, GameObject target)
        {
            PlayClipAt(Pick(category), target);
        }

        /// <summary>
        /// A random line out of <paramref name="category"/>, as an index for
        /// <see cref="PlayAt(string, int, GameObject)"/>, or -1 when the folder is empty.
        ///
        /// For a state whose length depends on WHICH line it says - a taunt lasts as long as its
        /// voice line. Each client picking its own, the way <see cref="PlayAt(string, GameObject)"/>
        /// does, would have the owner's machine end the pose on one line's timing while another
        /// player heard a different line cut off or trailing into silence. So the owner picks, the
        /// state networks the index, and everyone plays the same file.
        /// </summary>
        internal static int PickIndex(string category)
        {
            if (string.IsNullOrEmpty(category)) return -1;
            if (!Categories.TryGetValue(category, out List<Clip> list) || list.Count == 0) return -1;
            return UnityEngine.Random.Range(0, list.Count);
        }

        /// <summary>
        /// How long line <paramref name="index"/> of <paramref name="category"/> is actually
        /// heard for, in seconds - the file's trailing silence left out. 0 when there is no such line.
        /// </summary>
        internal static float AudibleLength(string category, int index)
        {
            Clip clip = At(category, index);
            return clip != null ? clip.audibleLength : 0f;
        }

        /// <summary>
        /// Whether two lines can be heard at once. Not on the Windows path, where a new line cuts
        /// off the one playing - so a state with two lines close together lets the first finish
        /// there before starting the second.
        /// </summary>
        internal static bool Overlaps
        {
            get
            {
                EnsureAudioChecked();
                return unityAudioUsable;
            }
        }

        /// <summary>
        /// Plays line <paramref name="index"/> of <paramref name="category"/> - see
        /// <see cref="PickIndex"/> - and hands back the line so it can be cut off with
        /// <see cref="Stop"/>. Null when nothing played: no such line, muted, or somebody else's
        /// body on the Windows path.
        /// </summary>
        internal static Voice PlayAt(string category, int index, GameObject target)
        {
            return PlayClipAt(At(category, index), target);
        }

        /// <summary>
        /// A line that has been started, for whatever started it to cut off. Opaque outside this
        /// class; only <see cref="Stop"/> reads it.
        /// </summary>
        internal sealed class Voice
        {
            /// <summary>The AudioSource's holder, on the Unity path.</summary>
            internal GameObject source;

            /// <summary>Which waveOut line this was, on the Windows path; 0 for none.</summary>
            internal int windowsLine;
        }

        /// <summary>
        /// Cuts a line off, if it is still playing. For a state whose voice belongs to it - a taunt
        /// that is walked out of stops talking.
        ///
        /// On the Windows path this only stops the device when the line on it is still THIS one.
        /// waveOut holds one line at a time, and whatever has started since - the Jarona that
        /// cancelled the taunt shouting its own - is not this caller's to silence.
        /// </summary>
        internal static void Stop(Voice voice)
        {
            if (voice == null) return;

            if (voice.source != null)
            {
                UnityEngine.Object.Destroy(voice.source);
                voice.source = null;
            }

            if (voice.windowsLine != 0 && voice.windowsLine == windowsLine) StopWindows();
            voice.windowsLine = 0;
        }

        /// <summary>
        /// The line most recently handed to waveOut, numbered from 1, so <see cref="Stop"/> can
        /// tell whether the device is still playing the one it was asked about.
        /// </summary>
        private static int windowsLine;

        private static Clip At(string category, int index)
        {
            if (string.IsNullOrEmpty(category) || index < 0) return null;
            if (!Categories.TryGetValue(category, out List<Clip> list) || index >= list.Count) return null;
            return list[index];
        }

        /// <summary>
        /// When a line goes quiet: the end of the last 20ms stretch louder than 1% of full scale.
        ///
        /// The recordings carry anything up to a second of silence after the last word - Taunt2.wav
        /// is 1.9s long and silent after 1.3 - and a pose timed to the file would stand there saying
        /// nothing for that long. Measured on the decoded samples, so it does not care how loud the
        /// player has the voice turned up, or whether it is muted.
        /// </summary>
        private static float MeasureAudible(Clip clip)
        {
            if (clip.samples == null || clip.channels <= 0 || clip.sampleRate <= 0) return 0f;

            const float Threshold = 0.01f;
            int window = Mathf.Max(1, clip.sampleRate / 50) * clip.channels;
            int windows = clip.samples.Length / window;

            for (int w = windows - 1; w >= 0; w--)
            {
                double sum = 0.0;
                for (int i = w * window, end = i + window; i < end; i++)
                {
                    sum += clip.samples[i] * clip.samples[i];
                }
                if (Math.Sqrt(sum / window) > Threshold)
                {
                    return (float)(w + 1) * window / clip.channels / clip.sampleRate;
                }
            }
            return 0f;
        }

        private static Voice PlayClipAt(Clip clip, GameObject target)
        {
            if (target == null || clip == null) return null;

            float volume = Volume();
            if (volume <= 0.001f) return null;

            EnsureAudioChecked();

            if (unityAudioUsable)
            {
                return new Voice { source = PlayThroughUnity(clip, target, SpatialBlend, volume) };
            }

            // The Windows fallback has no positioning and cuts off whatever was playing, so
            // restrict it to the character this player is actually controlling.
            if (IsLocalPlayerBody(target) && PlayThroughWindows(clip, volume))
            {
                return new Voice { windowsLine = windowsLine };
            }
            return null;
        }

        /// <summary>Plays flat, for menus.</summary>
        internal static void PlayUI(string category)
        {
            Clip clip = Pick(category);
            if (clip == null) return;

            float volume = Volume();
            if (volume <= 0.001f) return;

            EnsureAudioChecked();

            if (unityAudioUsable)
            {
                StopUI();
                uiVoice = PlayThroughUnity(clip, null, 0f, volume);
            }
            else
            {
                PlayThroughWindows(clip, volume);
            }
        }

        /// <summary>The menu line playing through Unity audio, so it can be cut off.</summary>
        private static GameObject uiVoice;

        /// <summary>
        /// Cuts off a menu line. Only ever called from the menus, where the only thing playing
        /// is what <see cref="PlayUI"/> started - the Windows path holds one line at a time, and
        /// no character a player controls exists to have started another.
        /// </summary>
        internal static void StopUI()
        {
            if (uiVoice != null)
            {
                UnityEngine.Object.Destroy(uiVoice);
                uiVoice = null;
            }
            StopWindows();
        }

        // ---------------------------------------------------------------------
        // Volume: the mod's own setting under the game's.
        // ---------------------------------------------------------------------

        /// <summary>
        /// How loud a clip plays, 0 to 1: the mod's Voice Clip Volume, times the game's master and
        /// SFX sliders, and 0 while the game is muting itself for having lost focus.
        ///
        /// The game applies all of those inside Wwise, and these clips never go through Wwise -
        /// waveOut hands them straight to Windows - so without this the audio settings menu did
        /// nothing to Kris's voice at all. Voice lines count as sound effects here because the
        /// game has no voice slider of its own.
        ///
        /// Read on every play rather than cached, so a slider moved mid-run applies to the very
        /// next line. The Windows path only re-encodes a clip when this number actually changes.
        /// </summary>
        private static float Volume()
        {
            if (MutedByFocus()) return 0f;
            return Mathf.Clamp01(KrisConfig.SoundVolume.Value) *
                   GameSlider("volume_master") * GameSlider("volume_sfx");
        }

        /// <summary>
        /// One of the game's volume sliders as linear gain, 0 to 1. The console variable holds
        /// 0-100. Wwise may map that through a curve of its own, so this matches the game's
        /// loudness closely rather than exactly - but 0 is silence and 100 is untouched either way.
        ///
        /// Parsed invariant: the game writes these with TextSerialization.ToStringInvariant, and
        /// a locale with a decimal comma would otherwise read "37.5" as 375.
        /// </summary>
        private static float GameSlider(string conVarName)
        {
            string text = ReadConVar(conVarName);
            float value;
            if (text == null || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return 1f;
            }
            return Mathf.Clamp01(value / 100f);
        }

        /// <summary>
        /// The game's "mute audio when unfocused" option (audio_focused_only). Wwise goes quiet on
        /// its own when the window loses focus; a clip handed to Windows would not.
        /// </summary>
        private static bool MutedByFocus()
        {
            return !Application.isFocused && ReadConVar("audio_focused_only") == "1";
        }

        private static readonly HashSet<string> unreadConVars = new HashSet<string>();

        /// <summary>
        /// A console variable's value, or null if the console is not up or has no such variable -
        /// in which case the clip plays as if that setting were at its default, and the log says
        /// so once per variable.
        /// </summary>
        private static string ReadConVar(string name)
        {
            try
            {
                RoR2.ConVar.BaseConVar conVar = RoR2.Console.instance != null
                    ? RoR2.Console.instance.FindConVar(name)
                    : null;
                if (conVar != null) return conVar.GetString();
            }
            catch (Exception e)
            {
                if (unreadConVars.Add(name)) Log.Warning("Could not read '" + name + "': " + e.Message);
                return null;
            }

            if (unreadConVars.Add(name))
            {
                Log.Warning("No console variable '" + name + "' - voice clips ignore that audio setting.");
            }
            return null;
        }

        private static bool IsLocalPlayerBody(GameObject target)
        {
            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser == null) return false;
            return localUser.cachedBodyObject == target;
        }

        private static GameObject PlayThroughUnity(Clip clip, GameObject target, float spatialBlend, float volume)
        {
            var holder = new GameObject("KrisVoice");
            if (target != null) holder.transform.SetParent(target.transform, false);

            AudioSource source = holder.AddComponent<AudioSource>();
            source.clip = clip.audioClip;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.minDistance = MinDistance;
            source.maxDistance = MaxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.loop = false;
            source.playOnAwake = false;
            source.Play();

            UnityEngine.Object.Destroy(holder, clip.audioClip.length + 0.5f);
            return holder;
        }

        /// <summary>
        /// waveOut has no volume control that is ours alone - its volume is the whole device's -
        /// so the clip is re-encoded with its samples scaled and handed over from memory. The
        /// encoded image is cached and only rebuilt when the volume actually changes.
        ///
        /// True when the line is playing, which is also when it gets a new <see cref="windowsLine"/>.
        /// </summary>
        private static bool PlayThroughWindows(Clip clip, float volume)
        {
            try
            {
                // Before anything is rebuilt: the playing line may be this very clip, and its
                // buffer is about to be freed.
                StopWindows();

                if (clip.wavPointer == IntPtr.Zero || !Mathf.Approximately(clip.builtVolume, volume))
                {
                    if (!BuildScaledWav(clip, volume))
                    {
                        Log.Warning("Could not encode " + Path.GetFileName(clip.filePath) + " for playback.");
                        return false;
                    }
                }

                var format = new WaveFormat
                {
                    formatTag = 1,                                   // PCM
                    channels = (ushort)clip.channels,
                    samplesPerSec = (uint)clip.sampleRate,
                    avgBytesPerSec = (uint)(clip.sampleRate * clip.channels * 2),
                    blockAlign = (ushort)(clip.channels * 2),
                    bitsPerSample = 16,
                };

                int result = waveOutOpen(out waveDevice, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
                if (result != 0)
                {
                    waveDevice = IntPtr.Zero;
                    Log.Warning("waveOutOpen failed (" + result + ") for " + Path.GetFileName(clip.filePath) + ".");
                    return false;
                }

                waveHeader = Marshal.AllocHGlobal((int)WaveHeaderSize);
                Marshal.StructureToPtr(new WaveHeader
                {
                    data = IntPtr.Add(clip.wavPointer, WavHeaderBytes),
                    bufferLength = (uint)(clip.samples.Length * 2),
                }, waveHeader, false);

                result = waveOutPrepareHeader(waveDevice, waveHeader, WaveHeaderSize);
                if (result == 0) result = waveOutWrite(waveDevice, waveHeader, WaveHeaderSize);
                if (result != 0)
                {
                    Log.Warning("waveOut could not play " + Path.GetFileName(clip.filePath) + " (" + result + ").");
                    StopWindows();
                    return false;
                }

                // A line can only start mid-pause from a menu, but if it does, it waits too.
                if (PauseManager.isPaused) waveOutPause(waveDevice);

                windowsLine++;
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("Windows playback failed for " + Path.GetFileName(clip.filePath) +
                            ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Cuts off the playing line, if any, and releases the device and its header. A finished
        /// line keeps both until the next one starts, which is harmless and saves tracking when
        /// it ended.
        /// </summary>
        private static void StopWindows()
        {
            if (waveDevice != IntPtr.Zero)
            {
                waveOutReset(waveDevice);
                if (waveHeader != IntPtr.Zero) waveOutUnprepareHeader(waveDevice, waveHeader, WaveHeaderSize);
                waveOutClose(waveDevice);
                waveDevice = IntPtr.Zero;
            }

            if (waveHeader != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(waveHeader);
                waveHeader = IntPtr.Zero;
            }
        }

        /// <summary>Size of the RIFF header EncodeWav writes ahead of the samples.</summary>
        private const int WavHeaderBytes = 44;

        private static bool BuildScaledWav(Clip clip, float volume)
        {
            if (clip.samples == null || clip.channels <= 0 || clip.sampleRate <= 0) return false;

            byte[] wav = EncodeWav(clip.samples, clip.channels, clip.sampleRate, volume);

            // waveOut reads the buffer as it plays, so it has to stay put for the lifetime of the
            // line rather than just the call.
            if (clip.pinnedWav.IsAllocated) clip.pinnedWav.Free();
            clip.pinnedWav = GCHandle.Alloc(wav, GCHandleType.Pinned);
            clip.wavPointer = clip.pinnedWav.AddrOfPinnedObject();
            clip.builtVolume = volume;
            return true;
        }

        /// <summary>Writes 16-bit PCM, which every waveOut device accepts.</summary>
        private static byte[] EncodeWav(float[] samples, int channels, int sampleRate, float volume)
        {
            int dataBytes = samples.Length * 2;
            var wav = new byte[44 + dataBytes];

            void WriteAscii(int offset, string text)
            {
                for (int i = 0; i < text.Length; i++) wav[offset + i] = (byte)text[i];
            }

            WriteAscii(0, "RIFF");
            BitConverter.GetBytes(36 + dataBytes).CopyTo(wav, 4);
            WriteAscii(8, "WAVE");
            WriteAscii(12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(wav, 16);                       // fmt chunk size
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);                 // PCM
            BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * channels * 2).CopyTo(wav, 28); // byte rate
            BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);     // block align
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);                 // bits per sample
            WriteAscii(36, "data");
            BitConverter.GetBytes(dataBytes).CopyTo(wav, 40);

            for (int i = 0; i < samples.Length; i++)
            {
                float scaled = Mathf.Clamp(samples[i] * volume, -1f, 1f);
                short value = (short)Mathf.RoundToInt(scaled * 32767f);
                wav[44 + i * 2] = (byte)(value & 0xFF);
                wav[45 + i * 2] = (byte)((value >> 8) & 0xFF);
            }

            return wav;
        }

        // ---------------------------------------------------------------------
        // Deciding which playback path to use.
        // ---------------------------------------------------------------------

        private static void EnsureAudioChecked()
        {
            if (audioChecked) return;
            audioChecked = true;

            unityAudioUsable = TryWakeUnityAudio();

            Log.Info(unityAudioUsable
                ? "Voice clips will play through Unity audio (sample rate " + cachedSampleRate + ")."
                : "Unity audio is disabled in this build, so voice clips play through the Windows " +
                  "waveform API instead: no 3D positioning, one clip at a time, and only for the " +
                  "character you are controlling.");

            if (unityAudioUsable) EnsureListener();
        }

        /// <summary>
        /// RoR2 ships with Unity audio switched off. Resetting the audio configuration sometimes
        /// brings it back; when it does not, <c>outputSampleRate</c> stays at 0 and there is
        /// nothing more to try.
        /// </summary>
        private static bool TryWakeUnityAudio()
        {
            if (SampleRate() > 0) return true;

            try
            {
                AudioConfiguration config = AudioSettings.GetConfiguration();
                if (config.sampleRate <= 0) config.sampleRate = 48000;
                if (config.numRealVoices <= 0) config.numRealVoices = 32;
                if (config.numVirtualVoices <= 0) config.numVirtualVoices = 128;
                if (config.dspBufferSize <= 0) config.dspBufferSize = 1024;
                config.speakerMode = AudioSpeakerMode.Stereo;

                AudioSettings.Reset(config);
            }
            catch (Exception e)
            {
                Log.Warning("Could not reset Unity audio: " + e.Message);
                return false;
            }

            cachedSampleRate = -1;
            return SampleRate() > 0;
        }

        /// <summary>
        /// Reads the output sample rate at most once per wake attempt. Unity logs an error every
        /// single time this is queried while its audio system is off, so caching keeps one
        /// unavoidable line in the log instead of several.
        /// </summary>
        private static int SampleRate()
        {
            if (cachedSampleRate >= 0) return cachedSampleRate;

            try { cachedSampleRate = AudioSettings.outputSampleRate; }
            catch { cachedSampleRate = 0; }

            return cachedSampleRate;
        }

        private static void EnsureListener()
        {
            if (UnityEngine.Object.FindObjectOfType<AudioListener>() != null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            camera.gameObject.AddComponent<AudioListener>();
            Log.Info("Added a Unity AudioListener to the camera so voice clips can be heard.");
        }

        /// <summary>
        /// Logs whether Unity's audio path is usable at all. Called once at startup so a silent
        /// mod reports why without the player having to trigger a clip first.
        /// </summary>
        internal static void ReportAudioState()
        {
            EnsureAudioChecked();
        }

        // ---------------------------------------------------------------------
        // Minimal RIFF/WAVE decoder, used for the Unity path and to validate files
        // at startup rather than failing silently mid-run.
        // ---------------------------------------------------------------------

        private static Clip LoadWav(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var clip = new Clip { filePath = path };

                clip.audioClip = DecodeWav(bytes, Path.GetFileNameWithoutExtension(path), clip);
                return clip.audioClip != null ? clip : null;
            }
            catch (Exception e)
            {
                Log.Warning("Failed to load " + Path.GetFileName(path) + ": " + e.Message);
                return null;
            }
        }

        private static AudioClip DecodeWav(byte[] bytes, string name, Clip destination)
        {
            if (bytes.Length < 12 ||
                bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
                bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            {
                Log.Warning(name + " is not a RIFF/WAVE file.");
                return null;
            }

            int format = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
            int dataOffset = -1, dataLength = 0;

            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
                int chunkSize = BitConverter.ToInt32(bytes, position + 4);
                int chunkBody = position + 8;

                if (chunkSize < 0 || chunkBody + chunkSize > bytes.Length)
                {
                    chunkSize = bytes.Length - chunkBody;
                }

                if (chunkId == "fmt ")
                {
                    format = BitConverter.ToUInt16(bytes, chunkBody);
                    channels = BitConverter.ToUInt16(bytes, chunkBody + 2);
                    sampleRate = BitConverter.ToInt32(bytes, chunkBody + 4);
                    bitsPerSample = BitConverter.ToUInt16(bytes, chunkBody + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkBody;
                    dataLength = chunkSize;
                }

                // Chunks are word-aligned.
                position = chunkBody + chunkSize + (chunkSize % 2);
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            {
                Log.Warning(name + " has no usable fmt/data chunks.");
                return null;
            }

            // 1 = PCM, 3 = IEEE float, 0xFFFE = extensible (the subformat is almost always PCM).
            if (format != 1 && format != 3 && format != 0xFFFE)
            {
                Log.Warning(name + " uses compressed WAV format " + format + "; re-export it as PCM.");
                return null;
            }

            float[] samples = ToFloatSamples(bytes, dataOffset, dataLength, bitsPerSample, format);
            if (samples == null || samples.Length == 0)
            {
                Log.Warning(name + " has " + bitsPerSample + "-bit samples, which are not supported.");
                return null;
            }

            destination.samples = samples;
            destination.channels = channels;
            destination.sampleRate = sampleRate;

            AudioClip clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float[] ToFloatSamples(byte[] bytes, int offset, int length, int bits, int format)
        {
            if (format == 3 && bits == 32)
            {
                var floats = new float[length / 4];
                for (int i = 0; i < floats.Length; i++) floats[i] = BitConverter.ToSingle(bytes, offset + i * 4);
                return floats;
            }

            switch (bits)
            {
                case 8:
                {
                    var samples = new float[length];
                    for (int i = 0; i < length; i++) samples[i] = (bytes[offset + i] - 128) / 128f;
                    return samples;
                }
                case 16:
                {
                    var samples = new float[length / 2];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] = BitConverter.ToInt16(bytes, offset + i * 2) / 32768f;
                    }
                    return samples;
                }
                case 24:
                {
                    var samples = new float[length / 3];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int b = offset + i * 3;
                        int value = (bytes[b] << 8) | (bytes[b + 1] << 16) | (bytes[b + 2] << 24);
                        samples[i] = (value >> 8) / 8388608f;
                    }
                    return samples;
                }
                case 32:
                {
                    var samples = new float[length / 4];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] = BitConverter.ToInt32(bytes, offset + i * 4) / 2147483648f;
                    }
                    return samples;
                }
                default:
                    return null;
            }
        }
    }
}
