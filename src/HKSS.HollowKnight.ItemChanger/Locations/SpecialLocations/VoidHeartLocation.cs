// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/VoidHeartLocation.cs, commit e57bc4e (LGPL-2.1)
using HutongGames.PlayMaker.Actions;
using ItemChanger.Components;
using ItemChanger.FsmStateActions;
using ItemChanger.Extensions;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Location which directly gives items in place of Void Heart following the Dream_Abyss sequence.
    /// </summary>
    public class VoidHeartLocation : AutoLocation, ILocalHintLocation
    {
        public bool HintActive { get; set; } = true;

        protected override void OnLoad()
        {
            Events.AddFsmEdit(UnsafeSceneName, new("End Cutscene", "Control"), EditEndCutscene);
            Events.AddFsmEdit(SceneNames.Abyss_15, new("Mirror", "FSM"), EditMirror);
            Events.AddFsmEdit(SceneNames.Abyss_15, new("Dream Enter Abyss", "Control"), EditDreamEnter);
        }

        protected override void OnUnload()
        {
            Events.RemoveFsmEdit(UnsafeSceneName, new("End Cutscene", "Control"), EditEndCutscene);
            Events.RemoveFsmEdit(SceneNames.Abyss_15, new("Mirror", "FSM"), EditMirror);
            Events.RemoveFsmEdit(SceneNames.Abyss_15, new("Dream Enter Abyss", "Control"), EditDreamEnter);
        }

        private void EditEndCutscene(PlayMakerFSM fsm)
        {
            FsmState? charmGet = fsm.GetState("Charm Get");
            FsmState? removeOvercharm = fsm.GetState("Remove Overcharm");
            FsmState? getMsg = fsm.GetState("Get Msg");

            FsmStateAction give = new AsyncLambda(GiveAll, "GET ITEM MSG END");

            charmGet?.ClearActions();
            removeOvercharm?.ClearActions();
            if (getMsg != null && !getMsg.Actions.Any(a => a is AsyncLambda))
            {
                var kept = getMsg.Actions.Take(1).ToList();
                kept.Add(give);
                getMsg.SetActions(kept.ToArray());
            }
        }

        private void EditDreamEnter(PlayMakerFSM fsm)
        {
            FsmState? init = fsm.GetState("Init");
            if (init != null && !init.Actions.Any(a => a is DelegateBoolTest))
            {
                var kept = init.Actions.Take(1).ToList();
                kept.Add(new DelegateBoolTest(Placement.AllObtained, "INACTIVE", null));
                init.SetActions(kept.ToArray());
            }
        }

        private void EditMirror(PlayMakerFSM fsm)
        {
            if (this.GetItemHintActive()) HintBox.Create(fsm.transform, Placement);

            FsmState? check = fsm.GetState("Check");
            if (check != null)
            {
                PlayerDataBoolTest? pdbt = check.GetFirstActionOfType<PlayerDataBoolTest>();
                if (pdbt != null)
                {
                    check.SetActions(new DelegateBoolTest(Placement.AllObtained, pdbt));
                }
            }
        }
    }
}
