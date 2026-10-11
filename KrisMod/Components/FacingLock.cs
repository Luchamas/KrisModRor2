using RoR2;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// Who has CharacterDirection switched off, counted.
    ///
    /// Kris's skills run on two state machines, and a state taking the facing used to remember
    /// whether the component was on, to hand it back. When one machine's state took it while the
    /// other's still held it - the SOUL's pull on "Body" handing over to the catch on "Weapon" -
    /// the second remembered "off", the first switched it back on underneath the second, and the
    /// second then switched it off for good. Counted here instead: off with the first holder, back
    /// to how it was with the last.
    /// </summary>
    public class FacingLock : MonoBehaviour
    {
        private int holders;
        private bool wasEnabled;

        internal static void Take(CharacterDirection direction)
        {
            if (direction == null) return;

            FacingLock facingLock = direction.GetComponent<FacingLock>();
            if (facingLock == null) facingLock = direction.gameObject.AddComponent<FacingLock>();

            if (facingLock.holders++ == 0) facingLock.wasEnabled = direction.enabled;
            direction.enabled = false;
        }

        internal static void Give(CharacterDirection direction)
        {
            if (direction == null) return;

            FacingLock facingLock = direction.GetComponent<FacingLock>();
            if (facingLock == null || facingLock.holders == 0) return;

            if (--facingLock.holders == 0) direction.enabled = facingLock.wasEnabled;
        }
    }
}
