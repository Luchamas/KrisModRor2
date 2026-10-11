using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace KrisMod.Modules
{
    /// <summary>
    /// Kris's default skin, built in code.
    ///
    /// Until now they wore Loader's ModelSkinController and therefore Loader's skins, which named
    /// Loader's renderers: every time one was applied it pointed CharacterModel back at Loader's
    /// meshes and switched them on again, and KrisModelEnforcer spent its life undoing that.
    /// With Loader's rig gone those skins would point at nothing, so they get one of their own.
    ///
    /// One SkinDef serves both the body and the character-select mannequin. SkinDef bakes each
    /// renderer as a path relative to <see cref="SkinDef.rootObject"/> and resolves the paths
    /// against whatever object the ModelSkinController sits on, and both of those are an
    /// mdlKris holding the same model instance - so the paths resolve on either.
    ///
    /// SkinCatalog picks it up by itself: it gathers skins from each body prefab's
    /// ModelSkinController at init and bakes them, so nothing has to be registered.
    /// </summary>
    internal static class KrisSkin
    {
        internal static SkinDef Default;

        /// <summary>
        /// Builds the skin against <paramref name="root"/> (the body's model object) the first
        /// time, and returns the same one after that.
        /// </summary>
        internal static SkinDef Get(GameObject root, CharacterModel.RendererInfo[] renderers)
        {
            if (Default != null) return Default;
            if (root == null || renderers == null) return null;

            var parameters = ScriptableObject.CreateInstance<SkinDefParams>();
            parameters.name = "skinKrisDefaultParams";
            parameters.rendererInfos = renderers;
            // Baking walks every one of these without a null check.
            parameters.gameObjectActivations = new SkinDefParams.GameObjectActivation[0];
            parameters.meshReplacements = new SkinDefParams.MeshReplacement[0];
            parameters.projectileGhostReplacements = new SkinDefParams.ProjectileGhostReplacement[0];
            parameters.minionSkinReplacements = new SkinDefParams.MinionSkinReplacement[0];
            parameters.lightReplacements = new CharacterModel.LightInfo[0];

            var skin = ScriptableObject.CreateInstance<SkinDef>();
            skin.name = "skinKrisDefault";
            skin.nameToken = "DEFAULT_SKIN";
            skin.icon = KrisAssets.IconKris;
            skin.rootObject = root;
            skin.baseSkins = new SkinDef[0];
            skin.unlockableDef = null;
            skin.skinDefParams = parameters;
            // Both addresses are read unconditionally by SkinCatalog and by baking, so they must
            // exist even though they point nowhere; an empty key is how vanilla spells "none".
            skin.skinDefParamsAddress = new AssetReferenceT<SkinDefParams>(string.Empty);
            skin.optimizedSkinDefParamsAddress = new AssetReferenceT<SkinDefParams>(string.Empty);

            Default = skin;
            Log.Info("Built Kris's default skin over " + renderers.Length + " renderer(s).");
            return skin;
        }

        /// <summary>
        /// Gives <paramref name="modelObject"/> a ModelSkinController wearing the default skin.
        /// DelayLoadingAnimatorUntilMaterialsHaveCompleted stays off: when on, the controller
        /// switches every renderer off and back on around the skin load, which is exactly the
        /// kind of renderer toggling the cape cannot survive.
        /// </summary>
        internal static void Wear(GameObject modelObject, SkinDef skin)
        {
            if (modelObject == null || skin == null) return;

            var controller = modelObject.GetComponent<ModelSkinController>();
            if (controller == null) controller = modelObject.AddComponent<ModelSkinController>();

            controller.skins = new[] { skin };
            controller.DelayLoadingAnimatorUntilMaterialsHaveCompleted = false;
        }
    }
}
