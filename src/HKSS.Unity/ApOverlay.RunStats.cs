using System.Collections.Generic;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Combined end-of-run stats tab: a title for the run, the time split between both worlds, a
/// side-by-side table of both games' numbers, whole-run records and earned feats.
/// </summary>
internal sealed partial class ApOverlay
{
    private RunSummary? _runSummary;
    private float _nextRunSummaryRefresh;
    private Vector2 _statsScroll;
    private Texture2D? _hkBarTex, _ssBarTex, _goldBarTex, _emptyBarTex;
    private GUIStyle? _epithetStyle, _hkHeadStyle, _ssHeadStyle, _featTitleStyle, _smallStyle;

    private void DestroyRunStatsTextures()
    {
        if (_hkBarTex != null) Destroy(_hkBarTex);
        if (_ssBarTex != null) Destroy(_ssBarTex);
        if (_goldBarTex != null) Destroy(_goldBarTex);
        if (_emptyBarTex != null) Destroy(_emptyBarTex);
    }

    private void EnsureRunStatsStyles()
    {
        _hkBarTex ??= UiTheme.MakeSolidTexture(UiTheme.HkColor);
        _ssBarTex ??= UiTheme.MakeSolidTexture(UiTheme.SsColor);
        _goldBarTex ??= UiTheme.MakeSolidTexture(UiTheme.GoldColor);
        _emptyBarTex ??= UiTheme.MakeSolidTexture(new Color(1f, 1f, 1f, 0.12f));
        _epithetStyle ??= new GUIStyle(_labelStyle) { fontStyle = FontStyle.Italic, fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = UiTheme.GoldColor } };
        _hkHeadStyle ??= new GUIStyle(_sectionStyle) { alignment = TextAnchor.MiddleRight, normal = { textColor = UiTheme.HkColor } };
        _ssHeadStyle ??= new GUIStyle(_sectionStyle) { alignment = TextAnchor.MiddleRight, normal = { textColor = UiTheme.SsColor } };
        _featTitleStyle ??= new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = UiTheme.GoldColor } };
        _smallStyle ??= new GUIStyle(_labelStyle) { fontSize = 11, normal = { textColor = new Color(0.7f, 0.73f, 0.8f, 1f) } };
    }

    private void DrawRunStatsTab()
    {
        if (_runSummary == null || Time.unscaledTime >= _nextRunSummaryRefresh)
        {
            RunStatsTracker.Flush();
            _runSummary = HkssPlugin.State.LoadSummary();
            _nextRunSummaryRefresh = Time.unscaledTime + 2f;
        }
        EnsureRunStatsStyles();
        RunSummary s = _runSummary;
        GameStats hk = s.HollowKnight, ss = s.Silksong;

        _statsScroll = GUILayout.BeginScrollView(_statsScroll);

        ApClient? client = ApManager.Instance?.Client;
        bool done = client != null && client.IsConnected
            ? GoalTracker.IsComboGoalSatisfied(client.GamesMode, client.ComboGoal, s.HkGoalCompleted, s.SsGoalCompleted)
            : s.HkGoalCompleted && s.SsGoalCompleted;
        string heading = s.SteelSoulDefeated
            ? $"Steel Soul defeated in {DoubleSteelSoul.FormatWorldName(s.SteelSoulDefeatedGame)}"
            : done
                ? (s.DoubleSteelSoul ? "Double Steel Soul complete" : "Run complete")
                : (s.DoubleSteelSoul ? "Double Steel Soul — In progress" : "Run in progress");
        GUILayout.Label(heading, _sectionStyle);
        GUILayout.Label($"“{s.Epithet}”", _epithetStyle);
        GUILayout.Space(4f);

        // Time split between both worlds.
        double share = s.HollowKnightShare;
        GUILayout.Label($"{RunSummary.FormatDuration(s.TotalPlaySeconds)} across both worlds", _labelStyle);
        DrawSplitBar((float)share);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Hallownest {share * 100:0}%", _smallStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Pharloom {(1 - share) * 100:0}%", _smallStyle);
        GUILayout.EndHorizontal();

        if (s.TotalChecks > 0)
        {
            GUILayout.Space(2f);
            GUILayout.Label($"Checks {RunSummary.FormatChecks(s.TotalChecksFound, s.TotalChecks)} ({100.0 * s.TotalChecksFound / s.TotalChecks:0}%)", _labelStyle);
            DrawProgressBar((float)s.TotalChecksFound / s.TotalChecks);
        }

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("", _labelStyle, GUILayout.Width(118f));
        GUILayout.Label("Hallownest", _hkHeadStyle, GUILayout.Width(105f));
        GUILayout.Label("Pharloom", _ssHeadStyle, GUILayout.Width(105f));
        GUILayout.EndHorizontal();

        Row("Goal", Goal(s.HkGoalCompleted, hk), Goal(s.SsGoalCompleted, ss));
        if (hk.GoalAtSeconds > 0 || ss.GoalAtSeconds > 0)
            Row("Goal reached at", GoalAt(hk), GoalAt(ss));
        Row("Play time", RunSummary.FormatDuration(hk.PlaySeconds), RunSummary.FormatDuration(ss.PlaySeconds));
        Row("Completion", $"{hk.CompletionPercent:0}%", $"{ss.CompletionPercent:0}%");
        Row("Checks", RunSummary.FormatChecks(hk.ChecksFound, hk.ChecksTotal), RunSummary.FormatChecks(ss.ChecksFound, ss.ChecksTotal));
        Row("Rooms explored", RunSummary.RoomsVisited(hk).ToString("N0"), RunSummary.RoomsVisited(ss).ToString("N0"));
        Row("Deaths", hk.Deaths.ToString("N0"), ss.Deaths.ToString("N0"));
        Row("Longest life", RunSummary.FormatDuration(hk.BestLifeSeconds), RunSummary.FormatDuration(ss.BestLifeSeconds));
        Row("Kills", hk.Kills.ToString("N0"), ss.Kills.ToString("N0"));
        Row("Hits landed", hk.HitsLanded.ToString("N0"), ss.HitsLanded.ToString("N0"));
        Row("Hits taken", hk.HitsTaken.ToString("N0"), ss.HitsTaken.ToString("N0"));
        Row("Masks lost", hk.MasksLost.ToString("N0"), ss.MasksLost.ToString("N0"));
        Row("Geo / Rosaries", hk.CurrencyEarned.ToString("N0"), ss.CurrencyEarned.ToString("N0"));
        Row("Jumps", hk.Jumps.ToString("N0"), ss.Jumps.ToString("N0"));
        Row("Dashes", hk.Dashes.ToString("N0"), ss.Dashes.ToString("N0"));
        Row("Distance", RunSummary.FormatDistance(hk.Distance), RunSummary.FormatDistance(ss.Distance));

        GUILayout.Space(10f);
        GUILayout.Label("Whole run", _sectionStyle);
        Record("Trips between worlds", s.Travels.ToString("N0"));
        if (s.ChecksPerHour > 0) Record("Checks per hour", s.ChecksPerHour.ToString("0.0"));
        Record("Kills per death", s.TotalDeaths > 0 ? s.KillsPerDeath.ToString("0.0") : "Deathless");
        Record("Longest life", RunSummary.FormatDuration(s.LongestLifeSeconds));
        if (s.GoalGapSeconds > 0) Record("Between both goals", RunSummary.FormatDuration(s.GoalGapSeconds));
        if (s.DeadliestArea is { } nemesis)
            Record("Deadliest area", $"{nemesis.Name} ({nemesis.Value:0})");
        if (s.FavouriteArea is { } haunt)
            Record("Favourite haunt", $"{haunt.Name} ({RunSummary.FormatDuration(haunt.Value)})");
        Record("Total distance", RunSummary.FormatDistance(s.TotalDistance));

        GUILayout.Space(10f);
        GUILayout.Label("Feats", _sectionStyle);
        IReadOnlyList<Feat> feats = s.Feats;
        if (feats.Count == 0)
            GUILayout.Label("None yet. The wilds still wait.", _smallStyle);
        foreach (Feat feat in feats)
        {
            GUILayout.Label("• " + feat.Title, _featTitleStyle);
            GUILayout.Label(feat.Description, _smallStyle);
        }

        GUILayout.Space(8f);
        GUILayout.Label("Each game updates its column while it is open; the other column shows where it was left.", _smallStyle);
        GUILayout.EndScrollView();
    }

    private void DrawSplitBar(float leftShare)
    {
        Rect r = GUILayoutUtility.GetRect(10f, 8f, GUILayout.ExpandWidth(true));
        float split = r.width * Mathf.Clamp01(leftShare);
        GUI.DrawTexture(new Rect(r.x, r.y, split, r.height), _hkBarTex);
        GUI.DrawTexture(new Rect(r.x + split, r.y, r.width - split, r.height), _ssBarTex);
    }

    private void DrawProgressBar(float fraction)
    {
        Rect r = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
        GUI.DrawTexture(r, _emptyBarTex);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), _goldBarTex);
    }

    private static string Goal(bool completed, GameStats stats) =>
        completed ? "Done" : stats.Played ? "Not yet" : "Not started";

    private static string GoalAt(GameStats stats) =>
        stats.GoalAtSeconds > 0 ? RunSummary.FormatDuration(stats.GoalAtSeconds) : "—";

    private void Row(string label, string hk, string ss)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(118f));
        GUILayout.Label(hk, RightAligned(), GUILayout.Width(105f));
        GUILayout.Label(ss, RightAligned(), GUILayout.Width(105f));
        GUILayout.EndHorizontal();
    }

    private GUIStyle? _rightLabelStyle;
    private GUIStyle RightAligned() => _rightLabelStyle ??= new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight, wordWrap = false };

    private void Record(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(150f));
        GUILayout.FlexibleSpace();
        GUILayout.Label(value, RightAligned());
        GUILayout.EndHorizontal();
    }
}
