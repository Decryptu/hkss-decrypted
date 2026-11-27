using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Optional combat readouts shared by both games: damage numbers that rise from enemies when they
/// are hit, small health bars over enemies that were hit recently, and a health bar for bosses
/// along the bottom of the screen. Damage is read from the enemy's hit points before and after each
/// hit, so it matches what the game really took off, including charms, tools and damage over time.
/// </summary>
internal sealed class CombatDisplay : MonoBehaviour
{
    private const float PopupLifetime = 0.9f;
    private const float PopupMergeWindow = 0.12f;
    private const float EnemyBarLinger = 4f;
    private const float EnemyBarFade = 0.8f;
    private const float BossBarLinger = 45f;
    private const float DeadBarLinger = 1.2f;
    private const int MaxPopups = 48;
    private const int MaxBossBars = 3;

    /// <summary>Hit points at or above this are placeholders (invincible props, damage relays), not enemies.</summary>
    private const int PlaceholderHp = 5000;

#if HOLLOW_KNIGHT
    private const int BossHpThreshold = 300;
#elif SILKSONG
    private const int BossHpThreshold = 250;
    private const int LargeBossHpThreshold = 120;
#endif

    private enum DamageKind
    {
        Melee,
        Spell,
        Tool,
        Shadow,
        OverTime,
        Critical,
    }

    private sealed class Tracked
    {
        public HealthManager Hm = null!;
        public Collider2D? Collider;
        public int MaxHp;
        public int Hp;
        public float ShownFraction = 1f;
        public float TrailFraction = 1f;
        public float LastHitAt;
        public float DiedAt = -1f;
        public bool IsBoss;
        public string Name = string.Empty;
    }

    private struct Popup
    {
        public HealthManager Target;
        public Vector3 World;
        public int Amount;
        public DamageKind Kind;
        public float Born;
        public float Drift;
        public int Stack;
    }

    private static readonly Dictionary<HealthManager, Tracked> TrackedEnemies = new();
    private static readonly List<Popup> Popups = new();
    private static readonly List<Tracked> BossScratch = new();
    private static readonly List<HealthManager> RemoveScratch = new();
    private static string _scene = string.Empty;

#if HOLLOW_KNIGHT
    private static readonly System.Reflection.FieldInfo? HpScaleField =
        AccessTools.Field(typeof(HealthManager), "hpScale");
    private static readonly System.Reflection.FieldInfo? HpScaleLevel1Field =
        HpScaleField != null ? AccessTools.Field(HpScaleField.FieldType, "level1") : null;
    private static readonly System.Reflection.FieldInfo? HpScaleLevel2Field =
        HpScaleField != null ? AccessTools.Field(HpScaleField.FieldType, "level2") : null;
    private static readonly System.Reflection.FieldInfo? HpScaleLevel3Field =
        HpScaleField != null ? AccessTools.Field(HpScaleField.FieldType, "level3") : null;
    private static readonly System.Reflection.FieldInfo? PlayerDataNameField =
        AccessTools.Field(typeof(EnemyDeathEffects), "playerDataName");
#elif SILKSONG
    private static readonly System.Reflection.FieldInfo? InitHpField =
        AccessTools.Field(typeof(HealthManager), "initHp");
    private static readonly System.Reflection.FieldInfo? BigEnemyDeathField =
        AccessTools.Field(typeof(HealthManager), "bigEnemyDeath");
    private static readonly System.Reflection.FieldInfo? JournalRecordField =
        AccessTools.Field(typeof(EnemyDeathEffects), "journalRecord");
#endif

    private Texture2D? _white;
    private GUIStyle? _numberStyle;
    private GUIStyle? _bossNameStyle;
    private GUIStyle? _bossHpStyle;
    private float _styleScale;

    internal static void Install(Harmony harmony)
    {
        Patch(harmony, AccessTools.Method(typeof(HealthManager), "TakeDamage"), "hits",
            nameof(BeforeHit), nameof(AfterHit));
        Patch(harmony, AccessTools.Method(typeof(HealthManager), nameof(HealthManager.ApplyExtraDamage), new[] { typeof(int) }),
            "extra damage", nameof(BeforeHit), nameof(AfterExtraDamage));
#if SILKSONG
        Patch(harmony, AccessTools.Method(typeof(HealthManager), nameof(HealthManager.ApplyExtraDamage), new[] { typeof(HitInstance) }),
            "damage over time", nameof(BeforeHit), nameof(AfterExtraDamage));
#endif
    }

    private static void Patch(Harmony harmony, System.Reflection.MethodInfo? target, string what, string prefix, string postfix)
    {
        if (target == null)
        {
            HkssPlugin.Log.LogWarning($"Combat display: hook for {what} not found; those numbers will not show.");
            return;
        }
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(CombatDisplay), prefix),
                postfix: new HarmonyMethod(typeof(CombatDisplay), postfix));
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Combat display: could not hook {what} ({e.Message}).");
        }
    }

    private static bool AnyEnabled =>
        PlayerOptions.DamageNumbers.Value || PlayerOptions.EnemyHealthBars.Value || PlayerOptions.BossHealthBar.Value;

    // The hit may land on another enemy: some Silksong bosses pass their damage to a shared body.
    private static HealthManager DamageTarget(HealthManager hm)
    {
#if SILKSONG
        if (hm.SendDamageTo != null) return hm.SendDamageTo;
#endif
        return hm;
    }

    private static void BeforeHit(HealthManager __instance, out int __state)
    {
        __state = int.MinValue;
        if (__instance == null || !AnyEnabled) return;
        __state = DamageTarget(__instance).hp;
    }

    private static void AfterHit(HealthManager __instance, int __state, HitInstance hitInstance)
    {
        if (__state == int.MinValue || __instance == null) return;
#if SILKSONG
        DamageKind kind = hitInstance.CriticalHit ? DamageKind.Critical : KindOf(hitInstance.AttackType);
#else
        DamageKind kind = KindOf(hitInstance.AttackType);
#endif
        Record(__instance, __state, kind);
    }

    private static void AfterExtraDamage(HealthManager __instance, int __state)
    {
        if (__state == int.MinValue || __instance == null) return;
        Record(__instance, __state, DamageKind.OverTime);
    }

    private static DamageKind KindOf(AttackTypes type) => type switch
    {
        AttackTypes.Nail or AttackTypes.NailBeam => DamageKind.Melee,
        AttackTypes.Spell => DamageKind.Spell,
        AttackTypes.SharpShadow => DamageKind.Shadow,
#if SILKSONG
        AttackTypes.Heavy => DamageKind.Melee,
#endif
        _ => DamageKind.Tool,
    };

    private static void Record(HealthManager source, int hpBefore, DamageKind kind)
    {
        try
        {
            HealthManager target = DamageTarget(source);
            int dealt = hpBefore - target.hp;
            if (dealt <= 0 || hpBefore >= PlaceholderHp) return;

            float now = Time.unscaledTime;
            Tracked tracked = Track(target, hpBefore);
            tracked.Hp = target.hp;
            tracked.LastHitAt = now;

            if (!PlayerOptions.DamageNumbers.Value) return;
            Collider2D? sourceCollider = ReferenceEquals(source, target)
                ? tracked.Collider
                : source.GetComponent<Collider2D>();
            Vector3 where = TopOf(source, sourceCollider);
            for (int i = Popups.Count - 1; i >= 0; i--)
            {
                Popup p = Popups[i];
                if (now - p.Born > PopupMergeWindow) break;
                if (p.Target != target || p.Kind != kind) continue;
                p.Amount += dealt;
                Popups[i] = p;
                return;
            }
            // Numbers that land on the same enemy close together stack upwards instead of overlapping.
            int stack = 0;
            foreach (Popup p in Popups)
                if (p.Target == target && now - p.Born < PopupLifetime * 0.5f) stack = Math.Max(stack, p.Stack + 1);
            if (Popups.Count >= MaxPopups) Popups.RemoveAt(0);
            Popups.Add(new Popup
            {
                Target = target,
                World = where,
                Amount = dealt,
                Kind = kind,
                Born = now,
                Drift = UnityEngine.Random.Range(-18f, 18f),
                Stack = stack % 4,
            });
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Combat display: {e.Message}");
        }
    }

    private static Tracked Track(HealthManager hm, int hpBefore)
    {
        if (!TrackedEnemies.TryGetValue(hm, out Tracked tracked))
        {
            tracked = new Tracked { Hm = hm, Collider = hm.GetComponent<Collider2D>() };
            int maxHp = Math.Max(hpBefore, 1);
#if SILKSONG
            if (InitHpField != null && InitHpField.GetValue(hm) is int initHp)
                maxHp = Math.Max(maxHp, initHp);
#endif
            tracked.MaxHp = maxHp;
            tracked.IsBoss = IsBoss(hm, maxHp);
            tracked.Name = tracked.IsBoss ? DisplayName(hm) : string.Empty;
            float start = Mathf.Clamp01((float)hpBefore / maxHp);
            tracked.ShownFraction = start;
            tracked.TrailFraction = start;
            TrackedEnemies[hm] = tracked;
        }
        else if (hpBefore > tracked.MaxHp)
        {
            tracked.MaxHp = hpBefore;
        }
        return tracked;
    }

    private static bool IsBoss(HealthManager hm, int maxHp)
    {
        if (maxHp >= BossHpThreshold) return true;
#if HOLLOW_KNIGHT
        // Every boss that Godhome rescales has scaled hit points set, so this catches the light ones too.
        object? scale = HpScaleField?.GetValue(hm);
        if (scale == null) return false;
        return (HpScaleLevel1Field?.GetValue(scale) is int l1 && l1 > 0)
            || (HpScaleLevel2Field?.GetValue(scale) is int l2 && l2 > 0)
            || (HpScaleLevel3Field?.GetValue(scale) is int l3 && l3 > 0);
#elif SILKSONG
        return maxHp >= LargeBossHpThreshold
            && BigEnemyDeathField?.GetValue(hm) is bool big && big;
#endif
    }

    private static string DisplayName(HealthManager hm)
    {
        try
        {
            EnemyDeathEffects? effects = hm.GetComponent<EnemyDeathEffects>();
#if HOLLOW_KNIGHT
            string? journal = effects == null ? null : PlayerDataNameField?.GetValue(effects) as string;
            if (!string.IsNullOrEmpty(journal))
            {
                string key = "NAME_" + UpperSnake(journal!);
                if (TeamCherry.Localization.Language.Has(key, "Journal"))
                    return TeamCherry.Localization.Language.Get(key, "Journal");
            }
#elif SILKSONG
            object? record = effects == null ? null : JournalRecordField?.GetValue(effects);
            if (record is EnemyJournalRecord journal && journal != null)
            {
                string name = journal.DisplayName.ToString();
                if (!string.IsNullOrWhiteSpace(name) && name.IndexOf('#') < 0) return name.Trim();
            }
#endif
        }
        catch (Exception)
        {
            // Fall back to the object's own name.
        }
        return CleanObjectName(hm.gameObject.name);
    }

    private static string UpperSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    internal static string CleanObjectName(string name)
    {
        int cut = name.IndexOf('(');
        if (cut > 0) name = name.Substring(0, cut);
        name = name.Replace('_', ' ').Trim();
        string[] words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int keep = words.Length;
        // Drop trailing numbering such as "1", "02" or "S1".
        while (keep > 1 && IsNumbering(words[keep - 1])) keep--;
        return string.Join(" ", words, 0, keep);
    }

    private static bool IsNumbering(string word)
    {
        int digits = 0;
        foreach (char c in word) if (char.IsDigit(c)) digits++;
        return digits > 0 && word.Length - digits <= 1;
    }

    private static Vector3 TopOf(HealthManager hm, Collider2D? collider)
    {
        if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
        {
            Bounds b = collider.bounds;
            return new Vector3(b.center.x, b.max.y, hm.transform.position.z);
        }
        return hm.transform.position + Vector3.up * 1.2f;
    }

    private void Update()
    {
        if (!AnyEnabled && TrackedEnemies.Count == 0 && Popups.Count == 0) return;

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            TrackedEnemies.Clear();
            Popups.Clear();
            return;
        }

        float now = Time.unscaledTime;
        float step = Time.unscaledDeltaTime;
        RemoveScratch.Clear();
        foreach (var pair in TrackedEnemies)
        {
            Tracked t = pair.Value;
            HealthManager hm = pair.Key;
            if (hm == null)
            {
                RemoveScratch.Add(pair.Key);
                continue;
            }

            // Bosses also heal or change phase through their own scripts, so read hit points each frame.
            int hp = hm.hp;
            if (hp > t.MaxHp && hp < PlaceholderHp) t.MaxHp = hp;
            t.Hp = hp;
            bool dead = hm.isDead || hp <= 0 || !hm.gameObject.activeInHierarchy;
            if (dead && t.DiedAt < 0f) t.DiedAt = now;
            else if (!dead) t.DiedAt = -1f;

            float target = Mathf.Clamp01((float)Math.Max(hp, 0) / t.MaxHp);
            t.ShownFraction = Mathf.MoveTowards(t.ShownFraction, target, step * 4f);
            if (t.ShownFraction > t.TrailFraction) t.TrailFraction = t.ShownFraction;
            else if (now - t.LastHitAt > 0.45f) t.TrailFraction = Mathf.MoveTowards(t.TrailFraction, t.ShownFraction, step * 0.8f);

            float linger = t.IsBoss ? BossBarLinger : EnemyBarLinger + EnemyBarFade;
            if ((t.DiedAt >= 0f && now - t.DiedAt > DeadBarLinger) || now - t.LastHitAt > linger)
                RemoveScratch.Add(pair.Key);
        }
        foreach (HealthManager hm in RemoveScratch) TrackedEnemies.Remove(hm);

        while (Popups.Count > 0 && now - Popups[0].Born > PopupLifetime) Popups.RemoveAt(0);
    }

    private static bool ShouldDraw()
    {
        if (!AnyEnabled || Travel.Instance?.IsTravelling == true) return false;
        GameManager? gm = GameManager.instance;
        if (gm == null || gm.IsGamePaused() || HeroController.instance == null) return false;
#if HOLLOW_KNIGHT
        if (gm.gameState != GlobalEnums.GameState.PLAYING) return false;
#elif SILKSONG
        if (gm.GameState != GlobalEnums.GameState.PLAYING) return false;
#endif
        return true;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !ShouldDraw()) return;
        Camera? cam = GameCameras.instance != null ? GameCameras.instance.mainCamera : Camera.main;
        if (cam == null) return;

        GUI.depth = 10;
        EnsureStyles();
        Color prev = GUI.color;
        float now = Time.unscaledTime;

        if (PlayerOptions.EnemyHealthBars.Value) DrawEnemyBars(cam, now);
        if (PlayerOptions.DamageNumbers.Value) DrawPopups(cam, now);
        if (PlayerOptions.BossHealthBar.Value) DrawBossBars(now);

        GUI.color = prev;
    }

    private float Scale => Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.5f);

    private void EnsureStyles()
    {
        if (_white == null)
        {
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }
        float scale = Scale;
        if (_numberStyle != null && Mathf.Approximately(scale, _styleScale)) return;
        _styleScale = scale;
        _numberStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.RoundToInt(24 * scale),
            clipping = TextClipping.Overflow,
            wordWrap = false,
        };
        _bossNameStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerLeft,
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.RoundToInt(17 * scale),
            clipping = TextClipping.Overflow,
            wordWrap = false,
        };
        _bossHpStyle = new GUIStyle(_bossNameStyle)
        {
            alignment = TextAnchor.LowerRight,
            fontStyle = FontStyle.Normal,
            fontSize = Mathf.RoundToInt(14 * scale),
        };
    }

    private static bool ToScreen(Camera cam, Vector3 world, out Vector2 screen)
    {
        Vector3 p = cam.WorldToScreenPoint(world);
        screen = new Vector2(p.x, Screen.height - p.y);
        return p.z > 0f && p.x > -100f && p.x < Screen.width + 100f && p.y > -100f && p.y < Screen.height + 100f;
    }

    private void DrawEnemyBars(Camera cam, float now)
    {
        float scale = Scale;
        float w = 64f * scale;
        float h = Mathf.Max(4f, 6f * scale);
        foreach (Tracked t in TrackedEnemies.Values)
        {
            if (t.Hm == null || (t.IsBoss && PlayerOptions.BossHealthBar.Value)) continue;
            if (!t.Hm.gameObject.activeInHierarchy && t.DiedAt < 0f) continue;
            float since = now - t.LastHitAt;
            float alpha = since > EnemyBarLinger ? 1f - (since - EnemyBarLinger) / EnemyBarFade : 1f;
            if (t.DiedAt >= 0f) alpha = Mathf.Min(alpha, 1f - (now - t.DiedAt) / DeadBarLinger);
            if (alpha <= 0f || !ToScreen(cam, TopOf(t.Hm, t.Collider), out Vector2 top)) continue;

            var rect = new Rect(top.x - w / 2f, top.y - h - 10f * scale, w, h);
            DrawBar(rect, t, alpha, BarColor(t.ShownFraction));
        }
    }

    private void DrawBossBars(float now)
    {
        BossScratch.Clear();
        foreach (Tracked t in TrackedEnemies.Values)
            if (t.IsBoss && t.Hm != null) BossScratch.Add(t);
        if (BossScratch.Count == 0) return;
        BossScratch.Sort((a, b) => b.LastHitAt.CompareTo(a.LastHitAt));
        int count = Math.Min(BossScratch.Count, MaxBossBars);
        BossScratch.RemoveRange(count, BossScratch.Count - count);
        BossScratch.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        float scale = Scale;
        float w = Mathf.Min(720f * scale, Screen.width * 0.6f);
        float h = 11f * scale;
        float gap = 30f * scale;
        float y = Screen.height - 48f * scale - (count - 1) * gap;
        foreach (Tracked t in BossScratch)
        {
            float alpha = t.DiedAt >= 0f ? 1f - (now - t.DiedAt) / DeadBarLinger : 1f;
            float since = now - t.LastHitAt;
            if (since > BossBarLinger - 2f) alpha = Mathf.Min(alpha, (BossBarLinger - since) / 2f);
            if (alpha <= 0f) continue;

            var rect = new Rect((Screen.width - w) / 2f, y, w, h);
            DrawShadowed(new Rect(rect.x, rect.y - 24f * scale, rect.width, 22f * scale), t.Name, _bossNameStyle!,
                new Color(0.96f, 0.93f, 0.86f, alpha), alpha);
            DrawShadowed(new Rect(rect.x, rect.y - 24f * scale, rect.width, 22f * scale),
                $"{Math.Max(t.Hp, 0)} / {t.MaxHp}", _bossHpStyle!, new Color(0.82f, 0.84f, 0.9f, alpha), alpha);
            DrawBar(rect, t, alpha, BossColor());
            y += gap;
        }
    }

    private void DrawBar(Rect rect, Tracked t, float alpha, Color fill)
    {
        float pad = Mathf.Max(1f, rect.height * 0.18f);
        GUI.color = new Color(0f, 0f, 0f, 0.72f * alpha);
        GUI.DrawTexture(new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2f, rect.height + pad * 2f), _white);
        GUI.color = new Color(0.18f, 0.18f, 0.22f, 0.9f * alpha);
        GUI.DrawTexture(rect, _white);
        GUI.color = new Color(Mathf.Lerp(fill.r, 1f, 0.55f), Mathf.Lerp(fill.g, 1f, 0.55f), Mathf.Lerp(fill.b, 1f, 0.55f), 0.55f * alpha);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * t.TrailFraction, rect.height), _white);
        GUI.color = new Color(fill.r, fill.g, fill.b, alpha);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * t.ShownFraction, rect.height), _white);
        GUI.color = new Color(1f, 1f, 1f, 0.18f * alpha);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * t.ShownFraction, Mathf.Max(1f, rect.height * 0.35f)), _white);
    }

    private void DrawPopups(Camera cam, float now)
    {
        float scale = Scale;
        foreach (Popup p in Popups)
        {
            float age = now - p.Born;
            float life = age / PopupLifetime;
            if (life >= 1f || !ToScreen(cam, p.World, out Vector2 at)) continue;

            float rise = (1f - (1f - life) * (1f - life)) * 56f * scale;
            float pop = age < 0.08f ? 1.35f - age / 0.08f * 0.35f : 1f;
            float alpha = life > 0.65f ? 1f - (life - 0.65f) / 0.35f : 1f;
            bool big = p.Kind == DamageKind.Critical;
            _numberStyle!.fontSize = Mathf.RoundToInt((big ? 31f : 24f) * scale * pop);
            string text = big ? p.Amount + "!" : p.Amount.ToString();
            var rect = new Rect(at.x + p.Drift * scale - 80f, at.y - (18f + 24f * p.Stack) * scale - rise - 20f, 160f, 40f);
            DrawShadowed(rect, text, _numberStyle, KindColor(p.Kind, alpha), alpha);
        }
        _numberStyle!.fontSize = Mathf.RoundToInt(24 * scale);
    }

    private static void DrawShadowed(Rect rect, string text, GUIStyle style, Color color, float alpha)
    {
        float o = Mathf.Max(1f, style.fontSize / 14f);
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x - o, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x + o, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y - o, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y + o, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    private static Color KindColor(DamageKind kind, float alpha)
    {
        Color c = kind switch
        {
            DamageKind.Spell =>
#if HOLLOW_KNIGHT
                new Color(0.62f, 0.86f, 1f),
#elif SILKSONG
                new Color(1f, 0.55f, 0.55f),
#endif
            DamageKind.Shadow => new Color(0.78f, 0.62f, 1f),
            DamageKind.Tool => new Color(1f, 0.74f, 0.38f),
            DamageKind.OverTime => new Color(0.68f, 0.95f, 0.45f),
            DamageKind.Critical => new Color(1f, 0.86f, 0.3f),
            _ => new Color(1f, 1f, 1f),
        };
        c.a = alpha;
        return c;
    }

    private static Color BarColor(float fraction) =>
        fraction > 0.5f ? new Color(0.55f, 0.9f, 0.5f)
        : fraction > 0.25f ? new Color(0.98f, 0.82f, 0.35f)
        : new Color(0.95f, 0.38f, 0.33f);

    private static Color BossColor()
    {
#if HOLLOW_KNIGHT
        return new Color(0.82f, 0.22f, 0.2f);
#elif SILKSONG
        return new Color(0.86f, 0.2f, 0.24f);
#endif
    }

    private void OnDestroy()
    {
        if (_white != null) Destroy(_white);
    }

#if DEBUG
    // Development only: HKSS_DEV_COMBAT_TEST=1 turns everything on, brings the nearest enemies next
    // to the player and hits them a few times, so a dev screenshot shows numbers and bars.
    private float _devStartAt = -1f;
    private float _devNextHit;
    private int _devHits;
    private readonly List<HealthManager> _devTargets = new();

    private void LateUpdate()
    {
        if (_devHits >= 12 || System.Environment.GetEnvironmentVariable("HKSS_DEV_COMBAT_TEST") != "1") return;
        if (!AutoLoad.IsReady || HeroController.instance == null) return;
        float now = Time.unscaledTime;
        if (_devStartAt < 0f)
        {
            _devStartAt = now + 0.5f;
            PlayerOptions.DamageNumbers.Value = true;
            PlayerOptions.EnemyHealthBars.Value = true;
            PlayerOptions.BossHealthBar.Value = true;
            return;
        }
        if (now < _devStartAt || now < _devNextHit) return;
        _devNextHit = now + 0.3f;

        Vector3 hero = HeroController.instance.transform.position;
        if (_devTargets.Count == 0)
        {
            var all = new List<HealthManager>(FindObjectsByType<HealthManager>(FindObjectsSortMode.None));
            all.RemoveAll(h => h == null || h.isDead || h.hp <= 0 || h.hp >= PlaceholderHp || !h.gameObject.activeInHierarchy);
            all.Sort((a, b) => (a.transform.position - hero).sqrMagnitude.CompareTo((b.transform.position - hero).sqrMagnitude));
            for (int i = 0; i < all.Count && _devTargets.Count < 3; i++)
            {
                HealthManager hm = all[i];
                hm.hp = Math.Max(hm.hp, 150);
                hm.transform.position = hero + new Vector3(3.5f + 3f * _devTargets.Count, 0.5f, 0f);
                _devTargets.Add(hm);
            }
            HkssPlugin.Log.LogInfo($"[dev] combat test: {_devTargets.Count} enemies of {all.Count}.");
            if (_devTargets.Count == 0) _devHits = int.MaxValue;
            return;
        }

        var takeDamage = AccessTools.Method(typeof(HealthManager), "TakeDamage");
        for (int i = 0; i < _devTargets.Count; i++)
        {
            HealthManager hm = _devTargets[i];
            if (hm == null || hm.isDead) continue;
            hm.transform.position = hero + new Vector3(3.5f + 3f * i, 0.5f, 0f);
            try
            {
                if (_devHits % 4 == 3)
                {
                    hm.ApplyExtraDamage(2);
                    continue;
                }
                var hit = new HitInstance
                {
                    Source = HeroController.instance.gameObject,
                    AttackType = _devHits % 4 == 1 ? AttackTypes.Spell : AttackTypes.Nail,
                    DamageDealt = 5 + i * 3,
                    Multiplier = 1f,
                    MagnitudeMultiplier = 1f,
                    IgnoreInvulnerable = true,
#if SILKSONG
                    CriticalHit = _devHits % 4 == 2,
#endif
                };
                takeDamage.Invoke(hm, new object[] { hit });
            }
            catch (Exception e)
            {
                HkssPlugin.Log.LogWarning($"[dev] combat test hit failed: {e.GetBaseException().Message}");
                hm.ApplyExtraDamage(3);
            }
        }
        if (_devHits == 0 && _devTargets[0] != null && TrackedEnemies.TryGetValue(_devTargets[0], out Tracked first))
        {
            first.IsBoss = true;
            first.Name = DisplayName(first.Hm);
        }
        _devHits++;
        if (_devHits >= 12)
            HkssPlugin.Log.LogInfo($"[dev] combat test done: tracking {TrackedEnemies.Count}, popups {Popups.Count}.");
    }
#endif
}
