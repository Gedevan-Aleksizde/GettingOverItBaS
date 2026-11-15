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
        var original = new LevelData.Mode { name = "Sandbox", allowGameModes = new List<string> { "Sandbox", "CrystalHunt" } };
        original.modules.Add(new LevelModuleCleaner { cleanerRate = 7 });
        var level = new LevelData { id = "Arena", modes = new List<LevelData.Mode> { original } };
        Check(GettingIt.TryAddMode(level), "Register Sandbox mode");
        Check(level.modes.Count == 2, "Preserve original mode");
        Check(level.modes[1].allowGameModes.Count == 1 && level.modes[1].allowGameModes[0] == "Sandbox", "Restrict to Sandbox");
        level.modes[1].allowGameModes.Clear();
        Check(original.allowGameModes.Count == 2, "Do not mutate original mode");
        Check(level.modes[1].modules[0] is LevelModuleCleaner, "Preserve module type");
        ((LevelModuleCleaner)level.modes[1].modules[0]).cleanerRate = 99;
        Check(((LevelModuleCleaner)original.modules[0]).cleanerRate == 7, "Modules are independent");
        Check(!GettingIt.TryAddMode(level) && level.modes.Count == 2, "Idempotent registration");
        level.id = "Home"; level.modes.RemoveAt(1);
        Check(!GettingIt.TryAddMode(level), "Never add a home mode");
        level.id = "CrystalOnly"; original.allowGameModes = new List<string> { "CrystalHunt" };
        Check(!GettingIt.TryAddMode(level), "Do not enable Crystal Hunt maps");
        Check(PotMesh.Profile[0].x == 0 && PotMesh.Profile[PotMesh.Profile.Length - 1].x == 0, "Pot floor is closed");
        Check(PotMesh.Profile[5].x > PotMesh.Profile[6].x && PotMesh.Profile[5].y == PotMesh.Profile[6].y, "Pot has hollow rim");
    }
}
