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
        private const float gravityMult = 0.7f;
        private const float massMult = 0.1f;
        private bool playerCrouchOnJump;
        public override IEnumerator OnLoadCoroutine()
        {
            this.turnAllowed = this.level.ParseLevelOption("turnAllowed", true);
            this.highJump = this.level.ParseLevelOption("highJumpt", true);
            Debug.Log("[GettingOverIt] Getting Over It mode activated");
            EventManager.OnPlayerSpawned += this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn += this.EventManager_onCreatureSpawn;
            return base.OnLoadCoroutine();
        }
        public override void OnUnload()
        {
            EventManager.OnPlayerSpawned -= this.ModifyPlayer_onSpawn;
            EventManager.onCreatureSpawn -= this.EventManager_onCreatureSpawn;
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
        }
        private void EventManager_onCreatureSpawn(Creature creature)
        {
            if(creature.isPlayer)
            {
                creature.equipment.UnequipAllWardrobes(true);
                creature.currentLocomotion.SetPhysicModifier(
                    this,
                    gravityMultiplier: this.highJump ? (float?)gravityMult : null,
                    massMultiplier: this.highJump ? massMult: -1);
                // TODO: keep crouching
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
    }
}