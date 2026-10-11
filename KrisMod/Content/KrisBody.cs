using System.Collections.Generic;
using System.Reflection;
using KrisMod.Components;
using KrisMod.Modules;
using R2API;
using RoR2;
using UnityEngine;

namespace KrisMod.Content
{
    /// <summary>
    /// Builds the Kris body, master and survivor entry.
    ///
    /// The body is cloned from Loader, the vanilla survivor whose rig already carries melee
    /// punch animations for Flower Punches to drive. Cloning also inherits every piece of survivor
    /// plumbing RoR2 expects - motor, hurtboxes, camera params, drop pod, ragdoll, item displays -
    /// already wired correctly. Only the parts that make Kris themself are replaced.
    /// </summary>
    internal static class KrisBody
    {
        internal const string BodyName = "KrisBody";
        internal const string MasterName = "KrisMonsterMaster";

        internal static GameObject BodyPrefab;
        internal static GameObject DisplayPrefab;
        internal static SurvivorDef Survivor;

        private static readonly FieldInfo CrosshairField =
            typeof(CharacterBody).GetField("_defaultCrosshairPrefab",
                                           BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Init()
        {
            var sourceBody = KrisAssets.Load<GameObject>("RoR2/Base/Loader/LoaderBody.prefab");
            if (sourceBody == null)
            {
                Log.Error("Could not load LoaderBody - Kris cannot be created.");
                return;
            }

            BodyPrefab = PrefabAPI.InstantiateClone(sourceBody, BodyName, true);

            ConfigureBody();
            ConfigureModel();
            KrisSkills.Build(BodyPrefab);
            AttachComponents();

            ContentAddition.AddBody(BodyPrefab);

            CreateMaster();
            CreateDisplayPrefab();
            CreateSurvivorDef();
        }

        private static void ConfigureBody()
        {
            var body = BodyPrefab.GetComponent<CharacterBody>();
            if (body == null)
            {
                Log.Error("Cloned body has no CharacterBody component.");
                return;
            }

            body.baseNameToken = Tokens.BodyName;
            body.subtitleNameToken = Tokens.BodySubtitle;
            body.bodyColor = KrisAssets.KrisBlue;

            if (KrisAssets.PortraitTexture != null) body.portraitIcon = KrisAssets.PortraitTexture;

            body.baseMaxHealth = KrisTuning.BaseHealth;
            body.levelMaxHealth = Mathf.Round(KrisTuning.BaseHealth * 0.3f);
            body.baseRegen = 1.5f;
            body.levelRegen = 0.2f;
            body.baseMaxShield = 0f;
            body.levelMaxShield = 0f;

            body.baseDamage = KrisTuning.BaseDamage;
            body.levelDamage = KrisTuning.BaseDamage * 0.2f;
            body.baseAttackSpeed = 1f;
            body.levelAttackSpeed = 0f;
            body.baseCrit = 1f;
            body.levelCrit = 0f;

            body.baseArmor = KrisTuning.BaseArmor;
            body.levelArmor = 0f;

            body.baseMoveSpeed = KrisTuning.BaseMoveSpeed;
            body.levelMoveSpeed = 0f;
            body.baseAcceleration = 80f;
            body.baseJumpPower = 15f;
            body.levelJumpPower = 0f;
            body.baseJumpCount = 1;
            body.sprintingSpeedMultiplier = 1.45f;

            body.autoCalculateLevelStats = false;
            body.hullClassification = HullClassification.Human;
            body.isChampion = false;

            ReplaceSpawnState(body);

            if (CrosshairField != null && KrisAssets.Crosshair != null)
            {
                CrosshairField.SetValue(body, KrisAssets.Crosshair);
            }
        }

        /// <summary>
        /// Everywhere Loader's body names vanilla's SpawnTeleporterState, Kris's body names
        /// KrisSpawnState instead. Vanilla's looks for the CharacterModel through an Animator
        /// mdlKris does not have, and froze them for four seconds at every stage start.
        ///
        /// Two fields can name it, on two different paths, and the one that matters is the state
        /// machine's. A body starts in its Body machine's initialStateType the moment it spawns,
        /// which is how every stage after the first begins. CharacterBody.preferredInitialStateType
        /// is only read by Run.HandlePlayerFirstEntryAnimation, and only for a body with no drop
        /// pod - Loader has one. The first fix set only that field and changed nothing in game.
        /// </summary>
        private static void ReplaceSpawnState(CharacterBody body)
        {
            var vanilla = typeof(EntityStates.SpawnTeleporterState);
            var ours = new EntityStates.SerializableEntityStateType(typeof(SkillStates.KrisSpawnState));
            var replaced = new List<string>();

            foreach (EntityStateMachine machine in BodyPrefab.GetComponents<EntityStateMachine>())
            {
                if (machine.initialStateType.stateType != vanilla) continue;
                machine.initialStateType = ours;
                replaced.Add("the " + machine.customName + " machine's initial state");
            }

            if (body.preferredInitialStateType.stateType == vanilla)
            {
                body.preferredInitialStateType = ours;
                replaced.Add("the body's preferred initial state");
            }

            if (replaced.Count > 0)
            {
                Log.Info("Spawn: KrisSpawnState replaces SpawnTeleporterState as " +
                         string.Join(" and ", replaced.ToArray()) + ".");
            }
            else
            {
                Log.Warning("Spawn: nothing on the body names SpawnTeleporterState, so KrisSpawnState " +
                            "is not used. If Kris freezes at stage start, find what does.");
            }
        }

        private static void ConfigureModel()
        {
            var modelLocator = BodyPrefab.GetComponent<ModelLocator>();
            if (modelLocator == null || modelLocator.modelBaseTransform == null) return;

            GameObject customModel = KrisAssets.FromBundle<GameObject>("mdlKris");

            // Before the swap, not after: swapping strips Loader's model object and resets its
            // scale, and the hitbox has to already be there to be carried through that unmoved.
            BuildMeleeHitbox(modelLocator);

            if (customModel != null)
            {
                SwapInCustomModel(modelLocator, customModel);
            }
            else
            {
                Log.Error("No mdlKris in the asset bundle - Kris is wearing Loader's model.");
            }

            ConfigureItemDisplays(modelLocator.modelTransform);
        }

        /// <summary>
        /// What Loader's model object is called once it is Kris's. The skin's renderer paths
        /// are relative to it, so the body and the mannequin must agree on what is inside it -
        /// not on this name, but keeping one name keeps that obvious.
        /// </summary>
        private const string HolderName = "mdlKris";

        /// <summary>
        /// Watches for the ragdoll waking, to stop Kris's animator - see
        /// <see cref="Components.KrisRagdollWatcher"/>.
        ///
        /// Without a ragdoll of their own there is no corpse to fall any more: the fallback used to
        /// hang them on Loader's, and Loader's skeleton is gone. They then holds their last pose until
        /// the body is cleaned up, which is what bodies with no ragdoll do in vanilla too.
        /// </summary>
        private static void WatchRagdoll(Transform rig, Transform custom, bool ownRagdoll)
        {
            var ragdoll = rig.GetComponent<RagdollController>();
            if (ragdoll == null || !ownRagdoll)
            {
                Log.Warning("Kris has no ragdoll of their own (Model Ragdoll is off, or their bones " +
                            "did not match) - their corpse will hold its last pose instead of falling.");
                return;
            }

            var watcher = rig.GetComponent<Components.KrisRagdollWatcher>();
            if (watcher == null) watcher = rig.gameObject.AddComponent<Components.KrisRagdollWatcher>();
            watcher.ragdoll = ragdoll;
            // Their bones are the physics, so their animator must let go of them. This does not rely
            // on RagdollController doing it: two writers on one transform is what stretched the
            // corpse, and it is too important to leave to a private field.
            watcher.animatorToStop = custom.GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Item displays are positioned by Loader's ItemDisplayRuleSet. Its rules name bones through
        /// the ChildLocator - which now points at Kris's bones - but every offset, rotation and
        /// scale in them was authored against Loader's bones, so items attach to the right bone at
        /// the wrong place. Until they have a rule set of their own, this at least makes hiding them a
        /// config flip rather than a code change.
        /// </summary>
        private static void ConfigureItemDisplays(Transform rig)
        {
            if (rig == null || KrisTuning.ShowItemDisplays) return;

            var characterModel = rig.GetComponent<CharacterModel>();
            if (characterModel == null) return;

            characterModel.itemDisplayRuleSet = null;
            Log.Info("Item displays hidden: Kris has no display rules of their own yet, and " +
                     "Loader's are positioned against Loader's skeleton.");
        }

        /// <summary>
        /// Flower Punches needs a HitBoxGroup to swing through. Loader's own fist boxes are sized
        /// for punches, and a custom model is not expected to ship any, so Kris gets their own
        /// built here against whatever model ended up on the body.
        /// </summary>
        private static void BuildMeleeHitbox(ModelLocator modelLocator)
        {
            Transform model = modelLocator.modelTransform;
            if (model == null) return;

            float reach = Mathf.Max(1f, KrisTuning.SlashRange);

            // Centred half a reach ahead of the body, so the box spans from chest to full reach.
            AddHitBox(model, SkillStates.KrisSlash.HitBoxGroupName, "KrisSwordHitbox",
                      new Vector3(0f, 1.1f, reach * 0.5f), new Vector3(3.6f, 3.2f, reach));

            // SOUL's cut: a slab through Kris along the facing, so it reaches in front and behind but
            // not out to the sides - Merc's airborne Whirlwind box (4 x 7.2 x 7.2), sized up with
            // the effect.
            AddHitBox(model, SkillStates.SoulSlash.HitBoxGroupName, "KrisSoulSlashHitbox",
                      new Vector3(0f, 1.2f, 0f), new Vector3(4f, 11f, 11f));
        }

        private static void AddHitBox(Transform model, string groupName, string objectName, Vector3 position, Vector3 size)
        {
            if (HitBoxGroup.FindByGroupName(model.gameObject, groupName) != null) return;

            var hitboxObject = new GameObject(objectName);
            hitboxObject.transform.SetParent(model, false);
            hitboxObject.transform.localPosition = position;
            hitboxObject.transform.localRotation = Quaternion.identity;
            // OverlapAttack reads the transform's scale as the box dimensions.
            hitboxObject.transform.localScale = size;

            var group = model.gameObject.AddComponent<HitBoxGroup>();
            group.groupName = groupName;
            group.hitBoxes = new[] { hitboxObject.AddComponent<HitBox>() };
        }

        /// <summary>
        /// Turns Loader's model object into Kris's.
        ///
        /// That object carries far more than a mesh: HurtBoxGroup (without it Kris is
        /// unhittable and untargetable), CharacterModel, RagdollController and ChildLocator. So
        /// it is kept as the holder, and in order:
        /// <list type="number">
        /// <item>their model goes in as a child, and CharacterModel is handed their renderers;</item>
        /// <item>the ragdoll is built on their bones;</item>
        /// <item>the hurtboxes and ChildLocator are moved onto their bones (KrisRig.MoveOntoModel);</item>
        /// <item>what is left of Loader - skeleton, meshes, animator, skins - is deleted
        /// (KrisRig.StripDonor);</item>
        /// <item>and they are given a skin of their own in place of Loader's.</item>
        /// </list>
        /// </summary>
        private static void SwapInCustomModel(ModelLocator modelLocator, GameObject customModel)
        {
            Transform rig = modelLocator.modelTransform;
            if (rig == null)
            {
                Log.Error("No model transform to attach the custom model to.");
                return;
            }

            GameObject instance = AttachCustomModel(rig, customModel, SkillStates.KrisAnimations.Idle);

            bool ownRagdoll = KrisTuning.ModelRagdoll &&
                              KrisRagdoll.Build(rig, instance.transform);
            KrisRig.MoveOntoModel(BodyPrefab.transform, rig, instance.transform);
            KrisRig.StripDonor(BodyPrefab.transform, rig, instance.transform, HolderName);
            WatchRagdoll(rig, instance.transform, ownRagdoll);

            var characterModel = rig.GetComponent<CharacterModel>();
            if (characterModel != null)
            {
                KrisSkin.Wear(rig.gameObject, KrisSkin.Get(rig.gameObject, characterModel.baseRendererInfos));
            }

            Log.Info("Custom model is the body's model now (scale " + ModelScale +
                     "); nothing of Loader's rig is left on it.");
        }

        /// <summary>
        /// Kris's size inside the model object. The FBX imports a hundred times larger than the
        /// .blend (Unity reads its centimetres as metres - KrisSetup turns file scale off), so
        /// Kris's 1.566m Stance arrives 156.6 units tall; 0.01344 stands them at 2.1m - 1.75m
        /// was tried first and read as too short next to the other survivors, so it is 20% up.
        ///
        /// A constant, not a config entry: it belongs to the model in the asset bundle, not to the
        /// player. (Flowery's was config once, tuned by hand in a local file that never shipped, so
        /// a fresh install fell back to the old default and got a giant Flowery.) Re-measure it if
        /// the model changes - KrisRig logs the height it measures at startup.
        /// </summary>
        private const float ModelScale = 0.01344f;

        /// <summary>
        /// Puts the custom model inside a vanilla model object and hands that object's
        /// CharacterModel over to it. Shared by the body and by the character-select mannequin.
        /// <paramref name="startPose"/> is the animator state the model idles in - flight in a
        /// run, the relaxed standing pose on the menu.
        /// </summary>
        private static GameObject AttachCustomModel(Transform rig, GameObject customModel,
                                                    string startPose, float scaleMultiplier = 1f,
                                                    string fallbackPose = null)
        {
            // Stay INSIDE the model object. Everything that matters hangs off it - CharacterModel
            // drives visibility, overlays and cloaking from its own hierarchy, and moving Kris
            // out made them invisible. Cancel out the object's own rotation and scale so their size
            // is relative to plain body space; that inherited transform is what made them giant
            // and upside-down. (StripDonor later resets the object to identity, holding them where
            // they are, so this ends up as their plain local transform.)
            GameObject instance = Object.Instantiate(customModel, rig);
            instance.name = SkillStates.KrisAnimations.ModelObject;

            Vector3 rigScale = rig.localScale;
            float wanted = ModelScale * scaleMultiplier;

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Inverse(rig.localRotation);
            instance.transform.localScale = new Vector3(
                wanted / Mathf.Max(0.0001f, rigScale.x),
                wanted / Mathf.Max(0.0001f, rigScale.y),
                wanted / Mathf.Max(0.0001f, rigScale.z));

            Log.Info("Rig '" + rig.name + "' localScale=" + rigScale +
                     " localRotation=" + rig.localRotation.eulerAngles +
                     " -> compensated child scale " + instance.transform.localScale);

            // Register their renderers so overlays, cloaking and elite materials find something to
            // work on. Loader's own meshes are not hidden here: StripDonor deletes them.
            var characterModel = rig.GetComponent<CharacterModel>();
            if (characterModel != null) characterModel.baseRendererInfos = BuildRendererInfos(instance);

            // Starts their pose and keeps the sword and shield in step with it.
            var enforcer = rig.GetComponent<Components.KrisModelEnforcer>();
            if (enforcer == null) enforcer = rig.gameObject.AddComponent<Components.KrisModelEnforcer>();
            enforcer.startPose = startPose;
            enforcer.fallbackPose = fallbackPose;

            return instance;
        }

        private static CharacterModel.RendererInfo[] BuildRendererInfos(GameObject model)
        {
            var infos = new List<CharacterModel.RendererInfo>();

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;

                if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 1)
                {
                    // CharacterModel rebuilds this renderer's whole material array, sized to one
                    // entry plus overlays, so every submesh past the first would stop rendering.
                    // Split the mesh by material in export_fbx.py instead.
                    Log.Warning("Renderer '" + renderer.name + "' has " +
                                renderer.sharedMaterials.Length + " materials; only the first " +
                                "will render in game. It needs to be one material per object.");
                }

                infos.Add(new CharacterModel.RendererInfo
                {
                    renderer = renderer,
                    defaultMaterial = renderer.sharedMaterial,
                    defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ignoreOverlays = false,
                    hideOnDeath = false,
                });
            }

            return infos.ToArray();
        }

        private static void AttachComponents()
        {
            if (BodyPrefab.GetComponent<TensionController>() == null) BodyPrefab.AddComponent<TensionController>();
            // On the prefab, not added later: HealthComponent collects its incoming-damage
            // receivers once, in Awake.
            if (BodyPrefab.GetComponent<DefendGuard>() == null) BodyPrefab.AddComponent<DefendGuard>();
            if (BodyPrefab.GetComponent<RootedGuard>() == null) BodyPrefab.AddComponent<RootedGuard>();
            if (BodyPrefab.GetComponent<TensionHud>() == null) BodyPrefab.AddComponent<TensionHud>();
            if (BodyPrefab.GetComponent<ActMenuHud>() == null) BodyPrefab.AddComponent<ActMenuHud>();
            if (BodyPrefab.GetComponent<KrisLocomotion>() == null) BodyPrefab.AddComponent<KrisLocomotion>();
        }

        private static void CreateMaster()
        {
            var sourceMaster = KrisAssets.Load<GameObject>("RoR2/Base/Loader/LoaderMonsterMaster.prefab");
            if (sourceMaster == null)
            {
                Log.Warning("Could not load LoaderMonsterMaster - Kris will have no AI master.");
                return;
            }

            GameObject master = PrefabAPI.InstantiateClone(sourceMaster, MasterName, true);
            var characterMaster = master.GetComponent<CharacterMaster>();
            if (characterMaster != null) characterMaster.bodyPrefab = BodyPrefab;

            ContentAddition.AddMaster(master);
        }

        /// <summary>
        /// The character-select mannequin.
        ///
        /// It has to be built on Loader's display prefab rather than on the bare model: the menu
        /// needs a CharacterModel and a ModelSkinController to render and light the mannequin at
        /// all, and ModelPanelParameters to frame it in the panel. Handing it the raw model left
        /// the survivor with an empty slot on the select screen and two warnings in the log
        /// saying exactly which components were missing.
        /// </summary>
        private static void CreateDisplayPrefab()
        {
            var sourceDisplay = KrisAssets.Load<GameObject>("RoR2/Base/Loader/LoaderDisplay.prefab");
            if (sourceDisplay == null)
            {
                Log.Warning("Could not load LoaderDisplay - Kris will have no select-screen model.");
                return;
            }

            DisplayPrefab = PrefabAPI.InstantiateClone(sourceDisplay, "KrisDisplay", false);

            var bodyModel = KrisAssets.FromBundle<GameObject>("mdlKris");
            if (bodyModel == null) return;

            // The mannequin's rig is the object carrying the CharacterModel, the same
            // arrangement the body uses. The menu instantiates this prefab the moment Kris is
            // picked, which is when the entrance starts; it runs once and the controller drops
            // them into the looping Select pose after it - see KrisAnimations.SelectIntro. A
            // bundle built before the entrance existed has no SelectIntro, and falls back to that
            // loop rather than to the controller's default state (the idle).
            var displayModel = DisplayPrefab.GetComponentInChildren<CharacterModel>();
            Transform rig = displayModel != null ? displayModel.transform : DisplayPrefab.transform;

            GameObject instance = AttachCustomModel(rig, bodyModel,
                SkillStates.KrisAnimations.SelectIntro, KrisTuning.DisplayModelScale,
                SkillStates.KrisAnimations.Select);

            // The same conversion as the body. Loader's display rig used to be held down at
            // runtime instead - its animator kept playing the charge punch and its effect
            // objects kept throwing lights and camera shake - and deleting it ends that.
            KrisRig.MoveOntoModel(DisplayPrefab.transform, rig, instance.transform);
            KrisRig.StripDonor(DisplayPrefab.transform, rig, instance.transform, HolderName);
            ConfigureItemDisplays(rig);

            // The sound for the look back over the shoulder, two seconds into the entrance.
            rig.gameObject.AddComponent<Components.KrisSelectLookSound>();

            // Not optional here: the select screen applies the loadout's skin to the
            // mannequin without checking it has a ModelSkinController to apply it with.
            if (KrisSkin.Default != null)
            {
                KrisSkin.Wear(rig.gameObject, KrisSkin.Default);
            }
            else
            {
                Log.Error("No Kris skin to put on the character-select mannequin - the select " +
                          "screen will fail to apply a skin to it.");
            }

            Log.Info("Character-select mannequin built on Loader's display prefab at " +
                     KrisTuning.DisplayModelScale + "x scale.");
        }

        private static void CreateSurvivorDef()
        {
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            ((ScriptableObject)Survivor).name = "KrisSurvivorDef";
            // Unique across every installed mod - the catalog keys on it. (Copied from Flowery as
            // "FLOWERY" at first, which made Kris and Flowery the same survivor to the catalog.)
            Survivor.cachedName = "KRIS";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = DisplayPrefab != null ? DisplayPrefab : BodyPrefab;
            Survivor.primaryColor = KrisAssets.KrisBlue;
            Survivor.displayNameToken = Tokens.BodyName;
            Survivor.descriptionToken = Tokens.BodyDescription;
            Survivor.outroFlavorToken = Tokens.BodyOutro;
            Survivor.mainEndingEscapeFailureFlavorToken = Tokens.BodyFailure;
            Survivor.desiredSortPosition = 25f;
            Survivor.hidden = false;
            Survivor.unlockableDef = null;

            ContentAddition.AddSurvivorDef(Survivor);
        }
    }
}
