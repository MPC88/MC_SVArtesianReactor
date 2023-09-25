﻿using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MC_SVArtesianReactor
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.artesianreactor";
        public const string pluginName = "SV Artesian Reactor";
        public const string pluginVersion = "1.0.0";

        private const string equipmentName = "Artesian Experimental Reactor";
        private const string description = "Overload the reactor, providing a +<LEVEL> boost to all power systems at the cost of a -1% hull per second per powered system.";

        private static int shipID = 333;
        private static int energyCost = 0;
        private static float rarityCostMod = 0f;
        private static float cooldown = 20f;
        private static float energyGen = 442f;
        private static float energyGenMult = 1.2f;
        private static float hpPercent = 0.1f;

        private static int equipID = 45353;
        private static Sprite equipmentIcon;
        private static GameObject audioGO;
        private static GameObject buffGO = null;

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            string pluginfolder = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
            string bundleName = "artesian battlecruiser";
            AssetBundle assets = AssetBundle.LoadFromFile($"{pluginfolder}\\Ships Data\\{bundleName}");
            equipmentIcon = assets.LoadAsset<Sprite>("Assets/ArtesianExperimentalReactor.png");
            audioGO = assets.LoadAsset<GameObject>("Assets/ArtesianExperimentalReactorAudio.prefab");
        }

        [HarmonyPatch(typeof(DemoControl), "SpawnMainMenuBackground")]
        [HarmonyPostfix]
        private static void DemoControlSpawnMainMenuBackground_Post()
        {
            List<ShipBonus> sb = new List<ShipBonus>(ShipDB.GetModel(shipID).modelBonus);
            sb.Add(new SB_BuiltInEquipment() { equipmentID = equipID });
            ShipDB.GetModel(shipID).modelBonus = sb.ToArray();
        }

        [HarmonyPatch(typeof(EquipmentDB), "LoadDatabaseForce")]
        [HarmonyPostfix]
        private static void EquipmentDBLoadDBForce_Post()
        {
            AccessTools.StaticFieldRefAccess<List<Equipment>>(typeof(EquipmentDB), "equipments").Add(CreateEquipment());
        }

        private static Equipment CreateEquipment()
        {
            Equipment equipment = ScriptableObject.CreateInstance<Equipment>();
            equipment.name = equipID + "." + equipmentName;
            equipment.id = equipID;
            equipment.refName = equipmentName;
            equipment.minShipClass = ShipClassLevel.Shuttle;
            equipment.activated = true;
            equipment.enableChangeKey = true;
            equipment.space = 10;
            equipment.energyCost = energyCost;
            equipment.energyCostPerShipClass = false;
            equipment.rarityCostMod = rarityCostMod;
            equipment.techLevel = 40;
            equipment.sortPower = 2;
            equipment.massChange = 0;
            equipment.type = EquipmentType.Device;
            equipment.effects = new List<Effect>() { new Effect() { type = 0, description = "", mod = 1f, value = energyGen, uniqueLevel = 0 } };
            equipment.uniqueReplacement = false;
            equipment.rarityMod = energyGenMult;
            equipment.sellChance = 0;
            equipment.repReq = new ReputationRequisite() { factionIndex = 0, repNeeded = 0 };
            equipment.dropLevel = DropLevel.DontDrop;
            equipment.lootChance = 0;
            equipment.spawnInArena = false;
            equipment.sprite = equipmentIcon;
            equipment.activeEquipmentIndex = equipID;
            equipment.defaultKey = KeyCode.Alpha3;
            equipment.requiredItemID = -1;
            equipment.requiredQnt = 0;
            equipment.equipName = equipmentName;
            equipment.description = description;
            equipment.craftingMaterials = null;
            if (buffGO == null)
                MakeBuffGO(equipment);
            equipment.buff = buffGO;

            return equipment;
        }

        private static void MakeBuffGO(Equipment equip)
        {
            buffGO = new GameObject { name = "NukeTransporter" };
            buffGO.AddComponent<BuffControl>();
            buffGO.GetComponent<BuffControl>().owner = null;
            buffGO.GetComponent<BuffControl>().activeEquipment = MakeActiveEquip(
                equip, null, equip.defaultKey, 1, 0);
        }

        private static AE_MCArtesianReactor MakeActiveEquip(Equipment equipment, SpaceShip ss, KeyCode key, int rarity, int qnt)
        {
            AE_MCArtesianReactor activeEquip = new AE_MCArtesianReactor
            {
                id = equipment.id,
                rarity = rarity,
                key = key,
                ss = ss,
                isPlayer = (ss != null && ss.CompareTag("Player")),
                equipment = equipment,
                qnt = qnt
            };
            activeEquip.active = false;
            return activeEquip;
        }

        [HarmonyPatch(typeof(ActiveEquipment), "ActivateDeactivate")]
        [HarmonyPrefix]
        internal static void ActivateDeactivate_Pre(ActiveEquipment __instance)
        {
            if (__instance != null || __instance.equipment != null ||
                __instance.equipment.id != equipID)
                return;

            if (buffGO == null)
                MakeBuffGO(__instance.equipment);
            buffGO.GetComponent<BuffControl>().activeEquipment = __instance;
            __instance.equipment.buff = buffGO;
        }

        [HarmonyPatch(typeof(ActiveEquipment), "AddActivatedEquipment")]
        [HarmonyPrefix]
        private static bool ActiveEquipmentAdd_Pre(Equipment equipment, SpaceShip ss, KeyCode key, int rarity, int qnt, ref ActiveEquipment __result)
        {
            if (GameManager.instance != null && GameManager.instance.inGame &&
                equipment.id == equipID)
            {
                __result = MakeActiveEquip(equipment, ss, key, rarity, qnt);
                ss.activeEquips.Add(__result);
                __result.AfterConstructor();
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(EquipmentDB), nameof(EquipmentDB.GetEquipmentString))]
        [HarmonyPostfix]
        private static void EquipmentDBGetEquipmentString_Post(int id, int rarity, ref string __result)
        {
            if (id != Main.equipID)
                return;

            __result = __result.Replace("<LEVEL>", rarity.ToString());
        }

        public class AE_MCArtesianReactor : AE_BuffBased
        {
            protected override bool showBuffIcon
            {
                get
                {
                    return this.isPlayer;
                }
            }

            private readonly List<int> affectedSystems = new List<int>();
            private float cnt;

            public AE_MCArtesianReactor()
            {
                this.targetIsSelf = true;
                this.saveState = true;
                this.saveCooldownID = this.id;
                this.cooldownTime = cooldown;
            }

            public override void ActivateDeactivate(bool shiftPressed, Transform target)
            {
                this.startEnergyCost = this.equipment.energyCost;

                if (this.active)
                    base.ActivateDeactivate(shiftPressed, target);

                base.ActivateDeactivate(shiftPressed, target);
            }

            public override void AfterActivate()
            {
                base.AfterActivate();

                this.affectedSystems.Clear();
                this.cnt = 0;

                for (int system = 0; system < this.ss.energyMmt.level.Length; system++)
                {
                    if (this.ss.energyMmt.level[system] > -3)
                    {
                        this.ss.energyMmt.level[system] += this.rarity;
                        if (this.ss.energyMmt.level[system] > 7)
                            this.ss.energyMmt.level[system] = 7;
                        this.affectedSystems.Add(system);
                    }
                }

                GameObject audio = GameObject.Instantiate(audioGO, ss.transform.position, ss.transform.rotation);
                audio.GetComponent<AudioSource>().volume = SoundSys.SFXvolume;
            }

            public override void AfterDeactivate()
            {
                base.AfterDeactivate();

                this.affectedSystems.ForEach(sys => {
                    this.ss.energyMmt.level[sys] -= this.rarity;
                    if (this.ss.energyMmt.level[sys] < -3)
                        this.ss.energyMmt.level[sys] = -3;
                });
            }

            public void Update()
            {
                cnt += Time.deltaTime;

                if (cnt >= 1)
                {
                    ss.currHP -= ss.baseHP * hpPercent * affectedSystems.Count;
                    cnt = 0;
                }
            }
        }
    }
}
