using BepInEx;
using KrisMod.Content;
using KrisMod.Modules;
using R2API.Utils;

namespace KrisMod
{
    /// <summary>
    /// Adds Kris, the human hero of DELTARUNE, as a playable survivor.
    ///
    /// ⚠ The R2API modules below are the same list as the dependencies in Build/manifest.json,
    /// which is what Thunderstore and the mod managers actually install. Change both: a module
    /// only named here is never installed, and a player without it never loads Kris. Only what the
    /// code uses belongs in either - Core (NetworkCompatibility), ContentManagement
    /// (ContentAddition), Prefab, Language and RecalculateStats.
    /// </summary>
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.content_management", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.prefab", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.language", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.bepis.r2api.recalculatestats", BepInDependency.DependencyFlags.HardDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInPlugin(Guid, ModName, Version)]
    public class KrisPlugin : BaseUnityPlugin
    {
        public const string Author = "deltarune";
        public const string ModName = "KrisMod";
        public const string Guid = "com." + Author + "." + ModName;
        public const string Version = "1.0.2";

        public static KrisPlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;

            Log.Init(Logger);
            Log.Info("Kris is waking up.");

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
