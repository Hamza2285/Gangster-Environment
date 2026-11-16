using System.Collections;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.UI;

// When the playing character dies: a WASTED screen, then they wake up outside City Hospital with full health and a hospital
// bill taken from their cash (as much as they have, never below zero). Follows whoever is playing, so it works for every
// character the switcher can hand control to.
public class HospitalRespawn : MonoBehaviour
{
    [Tooltip("Where the player stands up again, facing away from the hospital doors.")]
    public Transform respawnPoint;
    public int hospitalFee = 500;
    [Tooltip("Seconds the WASTED screen shows before the screen closes.")]
    public float wastedSeconds = 3.5f;

    vThirdPersonController watched;
    bool respawning;

    Canvas canvas;
    Image tint;
    Text wasted, bill;
    Font font;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHud();
    }

    void OnDisable()
    {
        Watch(null);
    }

    void Update()
    {
        // The character can change (switch wheel), so keep listening to whoever is playing.
        var player = GamePlayer.Current;
        var controller = player != null ? player.transform.GetComponent<vThirdPersonController>() : null;
        if (controller != watched) Watch(controller);
    }

    void Watch(vThirdPersonController controller)
    {
        if (watched != null) watched.onDead.RemoveListener(OnDead);
        watched = controller;
        if (watched == null) return;
        watched.removeComponentsAfterDie = false;
        watched.onDead.AddListener(OnDead);
    }

    void OnDead(GameObject _)
    {
        if (!respawning && watched != null) StartCoroutine(Respawn(watched));
    }

    IEnumerator Respawn(vThirdPersonController controller)
    {
        respawning = true;

        var mission = FindFirstObjectByType<AmbulanceMission>();
        if (mission != null) mission.Abort("You died");

        // WASTED: fade to a grey-red tint with the big title.
        canvas.gameObject.SetActive(true);
        bill.text = "";
        wasted.color = new Color(0.85f, 0.12f, 0.1f, 0f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.2f)
        {
            tint.color = new Color(0.12f, 0.05f, 0.05f, Mathf.Lerp(0f, 0.55f, t));
            wasted.color = new Color(0.85f, 0.12f, 0.1f, Mathf.Clamp01(t * 1.6f - 0.4f));
            yield return null;
        }
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, wastedSeconds - 1.2f));

        // Black out, then put the body back together outside the hospital.
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f)
        {
            tint.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.55f, 1f, t));
            yield return null;
        }
        tint.color = Color.black;
        wasted.color = Color.clear;

        if (controller != null)
        {
            var interaction = controller.GetComponent<PlayerVehicleInteraction>();
            if (interaction != null && interaction.IsInVehicle)
            {
                interaction.RequestExit();
                for (float t = 0f; t < 3f && interaction.IsInVehicle; t += Time.unscaledDeltaTime) yield return null;
            }
            Revive(controller);
        }

        int charged = ChargeFee();
        bill.text = charged > 0 ? "Hospital bill  <color=#FF5A4A>-$" + charged.ToString("N0") + "</color>" : "Hospital bill waived";
        yield return new WaitForSecondsRealtime(0.4f);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
        {
            tint.color = new Color(0f, 0f, 0f, 1f - t);
            yield return null;
        }
        tint.color = Color.clear;
        yield return new WaitForSecondsRealtime(2.2f);
        canvas.gameObject.SetActive(false);
        respawning = false;
    }

    void Revive(vThirdPersonController controller)
    {
        var ragdoll = controller.GetComponent<vRagdoll>();
        if (ragdoll != null && controller.ragdolled) ragdoll.RestoreRagdoll();
        controller.ragdolled = false;

        controller.ResetHealth();
        controller.isDead = false;

        Vector3 at = respawnPoint != null ? respawnPoint.position : controller.transform.position;
        float yaw = respawnPoint != null ? respawnPoint.eulerAngles.y : controller.transform.eulerAngles.y;
        var player = GamePlayer.Current;
        if (player != null && player.transform == controller.transform) player.Teleport(at, yaw);
        else controller.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

        var body = controller.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
        }
        var capsule = controller.GetComponent<CapsuleCollider>();
        if (capsule != null) capsule.enabled = true;

        var animator = controller.GetComponent<Animator>();
        if (animator != null)
        {
            // The death state has no way out, and its tag locks movement. Rebinding puts every layer back in its default
            // state with default parameters, as on a freshly spawned player.
            // Invector caches each layer's tags and only clears them when a state exits, which Rebind skips, so the
            // cached "Dead" / "LockMovement" tags are cleared first; the default states add their own tags back.
            var infos = controller.animatorStateInfos;
            if (infos != null && infos.stateInfos != null)
            {
                foreach (var info in infos.stateInfos)
                {
                    if (info == null) continue;
                    info.tags.Clear();
                    info.shortPathHash = 0;
                    info.normalizedTime = 0f;
                }
            }
            animator.Rebind();
            animator.Update(0f);
            animator.SetBool("isDead", false);
        }

        // Anything that switched the controls off while dead (input, locks, cursor) comes back on.
        controller.lockMovement = false;
        controller.lockRotation = false;
        controller.lockSetMoveSpeed = false;
        controller.StopCharacter();
        var input = controller.GetComponent<vThirdPersonInput>();
        if (input != null)
        {
            input.enabled = true;
            input.SetLockAllInput(false);
            input.SetLockBasicInput(false);
            input.SetLockCameraInput(false);
        }
        if (player != null)
        {
            player.SetControl(true);
            player.LockCursor(true);
            player.ResetLook();
        }
    }

    int ChargeFee()
    {
        var money = OpenworldCrime.Economy.MoneyManager.Instance;
        if (money == null || hospitalFee <= 0) return 0;
        int amount = Mathf.Min(hospitalFee, money.Balance);
        return amount > 0 && money.TrySpend(amount) ? amount : 0;
    }

    void BuildHud()
    {
        var canvasObject = new GameObject("Hospital Respawn Canvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var tintObject = new GameObject("Tint", typeof(RectTransform));
        tintObject.transform.SetParent(canvasObject.transform, false);
        var tintRect = (RectTransform)tintObject.transform;
        tintRect.anchorMin = Vector2.zero;
        tintRect.anchorMax = Vector2.one;
        tintRect.offsetMin = tintRect.offsetMax = Vector2.zero;
        tint = tintObject.AddComponent<Image>();
        tint.raycastTarget = false;

        wasted = Label("Wasted", canvasObject.transform, "WASTED", 150, new Vector2(0f, 40f));
        var outline = wasted.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(4f, -4f);
        bill = Label("Bill", canvasObject.transform, "", 44, new Vector2(0f, -90f));
        bill.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        canvasObject.SetActive(false);
    }

    Text Label(string name, Transform parent, string text, int size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1600f, size * 1.6f);
        rect.anchoredPosition = position;
        var label = go.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.supportRichText = true;
        label.raycastTarget = false;
        label.color = Color.white;
        return label;
    }
}
