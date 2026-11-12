using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Economy
{
    /// <summary>Everything the game persists. Plain data; owned by <see cref="SaveManager"/>.</summary>
    [Serializable]
    public class GameSaveData
    {
        public int version = 1;
        public bool hasMoney;
        public int money;
        public bool hasArsenal;
        [Tooltip("Invector item IDs of owned weapons.")]
        public List<int> ownedWeapons = new List<int>();
        public List<IntPair> reserveAmmo = new List<IntPair>();
        public List<IntPair> clipAmmo = new List<IntPair>();
    }

    [Serializable]
    public struct IntPair
    {
        public int key;
        public int value;
        public IntPair(int key, int value) { this.key = key; this.value = value; }
    }

    /// <summary>
    /// Central save owner (global manager under ///Manager///, Singleton because the save data is needed from every scene,
    /// including the additive interiors). Keeps one JSON blob in a single PlayerPrefs key; systems change <see cref="Data"/>
    /// and call <see cref="RequestSave"/>. Writes are batched to the end of the frame and flushed on pause/quit.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class SaveManager : MonoBehaviour
    {
        private const string PrefsKey = "OWC_SaveData";

        public static SaveManager Instance { get; private set; }

        public GameSaveData Data { get; private set; }

        private bool dirty;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(SaveManager)}] A second SaveManager on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
            Load();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Flush();
            Instance = null;
        }

        private void LateUpdate()
        {
            if (dirty) Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationQuit() => Flush();

        public void RequestSave() => dirty = true;

        /// <summary>Deletes the save and starts fresh (used by the editor menu / debug).</summary>
        public void ResetSave()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            Data = new GameSaveData();
            dirty = false;
        }

        private void Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            Data = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { Data = JsonUtility.FromJson<GameSaveData>(json); }
                catch (Exception e) { Debug.LogWarning($"[{nameof(SaveManager)}] Save data could not be read, starting fresh. {e.Message}", this); }
            }
            if (Data == null) Data = new GameSaveData();
        }

        private void Flush()
        {
            dirty = false;
            if (Data == null) return;
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/OpenworldCrime/Save/Delete Save Data")]
        private static void DeleteSaveMenu()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            Debug.Log("[SaveManager] Save data deleted.");
        }
#endif
    }
}
