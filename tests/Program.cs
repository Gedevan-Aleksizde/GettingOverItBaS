using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using GettingOverIt;
using ThunderRoad;

class Program
{
    static int checks;
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string managed = @"C:/Program Files (x86)/Steam/steamapps/common/Blade & Sorcery/BladeAndSorcery_Data/Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string file = new AssemblyName(e.Name).Name + ".dll";
            string candidate = Path.Combine(managed, file);
            if (!File.Exists(candidate)) candidate = Path.Combine(root, "bin/Release/net472", file);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        try { Run(); Console.WriteLine($"PASS: {checks} assertions"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        foreach (var id in new[] { null, "", "Home", "home", "mainMenu", "Master", "CharacterSelection" })
            Check(!GettingIt.Eligible(id), "Excluded level: " + id);
        Check(GettingIt.Eligible("Arena"), "Arena allowed");
        Check(GettingIt.Eligible("CustomModMap"), "Custom map allowed");
        TestOutfitLayout();
        TestLevelActivation();
        var original = new LevelData.Mode { name = "Sandbox", allowGameModes = new List<string> { "Sandbox", "CrystalHunt" } };
        original.modules.Add(new LevelModuleCleaner { cleanerRate = 7 });
        original.modules.Add(new SpatialTestModule
        {
            position = new UnityEngine.Vector3(1, 2, 3),
            rotation = new UnityEngine.Quaternion(0, 0, 0, 1)
        });
        var level = new LevelData { id = "Arena", modes = new List<LevelData.Mode> { original } };
        Check(GettingIt.TryAddMode(level), "Register Sandbox mode");
        Check(level.modes.Count == 2, "Preserve original mode");
        Check(level.modes[1].displayName == GettingIt.ModeName, "Map mode title resolves through Default group");
        Check(level.modes[1].description == GettingIt.ModeDescriptionId, "Map mode description uses localization ID");
        Check(level.modes[1].allowGameModes.Count == 1 && level.modes[1].allowGameModes[0] == "Sandbox", "Restrict to Sandbox");
        level.modes[1].allowGameModes.Clear();
        Check(original.allowGameModes.Count == 2, "Do not mutate original mode");
        Check(level.modes[1].modules[0] is LevelModuleCleaner, "Preserve module type");
        ((LevelModuleCleaner)level.modes[1].modules[0]).cleanerRate = 99;
        Check(((LevelModuleCleaner)original.modules[0]).cleanerRate == 7, "Modules are independent");
        var spatial = (SpatialTestModule)level.modes[1].modules[1];
        Check(spatial.position.x == 1 && spatial.position.y == 2 && spatial.position.z == 3, "Unity vector survives mode clone");
        Check(spatial.rotation.w == 1 && spatial.rotation.x == 0, "Unity quaternion survives mode clone");
        Check(!GettingIt.TryAddMode(level) && level.modes.Count == 2, "Idempotent registration");
        level.id = "Home"; level.modes.RemoveAt(1);
        Check(!GettingIt.TryAddMode(level), "Never add a home mode");
        level.id = "CrystalOnly"; original.allowGameModes = new List<string> { "CrystalHunt" };
        Check(!GettingIt.TryAddMode(level), "Do not enable Crystal Hunt maps");
    }

    static void TestOutfitLayout()
    {
        // Slots/names inspected from the actual 1.3.2 body prefabs, not invented layer meanings.
        var basis = new Dictionary<ThunderRoad.Manikin.ManikinLocations.LocationKey, string>
        {
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Torso", 7)] = "HumanMale_Base_Torso",
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Legs", 5)] = "HumanMale_Base_Legs",
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Legs", 3)] = "HumanMale_Underwear",
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Head", 12)] = "HumanMale_Base_Head"
        };
        var body = new Dictionary<ThunderRoad.Manikin.ManikinLocations.LocationKey, string>
        {
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Head", 12)] = "HumanMale_Kharese_Head01"
        };
        var pot = new Dictionary<ThunderRoad.Manikin.ManikinLocations.LocationKey, string>
        {
            [new ThunderRoad.Manikin.ManikinLocations.LocationKey("Torso", 30)] = "GettingOverItPot_HumanMale"
        };
        var target = ModeOutfit.BuildLayout(basis, body, pot);
        Check(target[new ThunderRoad.Manikin.ManikinLocations.LocationKey("Torso", 7)] == "HumanMale_Base_Torso", "Pot never replaces torso skin");
        Check(target[new ThunderRoad.Manikin.ManikinLocations.LocationKey("Legs", 5)] == "HumanMale_Base_Legs", "Keep legs mesh");
        Check(target[new ThunderRoad.Manikin.ManikinLocations.LocationKey("Legs", 3)] == "HumanMale_Underwear", "Keep male underwear mesh");
        Check(target[new ThunderRoad.Manikin.ManikinLocations.LocationKey("Head", 12)] == "HumanMale_Kharese_Head01", "Keep chosen ethnicity head");
        Check(target[new ThunderRoad.Manikin.ManikinLocations.LocationKey("Torso", 30)] == "GettingOverItPot_HumanMale", "Keep pot in separate slot");
        Check(basis.Count == 4 && basis.ContainsValue("HumanMale_Underwear"), "Baseline unchanged for restoration");
        var again = ModeOutfit.BuildLayout(basis, body, pot);
        Check(again.Count == target.Count, "Repeated reconciliation is stable");
        var female = ModeOutfit.BuildLayout(basis, body, pot);
        Check(female.ContainsKey(new ThunderRoad.Manikin.ManikinLocations.LocationKey("Legs", 3)), "Female filtering does not strip underwear");
    }

    static void TestLevelActivation()
    {
        var policy = new LevelActivation();
        Check(!ModOptions.EnableCrystalHunt, "Crystal Hunt option defaults to disabled");
        var option = (ModOption)typeof(ModOptions).GetField(nameof(ModOptions.EnableCrystalHunt)).GetCustomAttributes(typeof(ModOption), false)[0];
        Check(option.defaultValueIndex == 0 && !ModOptionBool.defaultValues[0].boolValue, "UI default is disabled");
        Check(option.nameLocalizationId == "GettingOverItOpt.CrystalHunt" && option.tooltipLocalizationId == "GettingOverItOpt.CrystalHunt_desc", "Option uses owned localization IDs");
        Check(!policy.ShouldApply("CrystalHunt", "DungeonOutpost", "CrystalHunt", true), "No activation before a level load");
        policy.BeginLevel(false);
        ModOptions.EnableCrystalHunt = true;
        Check(!policy.ShouldApply("CrystalHunt", "DungeonOutpost", "CrystalHunt", true), "Enabling mid-level is deferred");
        policy.BeginLevel(ModOptions.EnableCrystalHunt);
        Check(policy.ShouldApply("CrystalHunt", "DungeonOutpost", "CrystalHunt", true), "Next dungeon activates option");
        Check(policy.ShouldApply("CrystalHunt", "DungeonDalgarian", "CrystalHunt", true), "Dalgarian dungeon supported");
        Check(policy.ShouldApply("CrystalHunt", "CustomDungeon", "CustomMode", true), "Custom area dungeons supported");
        ModOptions.EnableCrystalHunt = false;
        Check(policy.ShouldApply("CrystalHunt", "DungeonOutpost", "CrystalHunt", true), "Disabling mid-level is deferred");
        foreach (var id in new[] { "Home", "mainMenu", "Master", "CharacterSelection" })
            Check(!policy.ShouldApply("CrystalHunt", id, "CrystalHunt", true), "Never activate in " + id);
        Check(!policy.ShouldApply("CrystalHunt", "Shop", "CrystalHunt", false), "Shop excluded");
        Check(!policy.ShouldApply("CrystalHunt", "Arena", "WaveAssault", false), "Arena excluded");
        Check(!policy.ShouldApply("Unknown", "DungeonOutpost", "CrystalHunt", true), "Other game modes excluded");
        Check(!policy.ShouldApply("Sandbox", "Arena", "Sandbox", false), "Standard Sandbox unaffected");
        Check(policy.ShouldApply("Sandbox", "Arena", GettingIt.ModeName, false), "Dedicated Sandbox mode still works");
        policy.BeginLevel(ModOptions.EnableCrystalHunt);
        Check(!policy.ShouldApply("CrystalHunt", "DungeonOutpost", "CrystalHunt", true), "Disable applies on next level");
    }
}

// Regression fixture: real map modules contain Vector3/Quaternion fields.
public class SpatialTestModule : LevelModule
{
    public UnityEngine.Vector3 position;
    public UnityEngine.Quaternion rotation;
}
