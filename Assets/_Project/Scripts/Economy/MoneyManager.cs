using System;
using UnityEngine;

namespace OpenworldCrime.Economy
{
    /// <summary>
    /// The player's cash (global manager under ///Manager///; Singleton so the shop and range in additive interior scenes
    /// can reach it). Persists through <see cref="SaveManager"/>. UI listens to <see cref="OnMoneyChanged"/>.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public class MoneyManager : MonoBehaviour
    {
        [Tooltip("Cash for a new game (no save yet).")]
        [SerializeField, Min(0)] private int startingMoney = 5000;
        [SerializeField, Min(0)] private int maxMoney = 999999999;

        public static MoneyManager Instance { get; private set; }

        /// <summary>Raised when the balance changes. Args: new balance, change (+ earned / - spent).</summary>
        public static event Action<int, int> OnMoneyChanged;

        public int Balance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(MoneyManager)}] A second MoneyManager on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null)
            {
                Debug.LogWarning($"[{nameof(MoneyManager)}] No SaveManager; money will not be saved.", this);
                Balance = startingMoney;
            }
            else
            {
                if (!save.Data.hasMoney)
                {
                    save.Data.hasMoney = true;
                    save.Data.money = startingMoney;
                    save.RequestSave();
                }
                Balance = save.Data.money;
            }
            OnMoneyChanged?.Invoke(Balance, 0);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool CanAfford(int amount) => amount <= Balance;

        /// <summary>Spends money if the player can afford it. Returns false (nothing changes) otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > Balance) return false;
            SetBalance(Balance - amount, -amount);
            return true;
        }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            SetBalance((int)Mathf.Min((long)Balance + amount, maxMoney), amount);
        }

        private void SetBalance(int value, int delta)
        {
            Balance = value;
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.Data.money = value;
                SaveManager.Instance.RequestSave();
            }
            OnMoneyChanged?.Invoke(Balance, delta);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            OnMoneyChanged = null;
        }
    }
}
