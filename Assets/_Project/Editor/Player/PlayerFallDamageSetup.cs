using System.Text;
using OpenworldCrime.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Vehicle falls and car hits on the playable characters (Tools > OpenworldCrime > Player > Set Up Vehicle Fall Damage, city
    /// scene open): adds <see cref="PlayerVehicleFallDamage"/> to Jason and Mia, and sets the car-hit threshold of their
    /// <see cref="PlayerVehicleImpact"/> (now measured as the car's own drive into the player) so a creeping car does not hurt.
    /// </summary>
    public static class PlayerFallDamageSetup
    {
        private const float CarHitMinimumSpeed = 3f; // m/s (about 11 km/h) of the car driving into the player

        [MenuItem("Tools/OpenworldCrime/Player/Set Up Vehicle Fall Damage (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[PlayerFallDamageSetup]\n");
            foreach (PlayableCharacter character in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (EditorUtility.IsPersistent(character)) continue;
                GameObject go = character.gameObject;
                if (go.GetComponent<PlayerVehicleFallDamage>() == null) Undo.AddComponent<PlayerVehicleFallDamage>(go); // Reset fills its references
                var impact = go.GetComponent<PlayerVehicleImpact>();
                if (impact != null)
                {
                    var so = new SerializedObject(impact);
                    so.FindProperty("minimumImpactSpeed").floatValue = CarHitMinimumSpeed;
                    so.ApplyModifiedProperties();
                }
                EditorSceneManager.MarkSceneDirty(go.scene);
                log.AppendLine($"  {go.name}: PlayerVehicleFallDamage, car hits from {CarHitMinimumSpeed} m/s");
            }
            return log.ToString();
        }
    }
}
