using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>Combined end-of-run stats tab: both games' play time, deaths, kills, checks and completion.</summary>
internal sealed partial class ApOverlay
{
    private RunSummary? _runSummary;
    private float _nextRunSummaryRefresh;

    private void DrawRunStatsTab()
    {
        if (_runSummary == null || Time.unscaledTime >= _nextRunSummaryRefresh)
        {
            RunStatsTracker.Flush();
            _runSummary = HkssPlugin.State.LoadSummary();
            _nextRunSummaryRefresh = Time.unscaledTime + 2f;
        }
        RunSummary s = _runSummary;

        bool done = s.HkGoalCompleted && s.SsGoalCompleted;
        string heading = s.SteelSoulDefeated
            ? $"Steel Soul defeated in {DoubleSteelSoul.FormatWorldName(s.SteelSoulDefeatedGame)}"
            : done
                ? (s.DoubleSteelSoul ? "Double Steel Soul complete" : "Run complete")
                : (s.DoubleSteelSoul ? "Double Steel Soul — In progress" : "Run in progress");
        GUILayout.Label(heading, _sectionStyle);
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("", _labelStyle, GUILayout.Width(110f));
        GUILayout.Label("Hallownest", _sectionStyle, GUILayout.Width(120f));
        GUILayout.Label("Pharloom", _sectionStyle, GUILayout.Width(120f));
        GUILayout.EndHorizontal();

        Row("Goal", Goal(s.HkGoalCompleted, s.HollowKnight), Goal(s.SsGoalCompleted, s.Silksong));
        Row("Play time", RunSummary.FormatDuration(s.HollowKnight.PlaySeconds), RunSummary.FormatDuration(s.Silksong.PlaySeconds));
        Row("Completion", $"{s.HollowKnight.CompletionPercent:0}%", $"{s.Silksong.CompletionPercent:0}%");
        Row("Checks", RunSummary.FormatChecks(s.HollowKnight.ChecksFound, s.HollowKnight.ChecksTotal),
            RunSummary.FormatChecks(s.Silksong.ChecksFound, s.Silksong.ChecksTotal));
        Row("Deaths", s.HollowKnight.Deaths.ToString(), s.Silksong.Deaths.ToString());
        Row("Kills", s.HollowKnight.Kills.ToString(), s.Silksong.Kills.ToString());

        GUILayout.Space(10f);
        GUILayout.Label("Whole run", _sectionStyle);
        GUILayout.Label($"Play time: {RunSummary.FormatDuration(s.TotalPlaySeconds)}", _labelStyle);
        GUILayout.Label($"Checks: {RunSummary.FormatChecks(s.TotalChecksFound, s.TotalChecks)}", _labelStyle);
        GUILayout.Label($"Deaths: {s.TotalDeaths}    Kills: {s.TotalKills}", _labelStyle);
        GUILayout.Space(6f);
        GUILayout.Label("Each game updates its column while it is open; the other column shows where it was left.", _labelStyle);
    }

    private static string Goal(bool completed, GameStats stats) =>
        completed ? "Done" : stats.Played ? "Not yet" : "Not started";

    private void Row(string label, string hk, string ss)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(110f));
        GUILayout.Label(hk, _labelStyle, GUILayout.Width(120f));
        GUILayout.Label(ss, _labelStyle, GUILayout.Width(120f));
        GUILayout.EndHorizontal();
    }
}
