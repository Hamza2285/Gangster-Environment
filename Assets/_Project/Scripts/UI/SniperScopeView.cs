using Invector.vCharacterController;
using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// GTA-style full-screen sniper scope: shows the black scope mask and reticle while Invector's scope camera is rendering
    /// for a magnified weapon that does not use Invector's own lens UI (<see cref="vShooterWeapon.useUI"/> off; the zoomed
    /// world is rendered full screen by the scope background camera). It also sets that camera's near clip so the rifle's
    /// barrel and the player never show inside the scope, and keeps the reticle centre on the point the shot goes to: Invector
    /// fires at its aim point, while its scope camera follows the arm's aim (which can lag a few degrees behind it).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // after Invector has placed the scope camera this frame
    public class SniperScopeView : MonoBehaviour
    {
        [SerializeField] private vShooterManager shooterManager;
        [SerializeField] private vShooterMeleeInput shooterInput;
        [Tooltip("Invector's scope background camera (player AimCanvas/ScopeAimBackgroundCamera).")]
        [SerializeField] private Camera scopeCamera;
        [Tooltip("Scope mask + reticle shown while scoped.")]
        [SerializeField] private GameObject overlay;
        [Tooltip("Near clip of the scope camera (m): beyond the muzzle, so the barrel and muzzle flash stay out of the view.")]
        [SerializeField, Min(0.01f)] private float scopeNearClip = 1f;
        [Tooltip("Only magnified scopes get the overlay: Invector's background zoom (FOV = 60 - zoom) must be at least this. "
                 + "Weapons whose scope view is only a closer over-the-shoulder camera keep the normal crosshair.")]
        [SerializeField, Range(0f, 59f)] private float minBackgroundZoom = 20f;

        private bool scoped;

        private void Awake()
        {
            if (shooterManager == null || shooterInput == null || scopeCamera == null || overlay == null)
            {
                Debug.LogError($"[{nameof(SniperScopeView)}] Missing references on '{name}'.", this);
                enabled = false;
                return;
            }
            scopeCamera.nearClipPlane = scopeNearClip;
            overlay.SetActive(false);
        }

        private void OnDisable() => SetScoped(false);

        // Per-frame: Invector's scope view events are not reliable (leaving the scope while still aiming raises the
        // "enable" event again), so the scope camera's own state is followed. While scoped, the camera is aimed at
        // Invector's aim point (where the bullet goes).
        private void LateUpdate()
        {
            vShooterWeapon weapon = shooterManager.CurrentWeapon;
            bool active = scopeCamera.isActiveAndEnabled && weapon != null && !weapon.useUI && weapon.backGroundScopeZoom >= minBackgroundZoom;
            if (active != scoped) SetScoped(active);
            if (!active) return;
            Transform cam = scopeCamera.transform;
            Vector3 toAim = shooterInput.AimPosition - cam.position;
            if (toAim.sqrMagnitude > 0.25f) cam.rotation = Quaternion.LookRotation(toAim, Vector3.up);
        }

        private void SetScoped(bool value)
        {
            scoped = value;
            if (overlay != null && overlay.activeSelf != value) overlay.SetActive(value);
        }
    }
}
