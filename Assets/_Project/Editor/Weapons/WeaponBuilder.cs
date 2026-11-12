using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using OpenworldCrime.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Turns the "FBX DATA GUN" models into Invector inventory weapons (prefab variants of the project / Invector weapon
    /// templates, so animations, IK, sounds, muzzle flash and shells are reused) and keeps the <see cref="WeaponCatalog"/>,
    /// the Invector item list, the display prefabs and the icons in sync.
    /// The FBX files and the templates are never modified. Re-running is safe: assets are updated in place, materials are
    /// only created once (so textures assigned to them later are kept).
    /// </summary>
    public static class WeaponBuilder
    {
        public const string CatalogPath = "Assets/_Project/Data/Weapons/WeaponCatalog.asset";
        private const string ItemListPath = "Assets/_Project/Data/Items/OWC_ShooterMelee_ItemListData.asset";
        private const string WeaponFolder = "Assets/_Project/Prefabs/Weapons/Shop";
        private const string DisplayFolder = "Assets/_Project/Prefabs/Weapons/Display";
        private const string MaterialFolder = "Assets/_Project/Art/Weapons/Materials";
        private const string IconFolder = "Assets/_Project/Art/Weapons/Icons";
        private const string FbxFolder = "Assets/FBX DATA GUN/";
        private const string PlayerBulletPath = "Assets/_Project/Prefabs/Weapons/PlayerBullet.prefab";
        private const string RocketPath = "Assets/_Project/Prefabs/Weapons/PlayerRocket.prefab";
        private const string InvectorRocketPath = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Weapon/_bullets/vRPGAmmo.prefab";
        private const string InvectorWeapons = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Weapon/_weapons_WITH_Inventory/_weaponsPrefabs/";

        private enum Template { Pistol, Rifle, Shotgun, Sniper, Rpg, Axe }
        private enum Finish { Gunmetal, Polymer, Golden, Chrome, Wood }

        private class TemplateInfo
        {
            public string prefabPath;
            public int itemId;
            public float leftHandAlong; // fraction of grip->front distance (rifles)
        }

        private static readonly Dictionary<Template, TemplateInfo> Templates = new Dictionary<Template, TemplateInfo>
        {
            { Template.Pistol, new TemplateInfo { prefabPath = "Assets/_Project/Prefabs/Weapons/Pistol_Inventory.prefab", itemId = 10 } },
            { Template.Rifle, new TemplateInfo { prefabPath = "Assets/_Project/Prefabs/Weapons/AssaultRifle_Inventory.prefab", itemId = 11, leftHandAlong = 0.45f } },
            { Template.Shotgun, new TemplateInfo { prefabPath = InvectorWeapons + "vShotgun_Inventory.prefab", itemId = 12, leftHandAlong = 0.5f } },
            { Template.Sniper, new TemplateInfo { prefabPath = InvectorWeapons + "vSniper_Inventory.prefab", itemId = 15, leftHandAlong = 0.36f } },
            { Template.Rpg, new TemplateInfo { prefabPath = InvectorWeapons + "vRPG_Inventory.prefab", itemId = 16 } },
            { Template.Axe, new TemplateInfo { prefabPath = "Assets/Invector-3rdPersonController/Melee Combat/Prefabs/Weapons/Axe_Inventory.prefab", itemId = 0 } },
        };

        /// <summary>How one FBX is fitted into the hand plus its catalog data.</summary>
        private class Spec
        {
            public string id, name, fbx;
            public WeaponCategory category;
            public Template template;
            public Finish finish;
            public int itemId;
            public int ammoItemId = -1;
            public string[] exclude = { "cameraMask", "cameraSprite" };
            public float gripZ, gripY;      // grip point as fractions of the kept meshes' bounds (from the rear / from the bottom)
            public float length;            // final length in metres
            public Vector3 euler;           // orientation fix
            public float secondGripZ = -1f; // RPG front grip (fraction), left hand
            public Vector3 handOffset;      // per-model grip correction (weapon space), checked in Captures/GunShop/hands_v2
            public Vector3 supportOffset;   // per-model left-hand IK target correction (weapon space)
            public WeaponDefinition stats;
            public string existingPrefab;   // starter weapons: use this prefab as-is (no FBX)
        }

        private static WeaponDefinition Stats(int price, int ammoPrice, int perBuy, int maxReserve, int minDmg, int maxDmg, float interval, bool auto, int clip, int pellets, float spread, float recoilUp, float recoilSide, float range)
        {
            return new WeaponDefinition
            {
                price = price, ammoPrice = ammoPrice, ammoPerPurchase = perBuy, maxReserveAmmo = maxReserve,
                minDamage = minDmg, maxDamage = maxDmg, shotInterval = interval, automatic = auto, clipSize = clip,
                pelletsPerShot = pellets, spread = spread, recoilUp = recoilUp, recoilSide = recoilSide, range = range,
            };
        }

        private const int PistolAmmo = 14, RifleAmmo = 13, ShotgunAmmo = 17, SniperAmmo = 19, RpgAmmo = 18;

        private static List<Spec> Specs()
        {
            var list = new List<Spec>
            {
                // Starters (existing project prefabs, kept as they are).
                new Spec { id = "pistol", name = "Pistol", category = WeaponCategory.Pistol, itemId = 10, ammoItemId = PistolAmmo, existingPrefab = Templates[Template.Pistol].prefabPath,
                    stats = Stats(500, 30, 12, 240, 15, 36, 0.25f, false, 12, 1, 0f, 2f, 0.5f, 50f) },
                new Spec { id = "assault_rifle", name = "Assault Rifle", category = WeaponCategory.AssaultRifle, itemId = 11, ammoItemId = RifleAmmo, existingPrefab = Templates[Template.Rifle].prefabPath,
                    stats = Stats(3000, 60, 30, 450, 15, 25, 0.125f, true, 30, 1, 0f, 1f, 0.02f, 50f) },

                // Pistols
                new Spec { id = "loyal69", name = "Loyal 69", fbx = "Loyal 69_0", category = WeaponCategory.Pistol, template = Template.Pistol, finish = Finish.Polymer, itemId = 100, ammoItemId = PistolAmmo,
                    gripZ = 0.229f, gripY = 0.457f, length = 0.23f, stats = Stats(800, 30, 15, 240, 18, 32, 0.2f, false, 15, 1, 0.5f, 1.5f, 0.6f, 45f) },
                new Spec { id = "deagle_chrome", name = "Deagle Chrome", fbx = "Deagle Chrome", exclude = new[] { "Barrel" }, category = WeaponCategory.Pistol, template = Template.Pistol, finish = Finish.Chrome, itemId = 101, ammoItemId = PistolAmmo,
                    gripZ = 0.252f, gripY = 0.476f, length = 0.27f, stats = Stats(1500, 40, 7, 240, 30, 55, 0.32f, false, 7, 1, 0.3f, 3f, 1f, 60f) },
                new Spec { id = "deagle_golden", name = "Golden Deagle", fbx = "Deagle Golden", category = WeaponCategory.Pistol, template = Template.Pistol, finish = Finish.Golden, itemId = 102, ammoItemId = PistolAmmo,
                    gripZ = 0.255f, gripY = 0.457f, length = 0.27f, stats = Stats(4500, 40, 7, 240, 35, 60, 0.32f, false, 7, 1, 0.3f, 3f, 1f, 60f) },
                new Spec { id = "m500", name = "M-500 Revolver", fbx = "M-500_0", category = WeaponCategory.Pistol, template = Template.Pistol, finish = Finish.Chrome, itemId = 103, ammoItemId = PistolAmmo,
                    gripZ = 0.15f, gripY = 0.443f, length = 0.36f, stats = Stats(2200, 50, 5, 240, 45, 80, 0.6f, false, 5, 1, 0.2f, 5f, 1.5f, 70f) },
                new Spec { id = "revolver_golden", name = "Golden Revolver", fbx = "Revolver Golden", category = WeaponCategory.Pistol, template = Template.Pistol, finish = Finish.Golden, itemId = 104, ammoItemId = PistolAmmo,
                    gripZ = 0.136f, gripY = 0.462f, length = 0.36f, stats = Stats(6000, 50, 6, 240, 50, 85, 0.6f, false, 6, 1, 0.2f, 5f, 1.5f, 70f) },

                // SMGs
                new Spec { id = "carbine", name = "Carbine SMG", fbx = "Carbine", category = WeaponCategory.SMG, template = Template.Rifle, finish = Finish.Polymer, itemId = 110, ammoItemId = RifleAmmo,
                    gripZ = 0.405f, gripY = 0.347f, length = 0.74f, stats = Stats(2500, 50, 30, 450, 14, 22, 0.075f, true, 30, 1, 1.2f, 0.8f, 0.4f, 45f) },
                new Spec { id = "g36c", name = "G36C", fbx = "G36C_0", category = WeaponCategory.SMG, template = Template.Rifle, finish = Finish.Polymer, itemId = 111, ammoItemId = RifleAmmo,
                    gripZ = 0.364f, gripY = 0.406f, length = 0.72f, stats = Stats(3200, 50, 30, 450, 16, 24, 0.085f, true, 30, 1, 1f, 0.9f, 0.4f, 55f) },

                // Assault rifles
                new Spec { id = "ak47", name = "AK-47", fbx = "AK47", exclude = new[] { "Barrel" }, category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Wood, itemId = 120, ammoItemId = RifleAmmo,
                    gripZ = 0.324f, gripY = 0.457f, length = 0.88f, stats = Stats(3500, 60, 30, 450, 22, 32, 0.11f, true, 30, 1, 0.9f, 1.4f, 0.5f, 80f) },
                new Spec { id = "m16a4", name = "M16A4", fbx = "M16A4_0", category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Gunmetal, itemId = 121, ammoItemId = RifleAmmo,
                    gripZ = 0.256f, gripY = 0.406f, length = 0.98f, stats = Stats(4000, 60, 30, 450, 20, 30, 0.1f, true, 30, 1, 0.6f, 1f, 0.3f, 90f) },
                new Spec { id = "scarl", name = "SCAR-L", fbx = "Scar L_1", category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Polymer, itemId = 122, ammoItemId = RifleAmmo,
                    gripZ = 0.35f, gripY = 0.368f, length = 0.9f, stats = Stats(4800, 60, 30, 450, 24, 34, 0.105f, true, 30, 1, 0.6f, 1.1f, 0.35f, 90f) },
                new Spec { id = "famas", name = "FAMAS", fbx = "Famas_0", category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Gunmetal, itemId = 123, ammoItemId = RifleAmmo,
                    gripZ = 0.439f, gripY = 0.316f, length = 0.78f, stats = Stats(4200, 60, 25, 450, 18, 28, 0.075f, true, 25, 1, 0.8f, 1f, 0.4f, 75f) },
                new Spec { id = "f2000", name = "F2000", fbx = "F2000", category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Polymer, itemId = 124, ammoItemId = RifleAmmo,
                    gripZ = 0.487f, gripY = 0.383f, length = 0.72f, stats = Stats(5200, 60, 30, 450, 20, 30, 0.08f, true, 30, 1, 0.7f, 0.9f, 0.3f, 80f) },
                new Spec { id = "golden_ak", name = "Golden AK", fbx = "Barrel Golden", exclude = new[] { "Barrel" }, category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Golden, itemId = 125, ammoItemId = RifleAmmo,
                    gripZ = 0.302f, gripY = 0.596f, length = 1.0f, stats = Stats(12000, 60, 35, 450, 28, 38, 0.1f, true, 35, 1, 0.6f, 1.2f, 0.4f, 90f) },
                new Spec { id = "dollar_gun", name = "Dollar Gun", fbx = "Dollar Gun", category = WeaponCategory.AssaultRifle, template = Template.Rifle, finish = Finish.Golden, itemId = 126, ammoItemId = RifleAmmo,
                    gripZ = 0.302f, gripY = 0.418f, length = 0.95f, stats = Stats(25000, 60, 40, 450, 30, 42, 0.09f, true, 40, 1, 0.5f, 1f, 0.3f, 100f) },

                // Shotguns
                new Spec { id = "double_barrel", name = "Double Barrel", fbx = "Double Barrel_0", category = WeaponCategory.Shotgun, template = Template.Shotgun, finish = Finish.Gunmetal, itemId = 130, ammoItemId = ShotgunAmmo,
                    gripZ = 0.081f, gripY = 0.446f, length = 0.86f, stats = Stats(2000, 40, 8, 120, 8, 20, 0.9f, false, 8, 8, 4f, 4f, 2f, 25f) },
                new Spec { id = "golden_shotgun", name = "Golden Shotgun", fbx = "Double Barrel Golden", category = WeaponCategory.Shotgun, template = Template.Shotgun, finish = Finish.Golden, itemId = 131, ammoItemId = ShotgunAmmo,
                    gripZ = 0.116f, gripY = 0.264f, length = 0.86f, euler = new Vector3(12f, 0f, 0f), stats = Stats(7500, 40, 8, 120, 10, 24, 0.9f, false, 8, 8, 4f, 4f, 2f, 28f) },
                new Spec { id = "high_caliber", name = "High Caliber", fbx = "High Caliber", category = WeaponCategory.Shotgun, template = Template.Shotgun, finish = Finish.Gunmetal, itemId = 132, ammoItemId = ShotgunAmmo,
                    gripZ = 0.302f, gripY = 0.429f, length = 0.8f, stats = Stats(5500, 40, 10, 120, 10, 22, 0.45f, false, 10, 8, 3.5f, 3f, 1.5f, 30f) },

                // Sniper
                new Spec { id = "awm", name = "AWM Sniper", fbx = "AWP_0", category = WeaponCategory.Sniper, template = Template.Sniper, finish = Finish.Polymer, itemId = 140, ammoItemId = SniperAmmo,
                    gripZ = 0.244f, gripY = 0.277f, length = 1.15f, stats = Stats(9000, 120, 5, 60, 90, 150, 1.2f, false, 5, 1, 0f, 6f, 1f, 150f) },

                // Heavy
                new Spec { id = "rpg", name = "RPG", fbx = "RPG", category = WeaponCategory.Heavy, template = Template.Rpg, finish = Finish.Polymer, itemId = 150, ammoItemId = RpgAmmo,
                    gripZ = 0.56f, gripY = 0.396f, secondGripZ = 0.44f, length = 1.1f, stats = Stats(15000, 500, 1, 10, 900, 1400, 1f, false, 1, 1, 0f, 8f, 8f, 120f) },

                // No melee weapons are sold (the axe was removed; Template.Axe stays available for future melee specs).
            };
            return list;
        }

        // ---------------------------------------------------------------- cameras and scope

        /// <summary>Background zoom of the GTA-style sniper scope: Invector's scope camera FOV is 60 - this (60 - 44 = 16 degrees).</summary>
        private const float SniperScopeZoom = 44f;

        /// <summary>
        /// The FBX DATA GUN models contain cameras (imported with the model). Enabled, they render the whole screen from inside
        /// the gun as soon as the weapon is equipped (they draw after the player camera), so they are switched off.
        /// </summary>
        private static int DisableImportedCameras(GameObject model)
        {
            int count = 0;
            foreach (Camera camera in model.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.enabled) continue;
                camera.enabled = false;
                count++;
            }
            return count;
        }

        /// <summary>
        /// GTA-style full-screen scope: Invector's scope background camera renders the zoomed world full screen from a point
        /// on the bore axis (so the reticle centre is exactly where the bullet goes at any distance) and the project's
        /// SniperScopeView draws the mask and reticle. Invector's render-texture lens is not used: its zoom camera sat inside the
        /// barrel and only showed the muzzle, so it is switched off (it also rendered every frame while the rifle was drawn).
        /// </summary>
        private static void ConfigureSniperScope(vShooterWeapon shooter)
        {
            shooter.useUI = false;
            shooter.onlyUseScopeUIView = false;
            shooter.backGroundScopeZoom = SniperScopeZoom;
            if (shooter.zoomScopeCamera != null) shooter.zoomScopeCamera.enabled = false;
            if (shooter.scopeTarget != null && shooter.muzzle != null)
            {
                Transform root = shooter.transform;
                Vector3 muzzle = root.InverseTransformPoint(shooter.muzzle.position);
                Vector3 scope = root.InverseTransformPoint(shooter.scopeTarget.position);
                Vector3 forward = root.InverseTransformDirection(shooter.muzzle.forward);
                shooter.scopeTarget.position = root.TransformPoint(new Vector3(muzzle.x, muzzle.y, scope.z));
                shooter.scopeTarget.rotation = Quaternion.LookRotation(root.TransformDirection(forward), root.up);
            }
        }

        /// <summary>Applies the camera and sniper scope fixes to the existing weapon prefabs (no rebuild).</summary>
        [MenuItem("Tools/OpenworldCrime/Weapons/Fix Weapon Cameras And Sniper Scope")]
        public static void FixCamerasMenu() => Debug.Log(FixCameras());

        public static string FixCameras()
        {
            var log = new StringBuilder("[WeaponBuilder] cameras / scope\n");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { WeaponFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int disabled = 0;
                    Transform model = root.transform.Find("Model");
                    if (model != null) disabled = DisableImportedCameras(model.gameObject);
                    var shooter = root.GetComponent<vShooterWeapon>();
                    bool sniper = shooter != null && path.EndsWith("awm_Inventory.prefab");
                    if (sniper) ConfigureSniperScope(shooter);
                    if (disabled > 0 || sniper)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        log.AppendLine($"  {path}: {disabled} imported camera(s) off{(sniper ? ", GTA scope" : string.Empty)}");
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return log.ToString();
        }

        [MenuItem("Tools/OpenworldCrime/Weapons/Build Weapons From Catalog")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[WeaponBuilder]\n");
            foreach (string folder in new[] { "Assets/_Project/Data/Weapons", WeaponFolder, DisplayFolder, MaterialFolder, IconFolder }) EnsureFolder(folder);

            var itemList = AssetDatabase.LoadAssetAtPath<vItemListData>(ItemListPath);
            var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBulletPath);
            if (itemList == null || bullet == null) return log.Append("Item list or PlayerBullet missing.").ToString();

            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            Material scopeMaterial = GetMaterial("WPN_ScopeGlass", Finish.Polymer, true);
            var definitions = new List<WeaponDefinition>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (Spec spec in Specs())
                {
                    string weaponPath = spec.existingPrefab ?? WeaponFolder + "/" + spec.id + "_Inventory.prefab";
                    if (spec.existingPrefab == null) BuildWeaponPrefab(spec, weaponPath, preview, bullet, scopeMaterial, log);
                    GameObject display = BuildDisplayPrefab(spec, weaponPath, preview);
                    Sprite icon = RenderIcon(spec.id, display, preview);
                    vItem item = UpsertItem(itemList, spec, weaponPath, icon);

                    WeaponDefinition def = spec.stats;
                    def.id = spec.id;
                    def.displayName = spec.name;
                    def.category = spec.category;
                    def.itemId = spec.itemId;
                    def.ammoItemId = spec.ammoItemId;
                    def.icon = icon;
                    def.displayPrefab = display;
                    definitions.Add(def);
                    log.AppendLine($"  {spec.name}: item {item.id}, {weaponPath}");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            log.AppendLine(BuildIKAdjustList(Specs()));

            catalog.EditorWeapons.Clear();
            catalog.EditorWeapons.AddRange(definitions);
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(itemList);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  catalog: {definitions.Count} weapons -> {CatalogPath}");
            return log.ToString();
        }

        // ---------------------------------------------------------------- weapon prefab

        private static void BuildWeaponPrefab(Spec spec, string path, Scene preview, GameObject bullet, Material scopeMaterial, StringBuilder log)
        {
            TemplateInfo info = Templates[spec.template];
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(info.prefabPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(template, preview);
            root.name = spec.id + "_Inventory";
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The template's own support-hand fit, measured on its mesh before it is hidden: how far below the handguard and
            // beside the gun its left-hand IK target sits. New guns reuse that relation on their own handguard.
            var templateShooter = root.GetComponent<vShooterWeapon>();
            Vector3 templateLh = templateShooter != null && templateShooter.handIKTarget != null ? templateShooter.handIKTarget.position : Vector3.zero;
            Vector3 templateMuzzle = templateShooter != null && templateShooter.muzzle != null ? templateShooter.muzzle.position : Vector3.zero;
            var templateVerts = new List<Vector3>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == null || mr.bounds.size.magnitude < 0.05f) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices) templateVerts.Add(m.MultiplyPoint3x4(v));
            }
            float templateGuardBottom = SliceBottom(templateVerts, templateLh.z, templateMuzzle.y);
            float templateGuardLeft = SliceLeft(templateVerts, templateLh.z, templateMuzzle.y);

            // Hide the template's own gun meshes (components only: muzzle, particles and audio live under them).
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = false;

            // Grip first: the model's grip point sits where the template's grip is (the weapon origin, inside the animated
            // fist), so the right palm wraps the grip and the index finger reaches the trigger like on the template.
            GameObject model = AddFittedModel(spec, root.transform, scopeMaterial, out float rawLength);
            model.transform.position += GripAnchor + spec.handOffset;
            Bounds bounds = MeshBounds(model);
            supportFit = new SupportFit { templateLh = templateLh, guardDrop = templateGuardBottom - templateLh.y, sideGap = templateLh.x - templateGuardLeft };

            var shooter = root.GetComponent<vShooterWeapon>();
            if (shooter != null) ConfigureShooter(spec, info, shooter, model, bounds, rawLength, bullet);
            var melee = root.GetComponent<vMeleeWeapon>();
            if (melee != null && spec.category == WeaponCategory.Melee)
            {
                melee.damage.damageValue = spec.stats.maxDamage;
                foreach (var hitbox in root.GetComponentsInChildren<vHitBox>(true)) FitHitbox(hitbox, bounds);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static GameObject AddFittedModel(Spec spec, Transform parent, Material scopeMaterial, out float rawLength)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxFolder + spec.fbx + ".fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            DisableImportedCameras(model);

            Material material = GetMaterial("WPN_" + spec.id, spec.finish, false);
            var kept = new List<Renderer>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (spec.exclude.Contains(r.name)) { r.gameObject.SetActive(false); continue; }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : string.Empty;
                    mats[i] = n.StartsWith("scope") || n.StartsWith("aim") ? scopeMaterial : material;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                kept.Add(r);
            }

            // Raw bounds in model space (model at identity under an identity root at the origin).
            Bounds raw = kept[0].bounds;
            foreach (Renderer r in kept) raw.Encapsulate(r.bounds);
            rawLength = raw.size.z;
            Vector3 grip = new Vector3(raw.center.x, raw.min.y + raw.size.y * spec.gripY, raw.min.z + raw.size.z * spec.gripZ);
            float scale = spec.length / raw.size.z;
            Quaternion rotation = Quaternion.Euler(spec.euler);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localRotation = rotation;
            model.transform.localPosition = -(rotation * (grip * scale));
            return model;
        }

        private static void ConfigureShooter(Spec spec, TemplateInfo info, vShooterWeapon shooter, GameObject model, Bounds bounds, float rawLength, GameObject bullet)
        {
            WeaponDefinition s = spec.stats;
            Transform root = shooter.transform;

            // Muzzle at the barrel tip; flash / light / particles move with it.
            Vector3 muzzle = MuzzlePoint(model, bounds);
            Vector3 oldMuzzle = shooter.muzzle != null ? shooter.muzzle.position : muzzle;
            Vector3 delta = muzzle - oldMuzzle;
            if (shooter.muzzle != null) shooter.muzzle.position = muzzle;
            var moved = new HashSet<Transform>();
            if (shooter.lightOnShot != null) MoveOnce(shooter.lightOnShot.transform, delta, moved);
            if (shooter.emittShurykenParticle != null)
                foreach (ParticleSystem ps in shooter.emittShurykenParticle)
                    if (ps != null) MoveOnce(ps.transform.parent != null && ps.transform.parent.name == "Particles" ? ps.transform.parent : ps.transform, delta, moved);

            // Support hand under the handguard (rifles / shotguns / sniper) or on the RPG's front grip.
            if (shooter.handIKTarget != null)
            {
                Vector3 lh = shooter.handIKTarget.position;
                if (spec.template == Template.Rpg && spec.secondGripZ >= 0f)
                {
                    // Second grip of the launcher, same height as the trigger grip.
                    lh.z = (spec.secondGripZ - spec.gripZ) * spec.length + GripAnchor.z;
                    lh.x += GripAnchor.x;
                }
                else if (info.leftHandAlong > 0f)
                {
                    // On the handguard, within the arm's comfortable reach of the template, at the template's distance below and
                    // beside the handguard (palm under it, fingers wrapping the side).
                    var verts = new List<Vector3>(Vertices(model));
                    float reachZ = supportFit.templateLh.z + GripAnchor.z;
                    lh.z = Mathf.Clamp(bounds.max.z * info.leftHandAlong, reachZ - 0.03f, reachZ + 0.08f);
                    lh.y = SliceBottom(verts, lh.z, muzzle.y) - supportFit.guardDrop;
                    lh.x = SliceLeft(verts, lh.z, muzzle.y) + supportFit.sideGap;
                }
                else
                {
                    // Pistols: the template's two-hand cup, moved with the grip, slightly lower so the left palm cups the grip
                    // under the right hand and its fingers wrap the right fingers instead of poking above them.
                    lh += PistolCupOffset;
                }
                shooter.handIKTarget.position = lh + spec.supportOffset;
            }

            // Aim alignment rotates the hand so the aim reference's forward points at the crosshair from the aim reference's
            // position: keep it on the bore line so the muzzle points at the crosshair even at close range.
            if (shooter.aimReference != null)
            {
                Vector3 a = shooter.aimReference.position;
                shooter.aimReference.position = new Vector3(muzzle.x, muzzle.y, a.z);
            }

            if (spec.template == Template.Sniper) ConfigureSniperScope(shooter);

            shooter.projectile = spec.template == Template.Rpg ? GetRocket() : bullet;
            shooter.minDamage = s.minDamage;
            shooter.maxDamage = s.maxDamage;
            shooter.shootFrequency = s.shotInterval;
            shooter.automaticWeapon = s.automatic;
            shooter.clipSize = s.clipSize;
            shooter.ammo = s.clipSize;
            shooter.projectilesPerShot = s.pelletsPerShot;
            shooter.dispersion = s.spread;
            shooter.recoilUp = s.recoilUp;
            shooter.recoilLeft = -s.recoilSide;
            shooter.recoilRight = s.recoilSide;
            shooter.maxDamageDistance = s.range;
            shooter.minDamageDistance = Mathf.Min(s.range * 0.3f, shooter.minDamageDistance);
            shooter.ammoID = spec.ammoItemId;
            shooter.weaponCategory = IKCategory(spec);
            shooter.hitLayer = shooter.hitLayer.value == 0 ? (LayerMask)(1 << 0) : shooter.hitLayer;
            EditorUtility.SetDirty(shooter);
        }

        // Invector's RPG rocket only damages the Player layer with its blast; this variant also hurts peds, cars and props
        // (GTA: one rocket wrecks a car) and pushes vehicles.
        private static GameObject GetRocket()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RocketPath);
            var source = existing != null ? existing : AssetDatabase.LoadAssetAtPath<GameObject>(InvectorRocketPath);
            GameObject root = existing != null ? PrefabUtility.LoadPrefabContents(RocketPath) : (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = "PlayerRocket";
                int damageMask = LayerMask.GetMask("Default", "Player", "Enemy", "BodyPart", "Vehicle", "VehicleHitbox");
                int forceMask = LayerMask.GetMask("Default", "Traffic AI", "BodyPart", "Vehicle");
                foreach (var explosive in root.GetComponentsInChildren<Invector.vExplosive>(true))
                {
                    var so = new SerializedObject(explosive);
                    if (so.FindProperty("maxExplosionRadius").floatValue <= 0f) continue; // the visual-only one
                    so.FindProperty("applyDamageLayer").intValue = damageMask;
                    so.FindProperty("applyForceLayer").intValue = forceMask;
                    so.FindProperty("minExplosionRadius").floatValue = 2.5f;
                    so.FindProperty("maxExplosionRadius").floatValue = 7f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (existing != null) PrefabUtility.SaveAsPrefabAsset(root, RocketPath);
                else PrefabUtility.SaveAsPrefabAsset(root, RocketPath);
            }
            finally
            {
                if (existing != null) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(RocketPath);
        }

        // ---------------------------------------------------------------- per-weapon IK adjusts

        private const string IKFolder = "Assets/_Project/Data/Weapons/IK";
        public const string IKListPath = IKFolder + "/OWC_WeaponIKAdjustList.asset";
        private const string VBotIKFolder = "Assets/Invector-3rdPersonController/Shooter/Editor/Resources/Vbot2.0_IKAdjusts/";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";

        /// <summary>Each weapon gets its own Invector IK category, so its hand / hint / spine offsets can be tuned alone.</summary>
        private static string IKCategory(Spec spec) => "OWC_" + spec.id;

        /// <summary>
        /// One vWeaponIKAdjust per weapon (Standing, StandingAiming, Crouching, CrouchingAiming), seeded once from Invector's
        /// vBot adjust of the matching weapon type and then tuned per weapon (the builder never overwrites an existing asset),
        /// collected in our own vWeaponIKAdjustList, which is assigned to Player_Jason.
        /// </summary>
        private static string BuildIKAdjustList(List<Spec> specs)
        {
            EnsureFolder(IKFolder);
            var list = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(IKListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<vWeaponIKAdjustList>();
                AssetDatabase.CreateAsset(list, IKListPath);
            }
            list.weaponIKAdjusts.Clear();
            foreach (Spec spec in specs)
            {
                if (spec.category == WeaponCategory.Melee) continue;
                string path = IKFolder + "/IK_" + spec.id + ".asset";
                var adjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(path);
                if (adjust == null)
                {
                    var seed = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(VBotIKFolder + VBotSeed(spec.template));
                    adjust = seed != null ? Object.Instantiate(seed) : ScriptableObject.CreateInstance<vWeaponIKAdjust>();
                    AssetDatabase.CreateAsset(adjust, path);
                }
                adjust.weaponCategories = new List<string> { IKCategory(spec) };
                adjust.AddDefaultStates();
                EditorUtility.SetDirty(adjust);
                list.weaponIKAdjusts.Add(adjust);

                if (spec.existingPrefab != null) SetExistingCategory(spec);
            }
            EditorUtility.SetDirty(list);

            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var manager = player.GetComponent<vShooterManager>();
                var so = new SerializedObject(manager);
                so.FindProperty("weaponIKAdjustList").objectReferenceValue = list;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            return $"  IK adjust list: {list.weaponIKAdjusts.Count} weapons -> {IKListPath} (assigned to Player_Jason)";
        }

        private static string VBotSeed(Template template)
        {
            switch (template)
            {
                case Template.Pistol: return "Pistols@vBot.asset";
                case Template.Shotgun: return "Shotguns@vBot.asset";
                case Template.Rpg: return "RPGs@vBot.asset";
                default: return "Rifles@vBot.asset";
            }
        }

        private static void SetExistingCategory(Spec spec)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(spec.existingPrefab);
            try
            {
                var shooter = root.GetComponentInChildren<vShooterWeapon>(true);
                if (shooter == null || shooter.weaponCategory == IKCategory(spec)) return;
                shooter.weaponCategory = IKCategory(spec);
                PrefabUtility.SaveAsPrefabAsset(root, spec.existingPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Where the weapon's grip point goes in the weapon handler's space, measured on Player_Jason's right hand
        // (finger curl centre / web of the hand, Captures/GunShop/hands_v2/_iter): the templates' grips sit ~12 cm behind
        // and 3 cm left of Jason's palm, which put every fist in front of the grip.
        private static readonly Vector3 GripAnchor = new Vector3(0.033f, 0f, 0.12f);
        private static readonly Vector3 PistolCupOffset = new Vector3(0.01f, -0.02f, 0.12f);

        private struct SupportFit
        {
            public Vector3 templateLh;
            public float guardDrop;
            public float sideGap;
        }

        private static SupportFit supportFit;

        // Bottom / left side of the gun body at a slice along the barrel, ignoring parts hanging more than 12 cm below the
        // bore (magazines, grips, launchers), i.e. the handguard the support hand holds.
        private static float SliceBottom(List<Vector3> verts, float z, float boreY)
        {
            float min = float.MaxValue;
            foreach (Vector3 v in verts) if (Mathf.Abs(v.z - z) < 0.03f && v.y > boreY - 0.12f && v.y < min) min = v.y;
            return min == float.MaxValue ? boreY - 0.06f : min;
        }

        private static float SliceLeft(List<Vector3> verts, float z, float boreY)
        {
            float min = float.MaxValue;
            foreach (Vector3 v in verts) if (Mathf.Abs(v.z - z) < 0.03f && v.y > boreY - 0.12f && v.x < min) min = v.x;
            return min == float.MaxValue ? -0.03f : min;
        }

        private static void MoveOnce(Transform t, Vector3 delta, HashSet<Transform> moved)
        {
            if (t == null || !moved.Add(t)) return;
            t.position += delta;
        }

        private static void FitHitbox(vHitBox hitbox, Bounds bounds)
        {
            var box = hitbox.GetComponent<BoxCollider>();
            if (box == null) return;
            // Blade end of the axe: the last third of the model.
            hitbox.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z - bounds.size.z * 0.15f);
            hitbox.transform.rotation = Quaternion.identity;
            box.center = Vector3.zero;
            Vector3 lossy = hitbox.transform.lossyScale;
            box.size = new Vector3(Mathf.Max(0.05f, bounds.size.x) / lossy.x, Mathf.Max(0.05f, bounds.size.y) / lossy.y, bounds.size.z * 0.3f / lossy.z);
        }

        // ---------------------------------------------------------------- mesh helpers

        private static IEnumerable<Vector3> Vertices(GameObject model)
        {
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices) yield return m.MultiplyPoint3x4(v);
            }
        }

        private static Bounds MeshBounds(GameObject model)
        {
            Renderer[] rs = model.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static Vector3 MuzzlePoint(GameObject model, Bounds bounds)
        {
            float slice = Mathf.Max(0.006f, bounds.size.z * 0.015f);
            Vector3 sum = Vector3.zero;
            int count = 0;
            float top = float.MinValue;
            foreach (Vector3 v in Vertices(model))
            {
                if (v.z < bounds.max.z - slice) continue;
                sum += v;
                count++;
                if (v.y > top) top = v.y;
            }
            Vector3 avg = count > 0 ? sum / count : bounds.center;
            // The bore is the top tube at the muzzle: shotgun magazine tubes / under-barrel parts end in the same plane lower down.
            float boreY = count > 0 ? Mathf.Max(avg.y, top - 0.012f) : avg.y;
            return new Vector3(avg.x, boreY, bounds.max.z + 0.01f);
        }

        // ---------------------------------------------------------------- display prefab, icons, materials, items

        // Visual-only copy of the weapon (renderers that are enabled), grip at the origin.
        private static GameObject BuildDisplayPrefab(Spec spec, string weaponPath, Scene preview)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            var copy = (GameObject)Object.Instantiate(weapon);
            SceneManager.MoveGameObjectToScene(copy, preview);
            copy.name = spec.id + "_Display";
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                bool keep = (r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.magnitude > 0.03f;
                if (!keep) Object.DestroyImmediate(r);
            }
            // Strip every behaviour, collider, light, audio and particle: only transforms, mesh filters and renderers remain.
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                    if (c != null && !(c is Transform) && !(c is MeshFilter) && !(c is Renderer)) Object.DestroyImmediate(c);
            foreach (MeshFilter mf in copy.GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponent<Renderer>() == null) Object.DestroyImmediate(mf);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                if (t != null && t != copy.transform && !t.gameObject.activeSelf) Object.DestroyImmediate(t.gameObject);

            string path = DisplayFolder + "/" + spec.id + "_Display.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
            Object.DestroyImmediate(copy);
            return saved;
        }

        private static Sprite RenderIcon(string id, GameObject display, Scene preview)
        {
            const int width = 512, height = 256;
            var lightGo = new GameObject("Icon Light");
            SceneManager.MoveGameObjectToScene(lightGo, preview);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -50f, 0f);
            var fillGo = new GameObject("Icon Fill");
            SceneManager.MoveGameObjectToScene(fillGo, preview);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.7f;
            fillGo.transform.rotation = Quaternion.Euler(-20f, 120f, 0f);

            var model = (GameObject)Object.Instantiate(display);
            SceneManager.MoveGameObjectToScene(model, preview);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds b = MeshBounds(model);

            var camGo = new GameObject("Icon Camera");
            SceneManager.MoveGameObjectToScene(camGo, preview);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = preview;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.aspect = (float)width / height;
            cam.orthographicSize = Mathf.Max(b.size.z / 0.9f / cam.aspect, b.size.y / 0.85f) * 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            camGo.transform.SetPositionAndRotation(b.center + Vector3.right * 3f, Quaternion.LookRotation(Vector3.left, Vector3.up));

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            string path = IconFolder + "/" + id + "_Icon.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(model);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(fillGo);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Material GetMaterial(string name, Finish finish, bool scope)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material; // keep any texture assigned later
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            Color color;
            float metallic, smoothness;
            if (scope) { color = new Color(0.03f, 0.035f, 0.04f); metallic = 0.3f; smoothness = 0.85f; }
            else switch (finish)
            {
                case Finish.Golden: color = new Color(1f, 0.74f, 0.3f); metallic = 1f; smoothness = 0.72f; break;
                case Finish.Chrome: color = new Color(0.82f, 0.84f, 0.88f); metallic = 1f; smoothness = 0.82f; break;
                case Finish.Wood: color = new Color(0.36f, 0.22f, 0.12f); metallic = 0.25f; smoothness = 0.4f; break;
                case Finish.Polymer: color = new Color(0.11f, 0.115f, 0.12f); metallic = 0.35f; smoothness = 0.42f; break;
                default: color = new Color(0.2f, 0.21f, 0.23f); metallic = 0.75f; smoothness = 0.5f; break;
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static vItem UpsertItem(vItemListData list, Spec spec, string weaponPath, Sprite icon)
        {
            vItem item = list.items.Find(i => i != null && i.id == spec.itemId);
            if (item == null)
            {
                vItem source = list.items.Find(i => i != null && i.id == Templates[spec.template].itemId);
                item = Object.Instantiate(source);
                item.id = spec.itemId;
                item.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(item, list);
                list.items.Add(item);
            }
            item.name = spec.name;
            item.icon = icon;
            if (spec.existingPrefab == null)
            {
                item.originalObject = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
                item.dropObject = null;
                item.canBeDroped = false;
                item.description = spec.name;
            }
            SetAttribute(item, vItemAttributes.Damage, spec.stats.maxDamage);
            if (spec.ammoItemId >= 0) SetAttribute(item, vItemAttributes.AmmoCount, spec.stats.clipSize);
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void SetAttribute(vItem item, vItemAttributes attribute, int value)
        {
            vItemAttribute a = item.GetItemAttribute(attribute);
            if (a != null) a.value = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
