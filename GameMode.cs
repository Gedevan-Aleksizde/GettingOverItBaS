using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ThunderRoad;
using UnityEngine;
using Extensions;

namespace GettingOverIt
{
    public class GameMode : LevelModule
    {
        public bool turnAllowed = true;
        public bool highJump = true;
        public bool fastSwing = true;
        private const float gravityMult = 0.7f;
        private const float massMult = 0.1f;
        private readonly Vector2 springDamper = new Vector2(4000f, 100f);
        private readonly Vector2 springDamper2HMult = new Vector2(2f, 1f);
        private const float forceMaxPosition = 10000f;
        private const float forceMaxRotation = 10000f;
        private Vector2 _forcePositionSpringDamper;
        private Vector2 _forceRotationSpringDamper;
        private Vector2 _forceRotationSpringDamper2HMult;
        private Vector2 _forcePositionSpringDamper2HMult;
        private float _forceMaxPosition;
        private float _forceMaxRotation;
        public override IEnumerator OnLoadCoroutine()
        {
            this.turnAllowed = this.level.ParseLevelOption("turnAllowed", true);
            this.highJump = this.level.ParseLevelOption("highJumpt", true);
            this.fastSwing = this.level.ParseLevelOption("fastSwing", true);
            EventManager.OnPlayerSpawned += this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn += this.EventManager_onCreatureSpawn;
            EventManager.onLevelUnload += EventManager_onLevelUnload;
            Debug.Log("[GettingOverIt] Getting Over It mode activated");
            return base.OnLoadCoroutine();
        }
        public override void OnUnload()
        {
            EventManager.OnPlayerSpawned -= this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn -= this.EventManager_onCreatureSpawn;
            EventManager.onLevelUnload -= EventManager_onLevelUnload;
            Debug.Log("[GettingOverIt] Getting Over It mode deactivated");
            base.OnUnload();
        }
        private void ModifyPlayer_onSpawn()
        {
            if(this.turnAllowed)
            {
                Player.TogglePlayerMovement(false);
                Player.TogglePlayerJump(false);
            }
            else
            {
                Player.TogglePlayerGroundMovement(false);
            }
            Player.crouchOnJump = false; // ther pot interrupt view if enabled
            CreatureData cd = Catalog.GetData<CreatureData>(Player.characterData.creatureId);
            this._forceMaxRotation = cd.forceMaxRotation;
            this._forceMaxPosition = cd.forceMaxPosition;
            this._forcePositionSpringDamper = cd.forcePositionSpringDamper;
            this._forceRotationSpringDamper = cd.forceRotationSpringDamper;
            this._forcePositionSpringDamper2HMult= cd.forcePositionSpringDamper2HMult;
            this._forceRotationSpringDamper2HMult = cd.forceRotationSpringDamper2HMult;
        }
        private void EventManager_onCreatureSpawn(Creature creature)
        {
            Debug.Log("[GettingOverIt] Player creature spawned As Diogenes");
            if(creature.isPlayer)
            {
                // TODO: keep crouching
                creature.currentLocomotion.SetPhysicModifier(
                    this,
                    gravityMultiplier: this.highJump ? (float?)gravityMult : null,
                    massMultiplier: this.highJump ? massMult: -1);
                if(this.fastSwing)
                {
                    //TODO: refactor
                    creature.data.forceMaxPosition = forceMaxPosition;
                    creature.data.forceMaxRotation = forceMaxRotation;
                    creature.data.forcePositionSpringDamper = this.springDamper;
                    creature.data.forceRotationSpringDamper = this.springDamper;
                    creature.data.forcePositionSpringDamper2HMult = this.springDamper2HMult;
                    creature.data.forceRotationSpringDamper2HMult = this.springDamper2HMult;                    
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
            if (eventTime == EventTime.OnStart)
            {
                Player.currentCreature.data.forceMaxPosition = this._forceMaxPosition;
                Player.currentCreature.data.forceMaxRotation = this._forceMaxRotation;
                Player.currentCreature.data.forcePositionSpringDamper = this._forcePositionSpringDamper;
                Player.currentCreature.data.forceRotationSpringDamper = this._forceRotationSpringDamper;
                Player.currentCreature.data.forcePositionSpringDamper2HMult = this._forcePositionSpringDamper2HMult;
                Player.currentCreature.data.forceRotationSpringDamper2HMult = this._forceRotationSpringDamper2HMult;
            }
        }
    }
}