// Ported from CustomKnight: https://github.com/PrashantMohta/HollowKnight.CustomKnight
// CustomKnight/Skin/Base/SkinManager.cs and Skin/Base/SkinableItems/** at
// 46cf73415dd29baa2fed98a9939f58f0c488bf12, MIT License, Copyright (c) 2024 CustomKnight.
// The skin folder layout and the objects each sheet belongs to are Custom Knight's, so existing
// Custom Knight skins work unchanged. Preloaded objects (Cloak, Shriek, Wings, ...), per-object
// swaps and inventory icons other than charms are not covered.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>Puts a Custom Knight skin on the Knight, its spells and charms' effects, and the HUD.</summary>
internal sealed class HkSkins : ISkinApplier
{
    private static HkSkins? _current;
    private static bool _patched;
    private SkinInfo? _skin;
    private readonly HashSet<string> _reportedMisses = new();
    private readonly Dictionary<string, Sprite> _originalCharms = new();

    // Custom Knight sheet name -> the materials that sheet replaces.
    private static readonly (string Sheet, Func<IEnumerable<Material?>> Find)[] Sheets =
    {
        ("Knight", () => Clip("Idle")),
        ("Sprint", () => Clip("Sprint")),
        ("Unn", () => Clip("Slug Up")),
        ("Shade", () => ShadeClip()),
        ("ShadeOrb", () => ShadeParticles("Shade Particles", "Reform Particles", "Retreat Particles", "Charge Particles", "Depart Particles", "Quake Particles")),
        ("Wraiths", () => HeroSprites("Spells", "Scr Heads", "Scr Base")),
        ("VoidSpells", () => HeroSprites("Spells", "Scr Heads 2", "Scr Base 2")),
        ("VS", () => HeroSprites("Focus Effects", "Heal Anim")),
        ("Hud", () => HudSprite("Health 1")),
        ("Liquid", () => HudSprite("Liquid")),
        ("Baldur", () => One(Tk2dMaterial(Child(Child(Child(Hero, "Charm Effects"), "Blocker Shield"), "Shell Anim")))),
        ("Fluke", () => One(Tk2dMaterial(FsmPrefab<CreateGameObjectPool>("Charm Effects", "Pool Flukes", "Pool Normal", 0, a => a.prefab.Value)))),
        ("Shield", () => One(Tk2dMaterial(Child(FsmPrefab<SpawnObjectFromGlobalPool>("Charm Effects", "Spawn Orbit Shield", "Spawn", 2, a => a.gameObject.Value), "Shield")))),
        ("Grimm", () => One(Tk2dMaterial(FsmPrefab<SpawnObjectFromGlobalPool>("Charm Effects", "Spawn Grimmchild", "Spawn", 2, a => a.gameObject.Value)))),
        ("Weaver", () => One(Tk2dMaterial(FsmPrefab<SpawnObjectFromGlobalPool>("Charm Effects", "Weaverling Control", "Spawn", 0, a => a.gameObject.Value)))),
        ("Hatchling", () => One(Tk2dMaterial(FsmPrefab<SpawnObjectFromGlobalPool>("Charm Effects", "Hatchling Spawn", "Hatch", -1, a => a.gameObject.Value)))),
        ("Compass", () => One(Tk2dMaterial(GameManager.instance?.gameMap?.GetComponent<GameMap>()?.compassIcon))),
        ("Beam", () => GrubberflyBeams()),
        ("DoubleJFeather", () => Particles(Hero, "Double J Feather")),
        ("HitPt", () => Particles(Hero, "Hit Pt 1", "Hit Pt 2")),
        ("Leak", () => Particles(Hero, "Leak", "Low Health Leak")),
        ("QOrbs", () => Particles(Hero, "Q Orbs")),
        ("QOrbs2", () => Particles(Hero, "Q Orbs 2")),
        ("ScrOrbs", () => Particles(Hero, "Scr Orbs")),
        ("ScrOrbs2", () => Particles(Hero, "Scr Orbs 2")),
        ("SDCrystalBurst", () => Particles(Hero, "SD Crystal Burst GL", "SD Crystal Burst GR", "SD Crystal Burst W")),
        ("ShadowDashBlobs", () => Particles(Hero, "Shadow Dash Blobs")),
        ("Deathpt", () => Particles(HeroController.instance?.heroDeathPrefab, "Particle Wave")),
        ("DDeathpt", () => Particles(HeroController.instance?.heroDeathPrefab, "Dream Burst Pt")),
        ("DeathAsh", () => Particles(Child(Hero, "Hero Death"), "Ash L", "Ash R")),
        ("BrummShield", () => Particles(Child(Hero, "Brumm_shield"), "grimm_flame_particle")),
        ("FlowerBreak", () => Particles(Child(Hero, "White_Flower_Break"), "white_petal_break_particle")),
    };

    public bool Ready
    {
        get
        {
            GameManager? gm = GameManager._instance;
            return gm != null && gm.IsGameplayScene() && !gm.IsInSceneTransition
                && HeroController.instance != null && GameCameras.instance != null
                && GameCameras.instance.hudCanvas != null && CharmIconList.Instance != null;
        }
    }

    public HkSkins()
    {
        _current = this;
        if (_patched) return;
        _patched = true;
        var harmony = new Harmony("decryptu.hkss.skins");
        var start = AccessTools.Method(typeof(GeoControl), "Start");
        if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(HkSkins), nameof(AfterGeoStart)));
    }

    public void Apply(SkinInfo? skin)
    {
        RestoreCharms();
        SkinTextures.RestoreAll();
        _skin = skin;
        _reportedMisses.Clear();
        if (skin == null) return;
        ApplySheets();
        ApplyHudOrb();
        ApplyCharms();
    }

    public void OnSceneChanged()
    {
        if (_skin == null || !Ready) return;
        ApplySheets();
        ApplyHudOrb();
    }

    public void LateUpdate()
    {
    }

#if DEBUG
    // Inverted Knight, Sprint and HUD sheets, and quartered test images for the soul orb and a charm.
    public void DevWriteTestSkin(string folder)
    {
        foreach (string sheet in new[] { "Knight", "Sprint", "Hud" })
        {
            var entry = Array.Find(Sheets, s => s.Sheet == sheet);
            Material? material = entry.Find?.Invoke().FirstOrDefault(m => m != null);
            Texture? texture = material != null ? SkinTextures.Original(material) : null;
            if (texture != null) SkinTextures.DevWriteInverted(Path.Combine(folder, sheet + ".png"), texture);
        }
        SkinTextures.DevWriteQuarters(Path.Combine(folder, "OrbFull.png"), 96, 96);
        SkinTextures.DevWriteQuarters(Path.Combine(folder, "Charms", "Charm_1.png"), 96, 96);
    }
#endif

    private string? SkinFile(string name)
    {
        if (_skin == null) return null;
        string path = Path.Combine(_skin.ContentDir, name + ".png");
        return File.Exists(path) ? path : null;
    }

    private void ApplySheets()
    {
        foreach (var (sheet, find) in Sheets)
        {
            string? path = SkinFile(sheet);
            if (path == null) continue;
            Texture2D? tex = SkinTextures.Load(path);
            if (tex == null) continue;
            int applied = 0;
            try
            {
                foreach (Material? material in find())
                {
                    if (material == null) continue;
                    SkinTextures.SetTexture(material, tex);
                    applied++;
                }
            }
            catch (Exception e)
            {
                HkssPlugin.Log.LogDebug($"[Skins] {sheet}: {e.Message}");
            }
            if (applied == 0 && _reportedMisses.Add(sheet))
                HkssPlugin.Log.LogInfo($"[Skins] {sheet}.png has nothing to apply to yet.");
        }
    }

    private void ApplyHudOrb()
    {
        string? path = SkinFile("OrbFull");
        if (path == null || SkinTextures.Load(path) is not Texture2D tex) return;
        foreach (SpriteRenderer renderer in GameCameras.instance.hudCanvas.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.name != "Orb Full" && renderer.name != "Pulse Sprite") continue;
            Sprite? original = SkinTextures.OriginalSprite(renderer);
            if (original == null) continue;
            SkinTextures.SetSprite(renderer, SkinTextures.MakeSprite(tex, original));
        }
    }

    private static void AfterGeoStart(GeoControl __instance)
    {
        HkSkins? self = _current;
        if (self?._skin == null) return;
        string? path = self.SkinFile("Geo");
        if (path == null) return;
        SkinTextures.SetTexture(Tk2dMaterial(__instance.gameObject), SkinTextures.Load(path));
    }

    // Charm icons live in one list the inventory reads when it draws, so swapping its entries is enough.
    private void ApplyCharms()
    {
        CharmIconList icons = CharmIconList.Instance;
        for (int i = 1; i < icons.spriteList.Length; i++)
        {
            int id = i;
            if (id is 36 or 40) continue;
            string name = id is 23 or 24 or 25 ? $"Charm_{id}_Fragile" : $"Charm_{id}";
            SwapCharm(name, () => icons.spriteList[id], s => icons.spriteList[id] = s);
        }
        SwapCharm("Charm_23_Unbreakable", () => icons.unbreakableHeart, s => icons.unbreakableHeart = s);
        SwapCharm("Charm_24_Unbreakable", () => icons.unbreakableGreed, s => icons.unbreakableGreed = s);
        SwapCharm("Charm_25_Unbreakable", () => icons.unbreakableStrength, s => icons.unbreakableStrength = s);
        SwapCharm("Charm_40_1", () => icons.grimmchildLevel1, s => icons.grimmchildLevel1 = s);
        SwapCharm("Charm_40_2", () => icons.grimmchildLevel2, s => icons.grimmchildLevel2 = s);
        SwapCharm("Charm_40_3", () => icons.grimmchildLevel3, s => icons.grimmchildLevel3 = s);
        SwapCharm("Charm_40_4", () => icons.grimmchildLevel4, s => icons.grimmchildLevel4 = s);
        SwapCharm("Charm_40_5", () => icons.nymmCharm, s => icons.nymmCharm = s);
    }

    private readonly Dictionary<string, Action<Sprite>> _charmSetters = new();

    private void SwapCharm(string name, Func<Sprite> get, Action<Sprite> set)
    {
        string? path = SkinFile(Path.Combine("Charms", name));
        if (path == null || SkinTextures.Load(path) is not Texture2D tex) return;
        Sprite original = get();
        if (original == null) return;
        _originalCharms[name] = original;
        _charmSetters[name] = set;
        set(SkinTextures.MakeSprite(tex, original));
    }

    private void RestoreCharms()
    {
        foreach (var pair in _originalCharms)
        {
            if (_charmSetters.TryGetValue(pair.Key, out var set)) set(pair.Value);
        }
        _originalCharms.Clear();
        _charmSetters.Clear();
    }

    private static GameObject? Hero => HeroController.instance != null ? HeroController.instance.gameObject : null;

    private static IEnumerable<Material?> One(Material? material) => new[] { material };

    private static IEnumerable<Material?> Clip(string clip)
    {
        tk2dSpriteAnimator? animator = Hero?.GetComponent<tk2dSpriteAnimator>();
        yield return animator?.GetClipByName(clip)?.frames[0].spriteCollection.spriteDefinitions[0].material;
    }

    private static IEnumerable<Material?> ShadeClip()
    {
        GameObject? shade = SceneShade();
        tk2dSpriteAnimator? animator = shade != null ? shade.GetComponent<tk2dSpriteAnimator>() : null;
        yield return animator?.GetClipByName("Idle")?.frames[0].spriteCollection.spriteDefinitions[0].material;
    }

    private static IEnumerable<Material?> ShadeParticles(params string[] names) => Particles(SceneShade(), names);

    private static GameObject? SceneShade()
    {
        GameObject? sm = GameManager.instance?.GetSceneManager();
        return sm != null ? sm.GetComponent<SceneManager>()?.hollowShadeObject : null;
    }

    private static IEnumerable<Material?> HeroSprites(string parent, params string[] names)
    {
        GameObject? root = Child(Hero, parent);
        if (root == null) yield break;
        foreach (Transform child in root.transform)
        {
            if (Array.IndexOf(names, child.name) >= 0) yield return Tk2dMaterial(child.gameObject);
        }
    }

    private static IEnumerable<Material?> HudSprite(string name)
    {
        GameObject hud = GameCameras.instance.hudCanvas;
        foreach (tk2dSprite sprite in hud.GetComponentsInChildren<tk2dSprite>(true))
        {
            if (sprite.name == name)
            {
                yield return sprite.GetCurrentSpriteDef()?.material;
                yield break;
            }
        }
    }

    private static IEnumerable<Material?> GrubberflyBeams()
    {
        HeroController? hc = HeroController.instance;
        if (hc == null) yield break;
        foreach (GameObject? beam in new[]
        {
            hc.grubberFlyBeamPrefabD, hc.grubberFlyBeamPrefabD_fury, hc.grubberFlyBeamPrefabU, hc.grubberFlyBeamPrefabU_fury,
            hc.grubberFlyBeamPrefabL, hc.grubberFlyBeamPrefabL_fury, hc.grubberFlyBeamPrefabR, hc.grubberFlyBeamPrefabR_fury,
        })
        {
            yield return Tk2dMaterial(beam);
        }
    }

    private static IEnumerable<Material?> Particles(GameObject? root, params string[] names)
    {
        foreach (string name in names)
        {
            GameObject? child = Child(root, name);
            ParticleSystemRenderer? renderer = child != null ? child.GetComponent<ParticleSystemRenderer>() : null;
            yield return renderer != null ? renderer.material : null;
        }
    }

    private static Material? Tk2dMaterial(GameObject? go)
    {
        if (go == null) return null;
        tk2dSprite? sprite = go.GetComponent<tk2dSprite>();
        return sprite != null ? sprite.GetCurrentSpriteDef()?.material : null;
    }

    private static GameObject? FsmPrefab<T>(string holder, string fsmName, string state, int index, Func<T, GameObject?> pick)
        where T : FsmStateAction
    {
        GameObject? owner = Child(Hero, holder);
        PlayMakerFSM? fsm = owner?.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == fsmName);
        FsmState? found = fsm?.FsmStates.FirstOrDefault(s => s.Name == state);
        if (found == null) return null;
        T? action = index >= 0
            ? found.Actions.ElementAtOrDefault(index) as T
            : found.Actions.OfType<T>().FirstOrDefault();
        return action != null ? pick(action) : null;
    }

    /// <summary>Breadth-first search for a descendant by name, including inactive objects.</summary>
    private static GameObject? Child(GameObject? root, string name)
    {
        if (root == null) return null;
        var queue = new Queue<Transform>();
        queue.Enqueue(root.transform);
        while (queue.Count > 0)
        {
            foreach (Transform child in queue.Dequeue())
            {
                if (child.name == name) return child.gameObject;
                queue.Enqueue(child);
            }
        }
        return null;
    }
}
