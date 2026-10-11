using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// UNLEASH's root on a flying enemy: slams it down onto the ground below and holds it there,
    /// still free to turn and attack but not to move, until the time runs out or it dies.
    ///
    /// Server only. Monsters' positions are the server's to set and reach clients through their
    /// usual sync, so nothing here needs to run anywhere else.
    /// </summary>
    public class GroundPin : MonoBehaviour
    {
        /// <summary>Seconds the slam down takes.</summary>
        private const float DropTime = 0.25f;
        /// <summary>How far below the enemy to look for ground. Over a pit with no floor in reach, nothing happens.</summary>
        private const float MaxDrop = 300f;

        private CharacterBody body;
        private Vector3 start;
        private Vector3 target;
        private float age;
        private float duration;

        /// <summary>Pins <paramref name="victim"/> for <paramref name="seconds"/>, or restarts the clock if it already is.</summary>
        internal static void Pin(CharacterBody victim, float seconds)
        {
            if (victim == null || !IsFlyer(victim)) return;

            GroundPin pin = victim.GetComponent<GroundPin>();
            if (pin != null)
            {
                pin.duration = Mathf.Max(pin.duration - pin.age, seconds) + pin.age;
                return;
            }

            Vector3 ground;
            if (!FindGround(victim, out ground)) return;

            pin = victim.gameObject.AddComponent<GroundPin>();
            pin.body = victim;
            pin.start = victim.transform.position;
            pin.target = ground;
            pin.duration = seconds;

            if (RoR2Content.Buffs.Entangle != null) victim.AddTimedBuff(RoR2Content.Buffs.Entangle, seconds);
        }

        private static bool IsFlyer(CharacterBody victim) =>
            victim.GetComponent<RigidbodyMotor>() != null
            || (victim.characterMotor != null && victim.characterMotor.isFlying);

        /// <summary>Where the body's pivot sits once its underside rests on the ground below it.</summary>
        private static bool FindGround(CharacterBody victim, out Vector3 ground)
        {
            ground = Vector3.zero;

            RaycastHit hit;
            if (!Physics.Raycast(victim.corePosition, Vector3.down, out hit, MaxDrop,
                                 LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return false;

            ground = victim.transform.position;
            ground.y = hit.point.y + PivotHeight(victim);
            return true;
        }

        /// <summary>How far the body's pivot is above its lowest point.</summary>
        private static float PivotHeight(CharacterBody victim)
        {
            float pivot = victim.transform.position.y;
            float lowest = float.PositiveInfinity;
            foreach (Collider collider in victim.GetComponents<Collider>())
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                lowest = Mathf.Min(lowest, collider.bounds.min.y);
            }

            if (float.IsPositiveInfinity(lowest)) lowest = victim.footPosition.y;
            return Mathf.Max(0f, pivot - lowest);
        }

        private void FixedUpdate()
        {
            age += Time.fixedDeltaTime;

            if (body == null || body.healthComponent == null || !body.healthComponent.alive || age >= duration)
            {
                Destroy(this);
                return;
            }

            // Eased in, so it is a slam rather than a float down.
            float t = Mathf.Clamp01(age / DropTime);
            Hold(Vector3.Lerp(start, target, t * t));
        }

        private void Hold(Vector3 position)
        {
            RigidbodyMotor rigidbodyMotor = body.GetComponent<RigidbodyMotor>();
            if (rigidbodyMotor != null)
            {
                rigidbodyMotor.moveVector = Vector3.zero;
                Rigidbody rigid = rigidbodyMotor.rigid;
                if (rigid != null)
                {
                    rigid.velocity = Vector3.zero;
                    rigid.position = position;
                }
                else
                {
                    body.transform.position = position;
                }
                return;
            }

            CharacterMotor motor = body.characterMotor;
            if (motor != null)
            {
                motor.velocity = Vector3.zero;
                if (motor.Motor != null) motor.Motor.SetPosition(position);
                else body.transform.position = position;
            }
        }
    }
}
