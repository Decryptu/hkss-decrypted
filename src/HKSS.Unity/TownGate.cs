using System;
using System.Reflection;
using GlobalEnums;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HKSS.Unity;

/// <summary>
/// Spawns the inter-game travel gate in Dirtmouth (Hollow Knight) and Bone Bottom (Silksong)
/// with a native "Enter" prompt, a respawn marker for gate-to-gate arrival, and runtime visuals.
/// </summary>
internal sealed class TownGate : MonoBehaviour
{
    internal const string GateObjectName = "HKSS_TownGate";
    internal const string RespawnMarkerName = "HKSS_Gate_Respawn";

#if HOLLOW_KNIGHT
    internal const string TownScene = "Town";
    internal const MapZone TownMapZone = MapZone.TOWN;
#elif SILKSONG
    internal const string TownScene = "Bonetown";
    internal const MapZone TownMapZone = MapZone.BONETOWN;
#endif

    private const int TerrainLayerMask = 1 << 8;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(HeroController), nameof(HeroController.LocateSpawnPoint)),
            prefix: new HarmonyMethod(typeof(TownGate), nameof(BeforeLocateSpawnPoint)));
#if SILKSONG
        MethodInfo? getRespawnInfo = AccessTools.Method(
            typeof(GameManager),
            "GetRespawnInfo",
            new[] { typeof(string).MakeByRefType(), typeof(string).MakeByRefType() });
        if (getRespawnInfo != null)
        {
            harmony.Patch(
                getRespawnInfo,
                prefix: new HarmonyMethod(typeof(TownGate), nameof(BeforeGetRespawnInfo)));
        }
#endif
    }

#if SILKSONG
    // Silksong's GetRespawnInfo validates respawnMarkerName against the static SceneTeleportMap
    // and falls back to Tut_01 if the marker isn't baked into the scene asset. Accept our
    // runtime-spawned Bonetown gate marker so returning from Hollow Knight spawns at the gate.
    private static bool BeforeGetRespawnInfo(GameManager __instance, out string scene, out string marker)
    {
        scene = string.Empty;
        marker = string.Empty;

        PlayerData? pd = __instance?.playerData ?? PlayerData.instance;
        if (pd == null) return true;

        string savedScene = !string.IsNullOrEmpty(pd.tempRespawnScene) ? pd.tempRespawnScene : pd.respawnScene;
        string savedMarker = !string.IsNullOrEmpty(pd.tempRespawnScene) ? pd.tempRespawnMarker : pd.respawnMarkerName;

        if (string.Equals(savedScene, TownScene, StringComparison.Ordinal)
            && string.Equals(savedMarker, RespawnMarkerName, StringComparison.Ordinal))
        {
            scene = TownScene;
            marker = RespawnMarkerName;
            return false;
        }

        return true;
    }
#endif

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private static void BeforeLocateSpawnPoint()
    {
        EnsureGateInScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureGateInScene(scene);
    }

    /// <summary>
    /// Sets the player's respawn point to this game's town gate so the next load or respawn
    /// arrives right at the gate.
    /// </summary>
    internal static void SetGateAsRespawnPoint()
    {
        PlayerData pd = PlayerData.instance;
        if (pd == null) return;

        pd.respawnScene = TownScene;
        pd.respawnMarkerName = RespawnMarkerName;
        pd.respawnType = 0;
        pd.mapZone = TownMapZone;
        pd.atBench = false;

#if SILKSONG
        pd.tempRespawnScene = string.Empty;
        pd.tempRespawnMarker = string.Empty;
        pd.tempRespawnType = 0;
#endif
    }

    internal static void EnsureGateInScene(Scene scene)
    {
        if (!scene.IsValid() || !string.Equals(scene.name, TownScene, StringComparison.Ordinal))
            return;

#if SILKSONG
        if (HkssPlugin.State != null && HkssPlugin.State.MarkBonetownGateOpen())
        {
            HkssPlugin.Log.LogInfo("First arrival in Bone Bottom: return gate is now open for this run.");
        }
#endif

        if (FindInScene(scene, GateObjectName) != null)
            return;

        Vector3 gatePos = ResolveGatePosition(scene);
        GameObject root = new GameObject(GateObjectName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = gatePos;

        CreateRespawnMarker(root.transform);
        CreateVisual(root.transform);
        CreateInteractable(root);

        HkssPlugin.Log.LogInfo($"Spawned town gate in {scene.name} at ({gatePos.x:F2}, {gatePos.y:F2}).");
    }

    private static GameObject? FindInScene(Scene scene, string name)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null) continue;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            Transform? found = FindChildByName(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform? FindChildByName(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null) continue;
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
            Transform? nested = FindChildByName(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static Vector3 ResolveGatePosition(Scene scene)
    {
        float x;
        float probeY;

#if HOLLOW_KNIGHT
        // Dirtmouth plaza between the bench and the background shop door: clear of Nymm, Elderbug,
        // and the doorway in every town state. Search only inside the target Town scene so an
        // outgoing scene's RestBench during F4 warp can never hijack the coordinates.
        GameObject? bench = FindInScene(scene, "RestBench");
        if (bench != null && bench.transform.position.x > 100f && bench.transform.position.x < 170f)
        {
            x = bench.transform.position.x + 6.5f;
            probeY = bench.transform.position.y + 4f;
        }
        else
        {
            x = 138.88f;
            probeY = 14f;
        }
#elif SILKSONG
        // Open flat ground in Bone Bottom with clear space away from nearby NPCs across all acts.
        x = 256.5f;
        probeY = 14f;
#endif

        float? groundY = RaycastGroundInScene(scene, x, probeY, 20f);
        float y = groundY ?? (probeY - 4f);
        return new Vector3(x, y, 0.005f);
    }

    private static float? RaycastGroundInScene(Scene scene, float x, float startY, float distance)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(new Vector2(x, startY), Vector2.down, distance, TerrainLayerMask);
        float? bestY = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null || col.gameObject.scene != scene) continue;
            if (!bestY.HasValue || hits[i].point.y > bestY.Value)
            {
                bestY = hits[i].point.y;
            }
        }
        return bestY;
    }

    private static void CreateRespawnMarker(Transform parent)
    {
        GameObject markerObj = new GameObject(RespawnMarkerName);
        markerObj.transform.SetParent(parent, worldPositionStays: false);
        markerObj.transform.localPosition = new Vector3(0f, 1.0f, 0f);
#if HOLLOW_KNIGHT
        markerObj.tag = "RespawnPoint";
#endif
        RespawnMarker marker = markerObj.AddComponent<RespawnMarker>();
        marker.respawnFacingRight = true;
#if SILKSONG
        marker.customFadeDuration = new TeamCherry.SharedUtils.OverrideFloat();
        marker.overrideMapZone = new OverrideMapZone();
#endif
    }

    private static void CreateVisual(Transform parent)
    {
        // Color-coded by destination: crimson-red in Hollow Knight (leading to Pharloom),
        // soul-blue in Silksong (leading to Hallownest).
#if HOLLOW_KNIGHT
        Color rimColor = new Color(1.00f, 0.34f, 0.32f, 0.96f);
        Color coreColor = new Color(0.72f, 0.10f, 0.16f, 0.62f);
        Color auraColor = new Color(0.95f, 0.22f, 0.24f, 0.28f);
        Color moteColor = new Color(1.00f, 0.52f, 0.45f, 0.90f);
        const string theme = "crimson-red (to Pharloom)";
#else
        Color rimColor = new Color(0.58f, 0.88f, 1.00f, 0.96f);
        Color coreColor = new Color(0.16f, 0.46f, 0.88f, 0.62f);
        Color auraColor = new Color(0.32f, 0.72f, 1.00f, 0.28f);
        Color moteColor = new Color(0.76f, 0.94f, 1.00f, 0.90f);
        const string theme = "soul-blue (to Hallownest)";
#endif

        GameObject visual = new GameObject("GateVisual_Portal");
        visual.transform.SetParent(parent, worldPositionStays: false);
        visual.transform.localPosition = new Vector3(0f, 1.95f, 0.01f);

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = BuildPortalSprite(rimColor, coreColor, auraColor);
        renderer.sortingOrder = 0;

        GatePulse pulse = visual.AddComponent<GatePulse>();
        pulse.Target = renderer;

        CreateMoteParticles(parent, moteColor);
        HkssPlugin.Log.LogInfo($"Town gate visual: symmetric portal, {theme}.");
    }

    private static void CreateMoteParticles(Transform parent, Color moteColor)
    {
        GameObject fx = new GameObject("GateVisual_Motes");
        fx.transform.SetParent(parent, worldPositionStays: false);
        fx.transform.localPosition = new Vector3(0f, 0.35f, -0.01f);

        ParticleSystem ps = fx.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = moteColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 28;

        var emission = ps.emission;
        emission.rateOverTime = 9f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.1f, 0.3f, 0.1f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(moteColor, 0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(moteColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        ParticleSystemRenderer psr = fx.GetComponent<ParticleSystemRenderer>();
        Shader? spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            Material mat = new Material(spriteShader);
            mat.mainTexture = BuildSoftDotTexture();
            psr.material = mat;
        }
        psr.sortingOrder = 1;
    }

    private static Texture2D BuildSoftDotTexture()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Sprite BuildPortalSprite(Color rimColor, Color coreColor, Color auraColor)
    {
        const int w = 256;
        const int h = 384;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float cx = (w - 1) * 0.5f;
        float archStartY = h * 0.54f;
        float outerRx = w * 0.36f;
        float outerRy = h * 0.36f;

        for (int y = 0; y < h; y++)
        {
            float baseFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(y / 28f));
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x - cx);
                float normDist;
                if (y >= archStartY)
                {
                    float nx = dx / outerRx;
                    float ny = (y - archStartY) / outerRy;
                    // Slightly pointed gothic arch profile.
                    normDist = Mathf.Pow(Mathf.Pow(nx, 1.75f) + Mathf.Pow(ny, 1.75f), 1f / 1.75f);
                }
                else
                {
                    normDist = dx / outerRx;
                }

                // Continuous, anti-aliased layer weights (no hard band cutoffs).
                float insideMask = 1f - Mathf.SmoothStep(0.84f, 0.94f, normDist);
                float rimDelta = (normDist - 0.93f) / 0.065f;
                float rimWeight = Mathf.Exp(-rimDelta * rimDelta);
                float auraOuter = 1f - Mathf.SmoothStep(0.92f, 1.30f, normDist);
                float auraWeight = auraOuter * auraOuter * (1f - insideMask * 0.7f);

                float centerGlow = Mathf.Clamp01(1f - (dx / (outerRx * 0.88f)));
                float verticalGlow = 0.65f + 0.35f * Mathf.Sin((y / (float)h) * Mathf.PI);

                Color coreCol = Color.Lerp(coreColor, rimColor, centerGlow * centerGlow * 0.45f);
                float coreAlpha = coreColor.a * (0.55f + 0.45f * centerGlow) * verticalGlow * insideMask;

                Color rimCol = Color.Lerp(rimColor, Color.white, rimWeight * 0.42f);
                float rimAlpha = rimColor.a * rimWeight;

                float auraAlpha = auraColor.a * auraWeight;

                // Premultiplied-style smooth composite of aura -> core -> rim.
                float a = Mathf.Clamp01(auraAlpha + coreAlpha * (1f - auraAlpha));
                Vector3 rgb = new Vector3(auraColor.r, auraColor.g, auraColor.b) * auraAlpha
                    + new Vector3(coreCol.r, coreCol.g, coreCol.b) * (coreAlpha * (1f - auraAlpha));
                rgb = rgb * (1f - rimAlpha) + new Vector3(rimCol.r, rimCol.g, rimCol.b) * rimAlpha;
                a = Mathf.Clamp01(a * (1f - rimAlpha) + rimAlpha);

                if (a > 0.001f) rgb /= a;
                tex.SetPixel(x, y, new Color(rgb.x, rgb.y, rgb.z, a * baseFade));
            }
        }

        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 96f);
    }

    private sealed class GatePulse : MonoBehaviour
    {
        internal SpriteRenderer? Target;

        private void Update()
        {
            if (Target == null) return;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            Color c = Target.color;
            c.a = 0.86f + 0.14f * wave;
            Target.color = c;
            float s = 0.985f + 0.03f * wave;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    private static void CreateInteractable(GameObject root)
    {
        BoxCollider2D trigger = root.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2.2f, 3.0f);
        trigger.offset = new Vector2(0f, 1.5f);

#if HOLLOW_KNIGHT
        root.AddComponent<HollowKnightGateInteractable>();
#elif SILKSONG
        GameObject promptAnchor = new GameObject("PromptAnchor");
        promptAnchor.transform.SetParent(root.transform, worldPositionStays: false);
        promptAnchor.transform.localPosition = new Vector3(0f, 2.8f, 0f);

        SilksongGateInteractable interactable = root.AddComponent<SilksongGateInteractable>();
        interactable.PromptMarker = promptAnchor.transform;
        interactable.InteractLabel = InteractableBase.PromptLabels.Enter;
#endif
    }

#if HOLLOW_KNIGHT
    private sealed class HollowKnightGateInteractable : MonoBehaviour
    {
        private bool _heroInside;
        private PromptMarker? _prompt;
        private GameObject? _promptPrefab;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null || other.gameObject.layer != 9) return;
            _heroInside = true;
            ShowPrompt();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other == null || other.gameObject.layer != 9) return;
            _heroInside = false;
            HidePrompt();
        }

        private void OnDisable()
        {
            _heroInside = false;
            HidePrompt();
        }

        private void Update()
        {
            if (!_heroInside) return;

            if (!TravelGuard.CanLeave(out _))
            {
                HidePrompt();
                return;
            }

            if (_prompt == null) ShowPrompt();

            InputHandler ih = InputHandler.Instance;
            if (ih != null && ih.inputActions != null && ih.inputActions.up.WasPressed)
            {
                HidePrompt();
                Travel.Instance?.Begin(fromGate: true);
            }
        }

        private void ShowPrompt()
        {
            if (_prompt != null || !TravelGuard.CanLeave(out _)) return;
            _promptPrefab ??= FindPromptPrefab();
            if (_promptPrefab == null) return;

            GameObject spawned = Instantiate(_promptPrefab);
            spawned.transform.position = transform.position + new Vector3(0f, 2.8f, 0f);
            PromptMarker marker = spawned.GetComponent<PromptMarker>();
            if (marker == null)
            {
                Destroy(spawned);
                return;
            }
            marker.SetLabel("Enter");
            marker.SetOwner(gameObject);
            marker.Show();
            _prompt = marker;
        }

        private void HidePrompt()
        {
            if (_prompt == null) return;
            _prompt.Hide();
            _prompt = null;
        }

        private static GameObject? FindPromptPrefab()
        {
            foreach (PlayMakerFSM fsm in FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None))
            {
                if (fsm == null || fsm.FsmStates == null) continue;
                foreach (FsmState state in fsm.FsmStates)
                {
                    if (state?.Actions == null) continue;
                    foreach (FsmStateAction action in state.Actions)
                    {
                        if (action is ShowPromptMarker show && show.prefab?.Value != null)
                            return show.prefab.Value;
                    }
                }
            }

            foreach (PromptMarker marker in Resources.FindObjectsOfTypeAll<PromptMarker>())
            {
                if (marker != null && marker.gameObject != null && !marker.gameObject.scene.IsValid())
                    return marker.gameObject;
            }
            return null;
        }
    }
#elif SILKSONG
    private sealed class SilksongGateInteractable : InteractableBase
    {
        public override void Interact()
        {
            Travel.Instance?.Begin(fromGate: true);
        }
    }
#endif
}
