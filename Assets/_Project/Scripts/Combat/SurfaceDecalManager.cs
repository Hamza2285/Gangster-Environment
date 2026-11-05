using Invector;
using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.Combat
{
    /// <summary>
    /// Bullet marks only where the hit point is the visible surface. Static mesh colliders are the world's own meshes, so they get
    /// Invector's decal and impact effect. Box and capsule colliders (cars, street props, trunks, lamp posts) stand off their meshes,
    /// and would leave a mark hanging in the air, so they only get a brief spark. Characters get no mark at all: their blood comes
    /// from their own hit particle.
    /// </summary>
    public class SurfaceDecalManager : vDecalManager
    {
        [Tooltip("Brief particle effect (destroys itself) without a persistent mark, for colliders that are not the visible surface.")]
        [SerializeField] private GameObject sparkEffect;

        public override void CreateDecal(RaycastHit hitInfo)
        {
            Collider surface = hitInfo.collider;
            if (surface == null) return;
            if (surface is MeshCollider && !surface.isTrigger && surface.attachedRigidbody == null)
            {
                base.CreateDecal(hitInfo);
                return;
            }
            if (surface.GetComponentInParent<vIHealthController>() != null) return;
            if (sparkEffect != null) Instantiate(sparkEffect, hitInfo.point, Quaternion.LookRotation(hitInfo.normal, Vector3.up), vObjectContainer.root);
        }
    }
}
