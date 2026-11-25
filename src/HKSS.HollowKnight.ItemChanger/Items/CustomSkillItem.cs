// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Items/CustomSkillItem.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Items
{
    /// <summary>
    /// Item which ensures that the corresponding custom skill module is enabled on load, and otherwise acts as a BoolItem.
    /// </summary>
    public class CustomSkillItem : AbstractItem
    {
        public string boolName;
        public string moduleName;

        protected override void OnLoad()
        {
            EnsureModuleLoaded();
        }

        private void EnsureModuleLoaded()
        {
            Type T = ResolveModuleType(moduleName);
            if (T == null || !T.IsSubclassOf(typeof(Modules.Module))) throw new InvalidOperationException($"Module type {moduleName} was not found.");
            ItemChangerMod.Modules.GetOrAdd(T);
        }

        private static Type ResolveModuleType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            Type t = Type.GetType(name);
            if (t != null) return t;

            // Embedded ItemChanger JSON references ", ItemChanger" as the assembly name,
            // whereas HKSS compiles ItemChanger into HKSS.HollowKnight.ItemChanger.
            int comma = name.IndexOf(',');
            string typeNameOnly = comma >= 0 ? name.Substring(0, comma).Trim() : name.Trim();
            return typeof(CustomSkillItem).Assembly.GetType(typeNameOnly);
        }

        public override void GiveImmediate(GiveInfo info)
        {
            EnsureModuleLoaded();
            PlayerData.instance.SetBool(boolName, true);
        }

        public override bool Redundant()
        {
            return PlayerData.instance.GetBool(boolName);
        }
    }
}
