using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using ThunderRoad;
using ThunderRoad.Skill.SpellPower;
using UnityEngine;

namespace GettingOverIt
{
    public class GettingIt : ThunderScript
    {
        public const string ModeName = "GettingOverIt";
        private readonly HashSet<LevelData> registered = new HashSet<LevelData>();
        private readonly HashSet<LevelData> failedRegistration = new HashSet<LevelData>();
        private Session session;
        private bool leaving;

        public static bool Eligible(string id) => !string.IsNullOrEmpty(id)
            && !id.Equals("Home", StringComparison.OrdinalIgnoreCase)
            && !id.Equals("MainMenu", StringComparison.OrdinalIgnoreCase)
            && !id.Equals("Master", StringComparison.OrdinalIgnoreCase)
            && !id.Equals("CharacterSelection", StringComparison.OrdinalIgnoreCase);

        public override void ScriptEnable()
        {
            EventManager.onLevelUnload += OnLevelUnload;
            EventManager.onCreatureDespawn += OnCreatureDespawn;
            Debug.Log("[GettingOverIt] Script enabled (1.1.2)");
        }

        public override void ScriptUpdate()
        {
            // Catalogs are refreshed after scripts are enabled. Register when data becomes available.
            foreach (LevelData level in Catalog.GetDataList<LevelData>())
            {
                if (!Eligible(level.id) || level.modes == null || registered.Contains(level) || failedRegistration.Contains(level)) continue;
                try
                {
                    if (TryAddMode(level))
                    {
                        registered.Add(level);
                        Debug.Log("[GettingOverIt] Registered level mode: " + level.id);
                    }
                }
                catch (Exception ex)
                {
                    // One incompatible custom map must not block player setup or flood every frame's log.
                    failedRegistration.Add(level);
                    Debug.LogError("[GettingOverIt] Cannot register level mode for " + level.id + ": " + ex);
                }
            }

            var current = Level.current;
            var creature = Player.currentCreature;
            bool active = !leaving && current != null && Eligible(current.data?.id)
                && current.mode?.name == ModeName && Player.characterData?.mode?.data?.id == "Sandbox";
            if (session != null && (!active || session.Creature != creature)) EndSession();
            if (active && session == null && creature != null && creature.loaded && !creature.isKilled
                && Player.local != null && creature.container.contentLoaded && creature.equipment != null
                && creature.equipment.GetPendingApparelLoading() == 0 && creature.manikinLocations.PendingHandles() == 0
                && creature.mana?.casterLeft != null && creature.mana.casterRight != null)
            {
                try
                {
                    session = new Session(creature);
                    session.Enter();
                    Debug.Log("[GettingOverIt] Entered level mode: " + current.data.id);
                }
                catch (Exception ex) { Debug.LogError("[GettingOverIt] Setup failed: " + ex); EndSession(); leaving = true; unloadingLevel = current; }
            }
            try { session?.Update(); }
            catch (Exception ex)
            {
                Debug.LogError("[GettingOverIt] Runtime setup failed; restoring player: " + ex);
                EndSession();
                leaving = true;
                unloadingLevel = current;
            }
            if (session?.Failed == true)
            {
                Debug.LogError("[GettingOverIt] Required item spawn failed; restoring player.");
                EndSession();
                leaving = true;
                unloadingLevel = current;
            }
            if (leaving && current == null) leaving = false;
            // Level unload sets this before the old player can be reactivated; a new level resets it.
            if (leaving && current != null && current != unloadingLevel) leaving = false;
        }

        private Level unloadingLevel;

        public static bool TryAddMode(LevelData level)
        {
            if (!Eligible(level.id) || level.modes == null) return false;
            if (level.modes.Any(m => m.name == ModeName)) return false;
            var original = level.modes.FirstOrDefault(m => m.name == "Sandbox" && m.allowGameModes != null && m.allowGameModes.Contains("Sandbox"));
            if (original == null) return false;
            // Catalog settings include Unity converters (vectors, quaternions, colors, etc.).
            // Plain JSON.NET settings walk Vector3.normalized recursively and abort the update loop.
            var settings = Catalog.GetJsonNetSerializerSettings();
            var mode = JsonConvert.DeserializeObject<LevelData.Mode>(JsonConvert.SerializeObject(original, settings), settings);
            mode.name = ModeName;
            mode.displayName = "Getting Over It";
            mode.description = "Climb using your hammer. Movement and magic are disabled.";
            mode.allowGameModes = new List<string> { "Sandbox" };
            level.modes.Add(mode);
            return true;
        }
        private void OnLevelUnload(LevelData data, LevelData.Mode mode, EventTime time)
        {
            if (time != EventTime.OnStart) return;
            unloadingLevel = Level.current;
            leaving = true;
            EndSession();
        }

        private void EndSession()
        {
            var old = session;
            session = null;
            old?.Dispose();
        }

        private void OnCreatureDespawn(Creature creature, EventTime time)
        {
            if (time == EventTime.OnStart && session?.Creature == creature) EndSession();
        }

        public override void ScriptDisable()
        {
            EventManager.onLevelUnload -= OnLevelUnload;
            EventManager.onCreatureDespawn -= OnCreatureDespawn;
            EndSession();
            foreach (var level in registered) level.modes.RemoveAll(m => m.name == ModeName);
            registered.Clear();
            failedRegistration.Clear();
        }

        private sealed class Session : IDisposable
        {
            public Creature Creature { get; }
            public bool Failed { get; private set; }
            private ItemContent pot;
            private Item hammer;
            private bool disposed;
            private bool oldMove, oldTurn, oldJump, oldCrouch;
            private SavedSpells spells;
            private bool savedSpells;
            private ModeOutfit outfit;
            private bool outfitVerified;
            private float outfitSetupStarted;
            private readonly List<SpellPowerSlowTime> powers = new List<SpellPowerSlowTime>();
            private readonly List<(Item item, Holder holder, RagdollHand hand)> stored = new List<(Item, Holder, RagdollHand)>();

            public Session(Creature creature)
            {
                Creature = creature;
                oldMove = Player.local.locomotion.allowMove;
                oldTurn = Player.local.locomotion.allowTurn;
                oldJump = Player.local.locomotion.allowJump;
                oldCrouch = Player.crouchOnJump;
            }

            public void Enter()
            {
                var hammerData = Catalog.GetData<ItemData>("GOILongHammer", true);
                var potData = Catalog.GetData<ItemData>("GOIPot", true);
                if (hammerData == null || potData == null) throw new InvalidOperationException("Required items missing");
                if (!OriginalPot.IsReady) throw new InvalidOperationException("Original pot assets did not load; see the earlier asset error");
                outfit = new ModeOutfit(Creature);
                outfitSetupStarted = Time.unscaledTime;
                outfit.Enforce(null);
                foreach (var holder in Creature.container.linkedHolders)
                    foreach (var item in holder.items.ToArray()) Store(item, holder, null);
                Store(Creature.handLeft.grabbedHandle?.item, null, Creature.handLeft);
                Store(Creature.handRight.grabbedHandle?.item, null, Creature.handRight);
                spells = Creature.mana.UnloadSpells();
                savedSpells = true;
                Creature.mana.casterLeft.telekinesis = null;
                Creature.mana.casterRight.telekinesis = null;
                foreach (var caster in new[] { Creature.mana.casterLeft, Creature.mana.casterRight })
                {
                    caster.DisallowCasting(this);
                    caster.DisableSpellWheel(this);
                }
                powers.AddRange(Creature.mana.spellPowerInstances.OfType<SpellPowerSlowTime>());
                foreach (var power in powers) { power.StopSlowTime(); Creature.mana.spellPowerInstances.Remove(power); }
                // Spawn through the engine so wardrobe addressables are loaded before equipping.
                potData.SpawnAsync(preview =>
                {
                    if (preview == null) { Failed = true; return; }
                    preview.Despawn();
                    if (disposed || Creature == null || Player.currentCreature != Creature) return;
                    pot = Creature.container.AddItemContent(potData);
                    Creature.equipment.EquipWardrobe(pot);
                    if (!pot.HasState<ContentStateWorn>()) Failed = true;
                    outfit.Enforce(pot);
                }, Creature.handLeft.transform.position, pooled: false);
                Player.crouchOnJump = false;
                Player.TogglePlayerMovement(false);
                Player.TogglePlayerJump(false);
                Player.TogglePlayerTurn(true);
                hammerData.SpawnAsync(item =>
                {
                    if (item == null) { Failed = true; return; }
                    if (disposed || Creature == null || Player.currentCreature != Creature) { item.Despawn(); return; }
                    hammer = item;
                    Creature.handRight.Grab(item.handles[0]);
                    Creature.handRight.OnUnGrabEvent += OnRelease;
                }, Creature.handRight.transform.position, pooled: false);
            }

            private void Store(Item item, Holder holder, RagdollHand hand)
            {
                if (item == null || stored.Any(s => s.item == item)) return;
                stored.Add((item, holder, hand));
                foreach (var handle in item.handles) handle.Release();
                if (holder != null) holder.UnSnap(item, true);
                item.gameObject.SetActive(false);
            }

            private void OnRelease(Side side, Handle handle, bool throwing, EventTime time)
            {
                // Reattach immediately after the engine has completed release, without blocking the left hand.
                if (!disposed && time == EventTime.OnEnd && hammer != null && handle.item == hammer)
                    Creature.handRight.Grab(handle);
            }

            public void Update()
            {
                if (Creature == null) return;
                outfit?.Enforce(pot);
                foreach (var holder in Creature.container.linkedHolders)
                    foreach (var item in holder.items.ToArray())
                        if (item != hammer) Store(item, holder, null);
                if (pot != null && !pot.HasState<ContentStateWorn>())
                {
                    if (!Creature.container.contents.Contains(pot)) Creature.container.AddItemContent(pot);
                    Creature.equipment.EquipWardrobe(pot);
                }
                if (pot != null && !outfitVerified) outfitVerified = outfit.Verify(pot);
                if (!outfitVerified && Time.unscaledTime - outfitSetupStarted > 15f)
                    throw new InvalidOperationException("Outfit/torso verification timed out; restoring the original outfit");
                Player.TogglePlayerMovement(false);
                Player.TogglePlayerJump(false);
                Player.TogglePlayerTurn(true);
                if (hammer != null && Creature.handRight.grabbedHandle?.item != hammer)
                    Creature.handRight.Grab(hammer.handles[0]);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (Creature != null)
                {
                    Creature.handRight.OnUnGrabEvent -= OnRelease;
                    if (hammer != null) hammer.Despawn();
                    if (pot != null) { Creature.equipment.UnequipWardrobe(pot); Creature.container.RemoveContent(pot); }
                    outfit?.Dispose();
                    foreach (var entry in stored)
                    {
                        if (entry.item == null) continue;
                        entry.item.gameObject.SetActive(true);
                        if (entry.holder != null) entry.holder.Snap(entry.item, true);
                        else if (entry.hand != null) entry.hand.Grab(entry.item.handles[0]);
                    }
                    foreach (var caster in new[] { Creature.mana.casterLeft, Creature.mana.casterRight })
                    {
                        caster.AllowCasting(this);
                        caster.AllowSpellWheel(this);
                    }
                    if (savedSpells)
                    {
                        Creature.mana.LoadSpells(spells);
                        Creature.mana.casterLeft.telekinesis = spells.tkLeft;
                        Creature.mana.casterRight.telekinesis = spells.tkRight;
                    }
                    foreach (var power in powers)
                        if (!Creature.mana.spellPowerInstances.Contains(power)) Creature.mana.spellPowerInstances.Add(power);
                }
                if (Player.local != null)
                {
                    Player.TogglePlayerMovement(oldMove);
                    Player.TogglePlayerJump(oldJump);
                    Player.TogglePlayerTurn(oldTurn);
                }
                Player.crouchOnJump = oldCrouch;
                outfit?.Dispose();
            }
        }

        public override void ScriptUnload()
        {
            EndSession();
            OriginalPot.Release();
        }
    }
}
