using ThunderRoad;
using UnityEngine;
using Extensions;
using System.Security.Policy;

namespace GettingOverIt
{
    public class GettingIt : ThunderScript
    {
        [ModOptionCategory("General", 0, "ModOpts.category_general")]
        [ModOption("getting_over_it", nameLocalizationId = "ModOpts.getting_over_it", order = 0, defaultValueIndex = 1)]
        [ModOptionTooltip("A", "ModOpts.getting_over_it_desc")]
        public static bool OptEnabled { get; set; }
        [ModOptionCategory("General", 0, "ModOpts.category_general")]
        [ModOption("enable_in_home", nameLocalizationId = "ModOpts.enable_in_home", order = 1, defaultValueIndex = 0)]
        public static bool OptEnabledHome { get; set; }
        [ModOptionCategory("General", 0, "ModOpts.category_general")]
        [ModOption("enable_turn", nameLocalizationId = "ModOpts.enable_turn", order = 2, defaultValueIndex = 0)]
        [ModOptionTooltip("A", "ModOpts.enable_turn_desc")] 
        public static bool OptEnableTurn
        {
            get => _optEnableTurn;
            set
            {
                _optEnableTurn = value;
                if(Player.local != null)
                {
                    Player.TogglePlayerTurn(_optEnableTurn);
                }
            }
        }
        private static bool _optEnableTurn = false;
        [ModOptionCategory("General", 0, "ModOpts.category_general")]
        [ModOption("high_jump", nameLocalizationId = "ModOpts.high_jump", order = 3, defaultValueIndex = 1)]
        [ModOptionTooltip("A", "ModOpts.high_jump_desc")]
        public static bool OptHighJump { get; set; }
        [ModOptionCategory("General", 0, "ModOpts.category_general")]
        [ModOption("fast_swing", nameLocalizationId = "ModOpts.fast_swing", order = 4, defaultValueIndex = 1)]
        [ModOptionTooltip("A", "ModOpts.fast_swing_desc")]
        public static bool OptFastSwing { get; set; }
        public override void ScriptLoaded(ModManager.ModData modData)
        {
            base.ScriptLoaded(modData);
        }
        public override void ScriptEnable()
        {
            Debug.Log("Activating Getting Over It mode...");
            this.crouchOnJump = Player.crouchOnJump;
            Player.onSpawn += this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn += this.EventManager_onCreatureSpawn;
            EventManager.onLevelUnload += EventManager_onLevelUnload;
            base.ScriptEnable();
        }
        public override void ScriptDisable()
        {
            Debug.Log("Deactivating Getting Over It mode...");
            Player.crouchOnJump = this.crouchOnJump;
            Player.onSpawn -= this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn -= this.EventManager_onCreatureSpawn;
            EventManager.onLevelUnload -= EventManager_onLevelUnload;
            TogglePlayerMove(true);
            base.ScriptDisable();
        }
        private void ModifyPlayer_onSpawn(Player player)
        {
            if (OptEnabled)
            {
                CreatureData cd = Catalog.GetData<CreatureData>(Player.characterData.creatureId);
                this.BackupfastSwing(cd);
            }
        }
        private void EventManager_onCreatureSpawn(Creature creature)
        {
            if(creature.isPlayer && OptEnabled && (OptEnabledHome || Level.current.data.id != "Home" ))
            {
                Player.crouchOnJump = false; // ther pot interrupt view if enabled
                TogglePlayerMove(false);
                // TODO: keep crouching
                creature.currentLocomotion.SetPhysicModifier(
                    this,
                    gravityMultiplier: OptHighJump ? (float?)gravityMult : null,
                    massMultiplier: OptHighJump ? massMult: -1);
                if(OptFastSwing)
                {
                    CreatureData cd = Catalog.GetData<CreatureData>("GettingOverItCreatureData");
                    this.ApplyCreatureDataValues(creature, cd);
                }
                creature.equipment.UnequipAllWardrobes(true);
                Catalog.InstantiateAsync(
                    "GettingOverIt.Pot",
                    creature.ragdoll.rootPart.transform.position,
                    Quaternion.identity,
                    creature.ragdoll.rootPart.transform,
                    delegate (GameObject go) {
                        go.transform.localScale = Vector3.one * 1.4f - Vector3.up * 0.3f;
                        go.transform.localPosition = Vector3.right * 0.5f + Vector3.forward * 0.1f ; // humans vertical axis is upside down X
                    },
                    "loadPot");
                Catalog.GetData<ItemData>("GOILongHammer").SpawnAsync(
                    delegate (Item item)
                    {
                        creature.handRight.Grab(item.handles[0]);
                        item.handles[0].data.forceClimbing = true;
                    },
                    creature.ragdoll.headPart.transform.position + Vector3.forward * 0.3f);
            }
        }
        private void EventManager_onLevelUnload(LevelData levelData, EventTime eventTime)
        {
            if (Player.currentCreature != null && eventTime == EventTime.OnStart && OptEnabled)
            {
                this.ApplyCreatureDataValues(Player.currentCreature, this.creatureData_backup) ;
            }
        }
        private void BackupfastSwing(CreatureData cd)
        {
            this.creatureData_backup.forceMaxPosition = cd.forceMaxPosition;
            this.creatureData_backup.forceMaxRotation = cd.forceMaxRotation;
            this.creatureData_backup.forcePositionSpringDamper = cd.forcePositionSpringDamper;
            this.creatureData_backup.forceRotationSpringDamper = cd.forceRotationSpringDamper;
            this.creatureData_backup.forcePositionSpringDamper2HMult = cd.forcePositionSpringDamper2HMult;
            this.creatureData_backup.forceRotationSpringDamper2HMult = cd.forceRotationSpringDamper2HMult;
        }
        private static void TogglePlayerMove(bool value)
        {
            if (Player.local != null)
            {
                Player.TogglePlayerMovement(value);
                Player.TogglePlayerJump(value);
                Player.TogglePlayerGroundMovement(value);
                Player.TogglePlayerTurn(_optEnableTurn);
            }
        }
        private void ApplyCreatureDataValues(Creature creature, CreatureData creatureData)
        {
            creature.data.forceMaxPosition = this.creatureData_backup.forceMaxPosition;
            creature.data.forceMaxRotation = this.creatureData_backup.forceMaxRotation;
            creature.data.forcePositionSpringDamper = this.creatureData_backup.forcePositionSpringDamper;
            creature.data.forceRotationSpringDamper = this.creatureData_backup.forceRotationSpringDamper;
            creature.data.forcePositionSpringDamper2HMult = this.creatureData_backup.forcePositionSpringDamper2HMult;
            creature.data.forceRotationSpringDamper2HMult = this.creatureData_backup.forceRotationSpringDamper2HMult;
        }
        private CreatureData creatureData_backup = new();
        private const float gravityMult = 0.7f;
        private const float massMult = 0.1f;
        private bool crouchOnJump = false;
    }
}