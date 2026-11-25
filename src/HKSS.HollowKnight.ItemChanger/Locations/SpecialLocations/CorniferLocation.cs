// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/CorniferLocation.cs, commit e57bc4e (LGPL-2.1)
using HutongGames.PlayMaker.Actions;
using ItemChanger.FsmStateActions;
using ItemChanger.Util;
using ItemChanger.Extensions;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Location which gives items through the interface to purchase a map from Cornifer.
    /// </summary>
    public class CorniferLocation : AutoLocation
    {
        public string? objectName;
        public override bool SupportsCost => true;

        protected override void OnLoad()
        {
            Events.AddFsmEdit(UnsafeSceneName, new(objectName, "Conversation Control"), HandleCorniferConvo);
            Events.AddFsmEdit(UnsafeSceneName, new("Cornifer Card", "FSM"), HandleCorniferCard);
            Events.AddLanguageEdit(new("Cornifer", "CORNIFER_PROMPT"), OnLanguageGet);
            ItemChangerMod.Modules.GetOrAdd<Modules.AltCorniferAtHomeTest>().Add(this);
        }

        protected override void OnUnload()
        {
            Events.RemoveFsmEdit(UnsafeSceneName, new(objectName, "Conversation Control"), HandleCorniferConvo);
            Events.RemoveFsmEdit(UnsafeSceneName, new("Cornifer Card", "FSM"), HandleCorniferCard);
            Events.RemoveLanguageEdit(new("Cornifer", "CORNIFER_PROMPT"), OnLanguageGet);
        }

        private void HandleCorniferConvo(PlayMakerFSM fsm)
        {
            // If an item respawned after paying Cornifer, we destroy Cornifer and spawn a shiny
            // If all of the items are obtained, the checkActive action will destroy Cornifer without spawning a shiny
            if (!IsCorniferPresent() && !Placement.AllObtained())
            {
                Container c = Container.GetContainer(Container.Shiny)!;
                GameObject shiny = c.GetNewContainer(new ContainerInfo(c.Name, Placement, flingType, (Placement as Placements.ISingleCostPlacement)?.Cost));
                c.ApplyTargetContext(shiny, fsm.gameObject, 0);
                ShinyUtility.FlingShinyDown(shiny.LocateMyFSM("Shiny Control"));
                UnityEngine.Object.Destroy(fsm.gameObject);
                return;
            }

            FsmState? checkActive = fsm.GetState("Check Active");
            FsmState? convoChoice = fsm.GetState("Convo Choice");
            FsmState? get = fsm.GetState("Geo Pause and GetMap");
            FsmState? sendText = fsm.GetState("Send Text");

            if (checkActive != null)
            {
                int idx = Array.FindIndex(checkActive.Actions, a => a is PlayerDataBoolTest);
                if (idx >= 0)
                    checkActive.ReplaceAction(new DelegateBoolTest(Placement.AllObtained, (PlayerDataBoolTest)checkActive.Actions[idx]), idx);
            }

            if (convoChoice != null)
            {
                int idx = Array.FindIndex(convoChoice.Actions, a => a is PlayerDataBoolTest);
                if (idx >= 0)
                    convoChoice.ReplaceAction(new DelegateBoolTest(Placement.AllObtained, (PlayerDataBoolTest)convoChoice.Actions[idx]), idx);
            }

            if (get != null && !get.Actions.Any(a => a is AsyncLambda))
            {
                var kept = get.Actions.Take(Math.Min(3, get.Actions.Length)).ToList();
                kept.Add(new Lambda(() => Placement.AddVisitFlag(VisitState.Accepted)));
                kept.Add(new AsyncLambda(GiveAllAsync(fsm.transform), "TALK FINISH"));
                get.SetActions(kept.ToArray());
                get.ClearTransitions();
            }

            // If the placement declares a different cost, we overwrite the map cost
            // otherwise, Cornifer charges the vanilla price
            if (Placement is Placements.ISingleCostPlacement iscp && iscp.Cost is Cost cost)
            {
                if (sendText != null)
                {
                    sendText.ClearActions();
                    sendText.AddFirstAction(new Lambda(() => YNUtil.OpenYNDialogue(fsm.gameObject, Placement, Placement.Items, cost)));
                }
                get?.AddFirstAction(new Lambda(() => { if (!cost.Paid) cost.Pay(); }));
            }

            if (fsm.GetState("Deepnest Check") is FsmState deepnestCheck)
            {
                int idx = Array.FindIndex(deepnestCheck.Actions, a => a is PlayerDataBoolTest);
                if (idx >= 0)
                    deepnestCheck.ReplaceAction(new DelegateBoolTest(Placement.AllObtained, (PlayerDataBoolTest)deepnestCheck.Actions[idx]), idx);
            }
        }

        private void HandleCorniferCard(PlayMakerFSM fsm)
        {
            FsmState? check = fsm.GetState("Check");
            if (check != null)
            {
                int idx = Array.FindIndex(check.Actions, a => a is PlayerDataBoolTest);
                if (idx >= 0)
                    check.ReplaceAction(new DelegateBoolTest(IsCorniferCardPresent, (PlayerDataBoolTest)check.Actions[idx]), idx);
            }
        }

        public bool IsCorniferPresent() => !Placement.CheckVisitedAll(VisitState.Accepted) && !Placement.AllObtained(); // we set the special flag after purchasing from Cornifer
        public bool IsCorniferCardPresent() => Placement.AllObtained();

        private void OnLanguageGet(ref string value)
        {
            if (GameManager.instance.sceneName == UnsafeSceneName)
            {
                value = Placement.GetUIName();
                Placement.OnPreview(value);
            }
        }
    }
}
