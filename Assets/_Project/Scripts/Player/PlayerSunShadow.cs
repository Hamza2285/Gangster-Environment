using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// A real, soft sun shadow for the character being played (Jason / Mia), without touching any lighting / URP setting.
    /// The city's own sun shadow (one 1024 map over 40 m, hard, normal bias 1) drew the player faint and broken.
    /// Here a small orthographic camera looks down the sun's direction and renders only the player into a mask; that view,
    /// projected along the sun onto the ground under him, is a mesh whose shader darkens the ground by the mask, blurred more the
    /// further it is from his feet (soft penumbra, sharp contact). Follows the sun (day / night), fades out when the player is
    /// already in shade (buildings, indoors), in vehicles, in the air and when dead. While a character is played, its mesh
    /// renderers sit on <see cref="shadowLayer"/> (an unused layer every camera and light already draws) and its built-in sun
    /// shadow is off; both are restored exactly when it is no longer played.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSunShadow : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The sun (Hamza's day/night rotates it). Empty: RenderSettings.sun.")]
        [SerializeField] private Light sun;
        [SerializeField] private Camera shadowCamera;
        [SerializeField] private MeshFilter receiver;

        [Header("Mask")]
        [Tooltip("Unused layer the played character's meshes are moved to while played (must be drawn by the game cameras).")]
        [SerializeField, Range(0, 31)] private int shadowLayer = 30;
        [SerializeField] private int resolution = 512;
        [Tooltip("Width / height (m) of the sun's view around the player.")]
        [SerializeField, Min(1f)] private float frameSize = 2.8f;

        [Header("Look")]
        [Tooltip("Darkness at full sun shadow strength (multiplied by the sun's own shadow strength).")]
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;
        [Tooltip("Direct light on the ground (sun intensity x cos of its angle) at and above which the shadow is at full darkness.")]
        [SerializeField, Min(0.01f)] private float directForFullShadow = 0.45f;
        [Tooltip("Height of the hips above the ground (m, beyond standing) at which the shadow has faded out.")]
        [SerializeField, Min(0.2f)] private float fadeHeight = 3f;
        [Tooltip("Lift of the receiver above the ground collider (road meshes sit a little above their colliders).")]
        [SerializeField, Min(0f)] private float groundOffset = 0.06f;
        [Tooltip("Lowest sun elevation (degrees) that still casts the shadow.")]
        [SerializeField] private float minSunElevation = 4f;
        [Tooltip("Turn the played character's built-in sun shadow off (it drew broken on top of this one).")]
        [SerializeField] private bool disableBuiltInShadow = true;
        [Tooltip("The world: ground the shadow lies on and what puts the player in shade.")]
        [SerializeField] private LayerMask worldLayers = 1;

        private static readonly int MaskId = Shader.PropertyToID("_ShadowMask");
        private static readonly int FeetId = Shader.PropertyToID("_FeetUV");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");

        private struct Original { public int layer; public ShadowCastingMode mode; }

        private readonly Dictionary<Renderer, Original> claimed = new Dictionary<Renderer, Original>();
        private readonly List<Renderer> scan = new List<Renderer>();
        private readonly List<Component> parts = new List<Component>();
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly Vector3[] corners = new Vector3[4];
        private RenderTexture mask;
        private Mesh mesh;
        private MeshRenderer receiverRenderer;
        private MaterialPropertyBlock block;
        private PlayableCharacter player;
        private Transform hips;
        private PlayerVehicleInteraction vehicle;
        private PlayerHealth health;
        private float visibility, nextScan, nextSunCheck;
        private bool inSun;

        private void Awake()
        {
            if (shadowCamera == null || receiver == null)
            {
                Debug.LogError($"[{nameof(PlayerSunShadow)}] Shadow camera / receiver not set on '{name}'.", this);
                enabled = false;
                return;
            }
            mask = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
            {
                name = "PlayerSunShadowMask", useMipMap = true, autoGenerateMips = true,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
            };
            shadowCamera.targetTexture = mask;
            shadowCamera.orthographic = true;
            shadowCamera.clearFlags = CameraClearFlags.SolidColor;
            shadowCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            shadowCamera.cullingMask = 1 << shadowLayer;
            shadowCamera.allowHDR = false;
            shadowCamera.allowMSAA = false;
            shadowCamera.useOcclusionCulling = false;
            shadowCamera.depth = -50f;
            shadowCamera.enabled = false;
            var data = shadowCamera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.volumeLayerMask = 0;

            mesh = new Mesh { name = "PlayerSunShadowReceiver" };
            mesh.MarkDynamic();
            mesh.vertices = new Vector3[4];
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            receiver.sharedMesh = mesh;
            receiver.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            receiver.transform.localScale = Vector3.one;
            receiverRenderer = receiver.GetComponent<MeshRenderer>();
            receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
            receiverRenderer.receiveShadows = false;
            receiverRenderer.enabled = false;
            block = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            ReleasePlayer();
            if (shadowCamera != null) shadowCamera.enabled = false;
            if (receiverRenderer != null) receiverRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (mask != null) { mask.Release(); Destroy(mask); }
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            if (player == null || !player.gameObject.activeInHierarchy) FindPlayer();
            Light light = sun != null ? sun : RenderSettings.sun;
            float target = 0f;
            Vector3 toGround = light != null ? light.transform.forward : Vector3.down;
            RaycastHit ground = default;
            bool onFoot = player != null && hips != null && (vehicle == null || vehicle.State == VehicleInteractionState.OnFoot)
                          && (health == null || !health.IsDead);
            if (onFoot && light != null && light.isActiveAndEnabled && light.shadows != LightShadows.None
                && -toGround.y >= Mathf.Sin(minSunElevation * Mathf.Deg2Rad) && Ground(out ground))
            {
                if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; ClaimRenderers(); }
                if (Time.time >= nextSunCheck) { nextSunCheck = Time.time + 0.15f; inSun = InSun(-toGround); }
                float height = Mathf.Max(0f, hips.position.y - ground.point.y - 0.95f);
                // As dark as the direct light it takes away: a grazing dusk sun (or one dimmed to nothing below the horizon by
                // Hamza's blend) lights the ground little, so its shadow is faint too.
                float direct = Mathf.Max(0f, Vector3.Dot(ground.normal, -toGround)) * light.intensity;
                target = inSun ? Mathf.Clamp01(1f - height / fadeHeight) * Mathf.Clamp01(direct / directForFullShadow) : 0f;
            }
            visibility = Mathf.MoveTowards(visibility, target, Time.deltaTime * 4f);
            bool show = visibility > 0.005f && hips != null && light != null && ground.collider != null;
            shadowCamera.enabled = show;
            receiverRenderer.enabled = show;
            if (!show) return;

            // The sun's view: centred on the body, looking along the sunlight.
            Vector3 centre = hips.position + Vector3.up * 0.05f;
            Vector3 upHint = Mathf.Abs(toGround.y) > 0.99f ? player.transform.forward : Vector3.up;
            Transform cam = shadowCamera.transform;
            const float back = 6f;
            cam.SetPositionAndRotation(centre - toGround * back, Quaternion.LookRotation(toGround, upHint));
            float half = frameSize * 0.5f;
            shadowCamera.orthographicSize = half;
            shadowCamera.aspect = 1f;
            shadowCamera.nearClipPlane = 0.05f;
            shadowCamera.farClipPlane = back + frameSize;

            // That view, carried along the sunlight onto the ground plane under the player: an exact (affine) projection.
            Vector3 right = cam.right, up = cam.up;
            Vector3 n = ground.normal;
            Vector3 g = ground.point + n * groundOffset;
            float dn = Vector3.Dot(toGround, n);
            if (dn > -0.05f) { shadowCamera.enabled = receiverRenderer.enabled = false; return; }
            corners[0] = Project(centre - right * half - up * half, g, n, toGround, dn);
            corners[1] = Project(centre + right * half - up * half, g, n, toGround, dn);
            corners[2] = Project(centre - right * half + up * half, g, n, toGround, dn);
            corners[3] = Project(centre + right * half + up * half, g, n, toGround, dn);
            mesh.vertices = corners;
            mesh.RecalculateBounds();

            Vector3 rel = ground.point - centre;
            var feet = new Vector4(Vector3.Dot(rel, right) / frameSize + 0.5f, Vector3.Dot(rel, up) / frameSize + 0.5f, 0f, 0f);
            receiverRenderer.GetPropertyBlock(block);
            block.SetTexture(MaskId, mask);
            block.SetVector(FeetId, feet);
            block.SetFloat(StrengthId, opacity * light.shadowStrength * visibility);
            receiverRenderer.SetPropertyBlock(block);
        }

        private static Vector3 Project(Vector3 p, Vector3 planePoint, Vector3 normal, Vector3 dir, float dn)
        {
            return p + dir * (Vector3.Dot(planePoint - p, normal) / dn);
        }

        private void FindPlayer()
        {
            ReleasePlayer();
            foreach (PlayableCharacter c in PlayableCharacter.All)
                if (c != null && c.gameObject.activeInHierarchy) { player = c; break; }
            if (player == null) return;
            var animator = player.GetComponent<Animator>();
            hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : player.transform;
            vehicle = player.GetComponent<PlayerVehicleInteraction>();
            health = player.GetComponent<PlayerHealth>();
            nextScan = 0f;
            nextSunCheck = 0f;
        }

        // The played character's visible meshes (and what it holds) onto the mask layer; only objects that are nothing but a
        // mesh renderer are moved, so no collider / trigger / script changes layer.
        private void ClaimRenderers()
        {
            if (player == null) return;
            player.GetComponentsInChildren(false, scan);
            foreach (Renderer r in scan)
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if (claimed.ContainsKey(r)) continue;
                r.GetComponents(parts);
                bool onlyRenderer = true;
                foreach (Component c in parts)
                    if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter)) { onlyRenderer = false; break; }
                if (!onlyRenderer) continue;
                claimed.Add(r, new Original { layer = r.gameObject.layer, mode = r.shadowCastingMode });
                r.gameObject.layer = shadowLayer;
                if (disableBuiltInShadow) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void ReleasePlayer()
        {
            foreach (KeyValuePair<Renderer, Original> pair in claimed)
            {
                if (pair.Key == null) continue;
                pair.Key.gameObject.layer = pair.Value.layer;
                pair.Key.shadowCastingMode = pair.Value.mode;
            }
            claimed.Clear();
            player = null;
            hips = null;
            vehicle = null;
            health = null;
        }

        // The ground under the hips (the player's own colliders and anything with a rigidbody are skipped).
        private bool Ground(out RaycastHit ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(hips.position + Vector3.up * 0.3f, Vector3.down, hits, 4f, worldLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.attachedRigidbody != null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; ground = hits[i]; }
            }
            return best < float.MaxValue;
        }

        // In the open sun (not under a roof / in a building's shade, where the world's own shadow already covers him).
        private bool InSun(Vector3 toSun)
        {
            return !Physics.Raycast(hips.position + Vector3.up * 0.4f, toSun, 150f, worldLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
