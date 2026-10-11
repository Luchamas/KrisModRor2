using BepInEx;
using KrisMod.Content;
using KrisMod.Modules;
using R2API.Utils;

namespace KrisMod
{
    /// <summary>
    /// Adds Kris, the human hero of DELTARUNE, as a playable survivor.
    /// </summary>
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.content_management", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.prefab", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.language", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.recalculatestats", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.networking", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.bepis.r2api.skills", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInPlugin(Guid, ModName, Version)]
    public class KrisPlugin : BaseUnityPlugin
    {
        public const string Author = "deltarune";
        public const string ModName = "KrisMod";
        public const string Guid = "com." + Author + "." + ModName;
        public const string Version = "0.1.0";

        public static KrisPlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;

            Log.Init(Logger);
            Log.Info("Kris is waking up.");

            KrisConfig.Init(Config);

            // Order matters: icons come from Assets, buffs use those icons, skills reference both,
            // and the body wires the finished skills onto the prefab.
            KrisAssets.Init();
            Sounds.Init();
            Tokens.Init();
            Buffs.Init();
            KrisSkills.RegisterEntityStates();
            KrisBody.Init();
            KrisDiagnostics.Init();
            Components.KrisMenuSounds.Spawn();

            Log.Info("Kris is ready. It's what they call \"you.\"");
        }
    }
}
