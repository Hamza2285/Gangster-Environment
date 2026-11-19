using System.Collections;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

// A beach food cart that sells a meal: walking up to its order spot shows "Press E to buy food ($20)" (BUY FOOD on phones).
// Buying: the player turns to the counter, reaches up with one hand for the food (the vendor serves), then takes three bites;
// every bite refills a third of the missing health. The price is taken when the food is handed over. At full health the meal is
// still bought and eaten (nothing to refill); without the cash a short message says why.
// Lives on the cart's "Order Point" (a trigger on the Triggers layer, where the customer stands), placed by FoodStallSetup.
[DisallowMultipleComponent]
public class FoodStall : MonoBehaviour, IPlayerInteractable
{
    public int price = 20;
    [Tooltip("Where the hand reaches when ordering: the front edge of the counter.")]
    public Transform counterPoint;
    [Tooltip("The vendor behind the cart (plays a talk move while serving).")]
    public Animator vendor;
    public AudioClip serveClip;

    [Header("Eating")]
    [Tooltip("Humanoid clip of the left hand lifting food to the mouth (the drinking clip). Only Eat Start to Eat End is played, " +
             "at Eat Speed. Empty = the hand is lifted by IK instead.")]
    public AnimationClip eatClip;
    public float eatStart = 1.6f;
    public float eatEnd = 6.0f;
    [Tooltip("Playback speed of the eating clip (it is a slow, long clip).")]
    public float eatSpeed = 1.8f;
    [Tooltip("Clip time (seconds) during which the hand is at the mouth: health is refilled in Bites steps over it.")]
    public Vector2 eatAtMouth = new Vector2(2.3f, 5.3f);

    [Header("Timing (seconds)")]
    public float turnTime = 0.35f;
    public float reachTime = 0.55f;
    public float holdAtCounter = 0.45f;
    public float biteUp = 0.35f, biteHold = 0.35f, biteDown = 0.3f;
    public int bites = 3;

    static FoodStall busy;
    static Material bunMaterial, sausageMaterial;

    public string InteractionPrompt => "Buy food ($" + price + ")";
    public static bool IsBusy => busy != null;

    public bool CanInteract(OpenworldCrime.Player.PlayerManager player)
    {
        return isActiveAndEnabled && busy == null && player != null && !player.IsInVehicle && !player.IsInputLocked
               && (player.Health == null || !player.Health.IsDead);
    }

    public void Interact(OpenworldCrime.Player.PlayerManager player)
    {
        if (!CanInteract(player)) return;
        var health = player.Health;
        var money = MoneyManager.Instance;
        if (money == null || money.Balance < price) { Toast.Show("Not enough cash - food costs $" + price); return; }
        StartCoroutine(Buy(player));
    }

    IEnumerator Buy(OpenworldCrime.Player.PlayerManager player)
    {
        busy = this;
        player.SetInputLocked(true);
        player.SetMovementLocked(true);
        var weapons = player.GetComponent<PlayerWeaponController>();
        int savedSlot = weapons != null && weapons.HasWeapon ? weapons.GetCurrentSlotIndex() : -1;
        if (savedSlot >= 0) weapons.Unarm(true);

        Transform body = player.transform;
        Animator animator = player.GetComponentInChildren<Animator>();
        PlayerArmReach arm = PlayerArmReach.For(animator);
        GameObject food = null;

        // Face the counter.
        Vector3 toCounter = (counterPoint != null ? counterPoint.position : transform.position + transform.forward) - body.position;
        toCounter.y = 0f;
        if (toCounter.sqrMagnitude > 0.01f)
        {
            Quaternion from = body.rotation, to = Quaternion.LookRotation(toCounter);
            var rb = body.GetComponent<Rigidbody>();
            for (float t = 0f; t < turnTime; t += Time.deltaTime)
            {
                Quaternion r = Quaternion.Slerp(from, to, t / turnTime);
                body.rotation = r;
                if (rb != null) rb.rotation = r;
                yield return null;
            }
            body.rotation = to;
            if (rb != null) rb.rotation = to;
        }

        if (vendor != null && vendor.HasState(0, Animator.StringToHash("talk1"))) vendor.CrossFadeInFixedTime("talk1", 0.25f);

        // Raise one hand to the counter.
        if (arm != null && counterPoint != null)
        {
            // A raised hand towards the vendor: above the counter, on the right-hand side (the arm does not cross the chest).
            arm.ElbowHint = new Vector3(0.4f, -0.3f, -0.1f);
            arm.Target = counterPoint.position + Vector3.up * 0.5f + body.right * 0.22f;
            yield return Blend(arm, 0f, 1f, reachTime);
        }

        // The hand reaches the stall: the money is handed over (cash goes down now, with the sound and a "-$20" by the cash).
        Pay(body.position);
        if (arm != null && counterPoint != null) yield return new WaitForSeconds(holdAtCounter);

        // The vendor hands the food over.
        if (arm != null && arm.Hand != null) food = MakeFood(arm.Hand);
        if (arm != null) yield return Blend(arm, 1f, 0f, reachTime * 0.8f);

        var health = player.Health;
        bool eaten = false;
        if (eatClip != null && animator != null && animator.isHuman)
        {
            // The eating clip lifts the left hand: the food goes into it.
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (food != null && left != null)
            {
                food.transform.SetParent(left, false);
                food.transform.localPosition = new Vector3(-0.02f, 0.09f, 0.04f);
                food.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            yield return EatWithClip(animator, health, food);
            eaten = true;   // done: skip the IK bites below
        }

        // Bites: hand to mouth, each one heals a share of what is missing.
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        for (int i = 0; i < (eaten ? 0 : bites); i++)
        {
            if (health != null && health.IsDead) break;
            if (arm != null && head != null)
            {
                arm.ElbowHint = new Vector3(0.25f, -0.55f, 0.15f);
                arm.Target = head.position + body.forward * 0.13f - Vector3.up * 0.06f + body.right * 0.02f;
                yield return Blend(arm, 0f, 1f, biteUp, () => head.position + body.forward * 0.13f - Vector3.up * 0.06f + body.right * 0.02f);
                yield return new WaitForSeconds(biteHold);
            }
            if (food != null) food.transform.localScale *= 0.72f;
            if (health != null)
            {
                float missing = health.MaxHealth - health.CurrentHealth;
                int heal = Mathf.CeilToInt(missing / Mathf.Max(1, bites - i));
                if (heal > 0) health.Heal(heal);
            }
            if (arm != null && head != null) yield return Blend(arm, 1f, 0f, biteDown, () => head.position + body.forward * 0.13f - Vector3.up * 0.06f);
        }

        if (food != null) Destroy(food);
        if (arm != null) arm.Weight = 0f;
        if (vendor != null && vendor.HasState(0, Animator.StringToHash("idle1"))) vendor.CrossFadeInFixedTime("idle1", 0.3f);
        if (savedSlot >= 0 && weapons != null) weapons.EquipSlot(savedSlot);
        player.SetMovementLocked(false);
        player.SetInputLocked(false);
        busy = null;
    }

    void Pay(Vector3 at)
    {
        MoneyManager.Instance.TrySpend(price);
        if (serveClip != null) AudioSource.PlayClipAtPoint(serveClip, at + Vector3.up, 0.9f);
        MoneyPopup.Show("-$" + price);
    }

    // Plays Eat Start..Eat End of the eating clip over the player's own animation (a playable graph whose output weight blends it
    // in and out), refilling health in Bites steps while the hand is at the mouth.
    IEnumerator EatWithClip(Animator animator, PlayerHealth health, GameObject food)
    {
        var graph = PlayableGraph.Create("Food Stall Eat");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Eat", animator);
        var clip = AnimationClipPlayable.Create(graph, eatClip);
        clip.SetApplyFootIK(true);
        clip.SetTime(eatStart);
        clip.SetSpeed(eatSpeed);
        output.SetSourcePlayable(clip);
        output.SetWeight(0f);
        graph.Play();
        try
        {
            const float blend = 0.25f;
            int healed = 0;
            float started = Time.time;
            float duration = (eatEnd - eatStart) / Mathf.Max(0.1f, eatSpeed);
            while (true)
            {
                float t = (float)clip.GetTime();
                float elapsed = Time.time - started;
                if (t >= eatEnd || elapsed > duration + 1f || (health != null && health.IsDead)) break;
                float fadeIn = Mathf.Clamp01(elapsed / blend);
                float fadeOut = Mathf.Clamp01((eatEnd - t) / (blend * eatSpeed));
                output.SetWeight(Mathf.Min(fadeIn, fadeOut));

                // Each step through the at-mouth window is a bite: a share of the missing health, the food gets smaller.
                float share = Mathf.InverseLerp(eatAtMouth.x, eatAtMouth.y, t);
                int due = Mathf.Min(bites, Mathf.FloorToInt(share * bites + 0.0001f) + (t >= eatAtMouth.x ? 1 : 0));
                while (healed < due)
                {
                    healed++;
                    if (food != null) food.transform.localScale *= 0.72f;
                    if (health != null)
                    {
                        float missing = health.MaxHealth - health.CurrentHealth;
                        int heal = Mathf.CeilToInt(missing / Mathf.Max(1, bites - healed + 1));
                        if (heal > 0) health.Heal(heal);
                    }
                }
                yield return null;
            }
            // Whatever is left (cut short): the meal still counts in full.
            if (health != null && !health.IsDead && health.CurrentHealth < health.MaxHealth)
                health.Heal(Mathf.CeilToInt(health.MaxHealth - health.CurrentHealth));
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }

    static IEnumerator Blend(PlayerArmReach arm, float from, float to, float seconds, System.Func<Vector3> follow = null)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (follow != null) arm.Target = follow();
            arm.Weight = Mathf.SmoothStep(from, to, t / seconds);
            yield return null;
        }
        if (follow != null) arm.Target = follow();
        arm.Weight = to;
    }

    void OnDisable()
    {
        if (busy == this) busy = null;
    }

    // A hot dog in the right hand, made of two capsules (no food model in the project).
    GameObject MakeFood(Transform hand)
    {
        var root = new GameObject("Hot Dog");
        root.transform.SetParent(hand, false);
        root.transform.localPosition = new Vector3(0.02f, 0.09f, 0.04f);
        root.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var shader = FindShader();
        if (bunMaterial == null) bunMaterial = Colored(shader, new Color(0.86f, 0.62f, 0.32f));
        if (sausageMaterial == null) sausageMaterial = Colored(shader, new Color(0.62f, 0.2f, 0.12f));
        Part(root.transform, bunMaterial, new Vector3(0.055f, 0.085f, 0.055f), Vector3.zero);
        Part(root.transform, sausageMaterial, new Vector3(0.035f, 0.105f, 0.035f), new Vector3(0f, 0f, 0.022f));
        return root;
    }

    Shader FindShader()
    {
        var cartRenderer = GetComponentInParent<Transform>().root.GetComponentInChildren<Renderer>();
        foreach (var r in transform.parent != null ? transform.parent.GetComponentsInChildren<Renderer>() : new Renderer[0])
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null) return r.sharedMaterial.shader;
        return cartRenderer != null && cartRenderer.sharedMaterial != null ? cartRenderer.sharedMaterial.shader : Shader.Find("Universal Render Pipeline/Lit");
    }

    static Material Colored(Shader shader, Color color)
    {
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        return m;
    }

    static void Part(Transform parent, Material material, Vector3 scale, Vector3 offset)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localScale = scale;
        part.transform.localPosition = offset;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    // "-$20" in red just under the cash display (top right), rising and fading out.
    class MoneyPopup : MonoBehaviour
    {
        static MoneyPopup instance;
        Text text;
        RectTransform rect;
        float shownAt = -10f;
        const float Seconds = 1.6f;

        public static void Show(string amount)
        {
            if (instance == null)
            {
                var canvasObject = new GameObject("Food Stall Money Canvas");
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 161;
                var scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                instance = canvasObject.AddComponent<MoneyPopup>();
                instance.text = new GameObject("Amount", typeof(RectTransform)).AddComponent<Text>();
                instance.text.transform.SetParent(canvasObject.transform, false);
                instance.rect = instance.text.rectTransform;
                instance.rect.anchorMin = instance.rect.anchorMax = instance.rect.pivot = new Vector2(1f, 1f);
                instance.rect.sizeDelta = new Vector2(300f, 60f);
                instance.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                instance.text.fontSize = 44;
                instance.text.fontStyle = FontStyle.Bold;
                instance.text.alignment = TextAnchor.MiddleRight;
                instance.text.raycastTarget = false;
                instance.text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            }
            instance.text.text = amount;
            instance.shownAt = Time.unscaledTime;
            instance.enabled = true;
            instance.text.enabled = true;
        }

        void Update()
        {
            float t = (Time.unscaledTime - shownAt) / Seconds;
            if (t >= 1f) { text.enabled = false; enabled = false; return; }
            rect.anchoredPosition = new Vector2(-36f, -290f + 30f * t);
            text.color = new Color(1f, 0.25f, 0.2f, 1f - Mathf.Clamp01((t - 0.6f) / 0.4f));
        }
    }

    // A short message under the top edge of the screen.
    static class Toast
    {
        static Text text;
        static float until;
        static ToastRunner runner;

        public static void Show(string message)
        {
            if (text == null)
            {
                var canvasObject = new GameObject("Food Stall Message Canvas");
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 160;
                var scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
                text.transform.SetParent(canvasObject.transform, false);
                var rect = text.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -150f);
                rect.sizeDelta = new Vector2(1200f, 60f);
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 34;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.raycastTarget = false;
                canvasObject.AddComponent<Outline>();
                text.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
                runner = canvasObject.AddComponent<ToastRunner>();
            }
            text.text = message;
            text.enabled = true;
            until = Time.unscaledTime + 2.5f;
            runner.enabled = true;
        }

        public static void Tick()
        {
            if (text != null && Time.unscaledTime > until) { text.enabled = false; runner.enabled = false; }
        }
    }

    class ToastRunner : MonoBehaviour
    {
        void Update() => Toast.Tick();
    }
}
