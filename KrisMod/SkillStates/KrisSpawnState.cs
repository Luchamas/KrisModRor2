using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace KrisMod.SkillStates
{
    /// <summary>
    /// How Kris arrives on every stage after the first: hidden for a moment, then printed in
    /// by the teleporter effect. This is vanilla's SpawnTeleporterState with the one line it gets
    /// wrong for them.
    ///
    /// Vanilla finds the CharacterModel to hide and print through the model's Animator -
    /// GetModelAnimator().gameObject. mdlKris deliberately has no Animator (see
    /// KrisRig.StripDonor), so it found none, and threw a NullReferenceException on its first
    /// tick past the delay. The throw landed before the line that shortens the state to that
    /// delay, so instead of arriving with everyone else they stood frozen for the state's full
    /// 4-second fallback, with no effect and no sound. That was the freeze at every stage start.
    ///
    /// Here the CharacterModel is read off the model transform, where it actually lives. The delay
    /// and the sound are vanilla's own configured statics, so they keep in step with other
    /// survivors if the game ever retunes them.
    /// </summary>
    public class KrisSpawnState : BaseState
    {
        private CharacterModel characterModel;
        private CameraTargetParams.AimRequest aimRequest;
        private bool hasTeleported;

        public override void OnEnter()
        {
            base.OnEnter();

            Transform model = GetModelTransform();
            if (model != null) characterModel = model.GetComponent<CharacterModel>();
            if (characterModel != null) characterModel.invisibilityCount++;

            if (cameraTargetParams != null)
            {
                aimRequest = cameraTargetParams.RequestAimType(CameraTargetParams.AimType.Aura);
            }

            if (NetworkServer.active) characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (!hasTeleported && fixedAge >= SpawnTeleporterState.initialDelay)
            {
                hasTeleported = true;

                if (characterModel != null)
                {
                    characterModel.invisibilityCount--;
                    TeleportOutController.AddTPOutEffect(characterModel, 1f, 0f, SpawnTeleporterState.initialDelay);
                }

                GameObject effect = Run.instance != null ? Run.instance.GetTeleportEffectPrefab(gameObject) : null;
                if (effect != null && !Run.instance.spawnWithPod)
                {
                    EffectManager.SimpleEffect(effect, transform.position, Quaternion.identity, false);
                    Util.PlaySound(SpawnTeleporterState.soundString, gameObject);
                }
            }

            // Vanilla shortens its duration to the delay the moment it teleports, so control is
            // handed back on that same tick.
            if (hasTeleported && isAuthority) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (!hasTeleported && characterModel != null) characterModel.invisibilityCount--;
            if (aimRequest != null) aimRequest.Dispose();

            if (NetworkServer.active)
            {
                characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
                characterBody.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, 3f);
            }

            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Death;
    }
}
