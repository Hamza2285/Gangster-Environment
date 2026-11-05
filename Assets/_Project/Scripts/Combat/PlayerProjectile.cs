using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.Combat
{
    /// <summary>
    /// Invector's bullet starts casting from wherever it is when its Start runs, but the weapon fires in LateUpdate, so physics has
    /// already flown it one frame by then: at 310 m/s and 30 fps that is 10 m with no hit check, and close targets get shot through.
    /// This casts from the muzzle instead.
    /// </summary>
    public class PlayerProjectile : vProjectileControl
    {
        private Vector3 spawnPosition;

        private void Awake()
        {
            spawnPosition = transform.position;
        }

        protected override void Start()
        {
            base.Start();
            startPosition = spawnPosition;
            previousPosition = spawnPosition;
        }
    }
}
