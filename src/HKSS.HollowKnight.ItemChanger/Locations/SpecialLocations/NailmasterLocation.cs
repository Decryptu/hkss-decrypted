// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/NailmasterLocation.cs, commit e57bc4e (LGPL-2.1)
using HutongGames.PlayMaker.Actions;
using ItemChanger.FsmStateActions;
using ItemChanger.Util;
using ItemChanger.Extensions;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Location which gives items when a Nailmaster would teach their nail art.
    /// </summary>
    public class NailmasterLocation : AutoLocation
    {
        public string objectName;
        public string fsmName;
        public override bool SupportsCost => true;

        protected override void OnLoad()
        {
            Events.AddFsmEdit(UnsafeSceneName, new(objectName, fsmName), EditNailmasterConvo);
            Events.AddLanguageEdit(new("Prompts", "NAILMASTER_FREE"), OnLanguageGet);
            Events.AddSceneChangeEdit(UnsafeSceneName, MakeShinyForRespawnedItems);
            if (UnsafeSceneName == SceneNames.Room_nailmaster_02) ItemChangerMod.Modules.GetOrAdd<Modules.AltNailsmithSheoTest>().Subscribe(this);
        }

        protected override void OnUnload()
        {
            Events.RemoveFsmEdit(UnsafeSceneName, new(objectName, fsmName), EditNailmasterConvo);
            Events.RemoveLanguageEdit(new("Prompts", "NAILMASTER_FREE"), OnLanguageGet);
            Events.RemoveSceneChangeEdit(UnsafeSceneName, MakeShinyForRespawnedItems);
        }

        private void EditNailmasterConvo(PlayMakerFSM fsm)
        {
            FsmState? convo = fsm.GetState("Convo Choice");
            FsmState? getMsg = fsm.GetState("Get Msg");
            FsmState? sendText = fsm.GetState("Send Text");

            FsmStateAction test = new DelegateBoolTest(MakeOffer, "REOFFER", null);
            FsmStateAction give = new AsyncLambda(GiveAllAsync(fsm.transform), "GET ITEM MSG END");

            if (convo != null)
            {
                int idx = Array.FindIndex(convo.Actions, a =>
                    a is PlayerDataBoolTest pdbt
                    && (pdbt.isTrue?.Name == "REOFFER" || pdbt.isFalse?.Name == "REOFFER"));
                if (idx < 0)
                {
                    int fallback = objectName == "NM Sheo NPC" ? 2 : 1;
                    if (fallback < convo.Actions.Length) idx = fallback;
                }
                if (idx >= 0) convo.ReplaceAction(test, idx);
            }

            if (getMsg != null && !getMsg.Actions.Any(a => a is AsyncLambda))
            {
                var kept = getMsg.Actions.Take(Math.Min(3, getMsg.Actions.Length)).ToList();
                kept.Add(new Lambda(() => Placement.AddVisitFlag(VisitState.Accepted)));
                kept.Add(give);
                getMsg.SetActions(kept.ToArray());
            }

            if (Placement is Placements.ISingleCostPlacement iscp && iscp.Cost is Cost cost)
            {
                if (sendText != null)
                {
                    sendText.ClearActions();
                    sendText.AddFirstAction(new Lambda(() => YNUtil.OpenYNDialogue(fsm.gameObject, Placement, Placement.Items, cost)));
                }
                getMsg?.AddFirstAction(new Lambda(() => { if (!cost.Paid) cost.Pay(); }));
            }
        }

        private void MakeShinyForRespawnedItems(Scene to)
        {
            if (AcceptedOffer() && !Placement.AllObtained())
            {
                GameObject nailmaster = to.FindGameObjectByName(objectName)!;
                Container c = Container.GetContainer(Container.Shiny)!;
                GameObject shiny = c.GetNewContainer(new ContainerInfo(c.Name, Placement, flingType, (Placement as Placements.ISingleCostPlacement)?.Cost));
                c.ApplyTargetContext(shiny, nailmaster.transform.position.x, nailmaster.transform.position.y, 1.5f);
                ShinyUtility.FlingShinyLeft(shiny.LocateMyFSM("Shiny Control"));
            }
        }

        public bool AcceptedOffer() => Placement.CheckVisitedAll(VisitState.Accepted);
        public bool MakeOffer() => !AcceptedOffer() && !Placement.AllObtained();

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
