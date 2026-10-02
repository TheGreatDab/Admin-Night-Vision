/*
 * Admin Night Vision
 * A fork of NightVision by Clearshot, renamed and maintained by Dab / DabTheGreat.
 *
 * MIT License
 *
 * Copyright (c) 2020 Clearshot
 * Copyright (c) 2026 Dab / DabTheGreat
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Network;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Admin Night Vision", "Dab / DabTheGreat", "1.0.3")]
    [Description("Allows admins to see at night")]
    class AdminNightVision : CovalencePlugin
    {
        private PluginConfig _config;
        private Game.Rust.Libraries.Player _rustPlayer = Interface.Oxide.GetLibrary<Game.Rust.Libraries.Player>("Player");
        private EnvSync _envSync;
        private Dictionary<ulong, NVPlayerData> _playerData = new Dictionary<ulong, NVPlayerData>();
        private Dictionary<ulong, float> _playerTimes = new Dictionary<ulong, float>();
        private DateTime _nvDate;
        private List<ulong> _connected = new List<ulong>();

        private string PERM_ALLOWED = "adminnightvision.allowed";
        private string PERM_UNLIMITEDNVG = "adminnightvision.unlimitednvg";
        private string PERM_AUTO = "adminnightvision.auto";

        private bool API_blockEnvUpdates = false;
        private List<KeyValuePair<Plugin, FieldInfo>> _linkedReferences = new List<KeyValuePair<Plugin, FieldInfo>>();
        private Dictionary<Type, FieldInfo[]> _nightVisionFields = new Dictionary<Type, FieldInfo[]>();

        private void SendChatMsg(BasePlayer pl, string msg, string prefix = null) =>
            _rustPlayer.Message(pl, msg, prefix != null ? prefix : lang.GetMessage("ChatPrefix", this, pl.UserIDString), Convert.ToUInt64(_config.chatIconID), Array.Empty<object>());

        private void Init()
        {
            permission.RegisterPermission(PERM_ALLOWED, this);
            permission.RegisterPermission(PERM_UNLIMITEDNVG, this);
            permission.RegisterPermission(PERM_AUTO, this);

            _playerTimes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, float>>($"{Name}\\playerTimes");
        }

        private void OnServerInitialized()
        {
            _envSync = BaseNetworkable.serverEntities.OfType<EnvSync>().FirstOrDefault();

            // NightVision does the same job: both send every player the sky
            // every 5 seconds, so with both loaded a locked player gets day
            // from one and night from the other, and the sky flickers.
            if (plugins.Find("NightVision") != null)
                PrintWarning("NightVision is also loaded. Both send every player the sky, so locked skies will flicker between day and night. Unload one: o.unload NightVision");

            timer.Every(5f, () => {
                LinkNightVisionReferences();

                if (!_envSync.limitNetworking)
                    _envSync.limitNetworking = true;

                List<Connection> subscribers = _envSync.net.group.subscribers;
                if (subscribers != null && subscribers.Count > 0)
                {
                    for (int i = 0; i < subscribers.Count; i++)
                    {
                        Connection connection = subscribers[i];
                        global::BasePlayer basePlayer = connection.player as global::BasePlayer;

                        if (!(basePlayer == null)) {
                            NVPlayerData nvPlayerData = GetNVPlayerData(basePlayer);

                            if (API_blockEnvUpdates && !nvPlayerData.timeLocked) continue;

                            if (connection != null)
                            {
                                var write = Net.sv.StartWrite();
                                connection.validate.entityUpdates = connection.validate.entityUpdates + 1;
                                BaseNetworkable.SaveInfo saveInfo = new global::BaseNetworkable.SaveInfo
                                {
                                    forConnection = connection,
                                    forDisk = false
                                };
                                write.PacketID(Message.Type.Entities);
                                write.UInt32(connection.validate.entityUpdates);
                                using (saveInfo.msg = Facepunch.Pool.Get<ProtoBuf.Entity>())
                                {
                                    _envSync.Save(saveInfo);
                                    if (nvPlayerData.timeLocked)
                                    {
                                        saveInfo.msg.environment.dateTime = _nvDate.AddHours(nvPlayerData.time).ToBinary();
                                        saveInfo.msg.environment.fog = 0;
                                        saveInfo.msg.environment.rain = 0;
                                        saveInfo.msg.environment.clouds = 0;
										saveInfo.msg.environment.wind = 0;
                                    }
                                    if (saveInfo.msg.baseEntity == null)
                                    {
                                        LogError(this + ": ToStream - no BaseEntity!?");
                                    }
                                    if (saveInfo.msg.baseNetworkable == null)
                                    {
                                        LogError(this + ": ToStream - no baseNetworkable!?");
                                    }
                                    write.Proto(saveInfo.msg);
                                    _envSync.PostSave(saveInfo);
                                    write.Send(new SendInfo(connection));
                                }
                            }
                        }
                    }
                }
            });
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && !_connected.Contains(player.userID))
                _connected.Add(player.userID);
        }

        private void OnPlayerDisconnected(BasePlayer pl, string reason)
        {
            if (pl != null && _playerData.ContainsKey(pl.userID))
                _playerData.Remove(pl.userID);

            if (pl != null && _connected.Contains(pl.userID))
                _connected.Remove(pl.userID);
        }

        private void OnPlayerSleepEnded(BasePlayer pl)
        {
            if (pl == null)
                return;

            if (!_connected.Contains(pl.userID))
                return;

            if (permission.UserHasPermission(pl.UserIDString, PERM_AUTO))
                AdminNightVisionCommand(pl.IPlayer, "nv", new string[] { _playerTimes.ContainsKey(pl.userID) ? _playerTimes[pl.userID].ToString() : "" });

            _connected.Remove(pl.userID);
        }

        // Plugins written for NightVision, such as Clear Night, find it by name
        // ([PluginReference("NightVision")]) to skip time-locked players. After
        // the rename they can't, so they send the real sky to everyone and a
        // locked sky flickers. Point those references at this plugin instead.
        // Runs from the sync timer, so a plugin's own load-time checks (Clear
        // Night unloads itself for NightVision below v1.4.0) have already run.
        private void LinkNightVisionReferences()
        {
            _linkedReferences.RemoveAll(link => !link.Key.IsLoaded);

            if (plugins.Find("NightVision") != null)
                return;

            foreach (Plugin plugin in plugins.GetAll())
            {
                if (plugin == null || plugin == this || !plugin.IsLoaded)
                    continue;

                try
                {
                    foreach (FieldInfo field in GetNightVisionFields(plugin.GetType()))
                    {
                        if (field.GetValue(plugin) != null)
                            continue;

                        field.SetValue(plugin, this);
                        _linkedReferences.Add(new KeyValuePair<Plugin, FieldInfo>(plugin, field));
                        Puts($"{plugin.Title} looks for NightVision; linked it to Admin Night Vision");
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not link {plugin.Title} to Admin Night Vision: {ex.Message}");
                }
            }
        }

        private FieldInfo[] GetNightVisionFields(Type type)
        {
            FieldInfo[] fields;
            if (_nightVisionFields.TryGetValue(type, out fields))
                return fields;

            fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => {
                    var reference = field.GetCustomAttributes(typeof(PluginReferenceAttribute), true).FirstOrDefault() as PluginReferenceAttribute;
                    return reference != null
                        && (string.IsNullOrEmpty(reference.Name) ? field.Name : reference.Name) == "NightVision"
                        && field.FieldType.IsAssignableFrom(GetType());
                })
                .ToArray();
            _nightVisionFields[type] = fields;
            return fields;
        }

        private void Unload()
        {
            foreach (var link in _linkedReferences)
            {
                try
                {
                    if (link.Value.GetValue(link.Key) == this)
                        link.Value.SetValue(link.Key, null);
                }
                catch { }
            }

            if (_envSync != null)
                _envSync.limitNetworking = false;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}\\playerTimes", _playerTimes);
        }

        [Command("adminnightvision", "nv", "unlimitednvg", "unvg")]
        private void AdminNightVisionCommand(IPlayer player, string command, string[] args)
        {
            if (player == null) return;
            BasePlayer pl = (BasePlayer)player.Object;
            if (pl == null) return;

            if (args.Length != 0 && args[0] == "help")
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(lang.GetMessage("HelpTitle", this, pl.UserIDString));
                sb.AppendLine(lang.GetMessage("Help1", this, pl.UserIDString));

                if (permission.UserHasPermission(pl.UserIDString, PERM_UNLIMITEDNVG))
                    sb.AppendLine(lang.GetMessage("Help2", this, pl.UserIDString));

                SendChatMsg(pl, sb.ToString(), "");
                return;
            }

            NVPlayerData nvpd;
            switch(command)
            {
                case "adminnightvision":
                case "nv":
                    if (!permission.UserHasPermission(pl.UserIDString, PERM_ALLOWED))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    nvpd = GetNVPlayerData(pl);
                    nvpd.timeLocked = !nvpd.timeLocked;
                    float time;
                    nvpd.time = args.Length > 0 && float.TryParse(args[0], out time) && time >= 0 && time <= 24 ? time : _config.time;

                    if (permission.UserHasPermission(pl.UserIDString, PERM_AUTO))
                    {
                        _playerTimes[pl.userID] = nvpd.time;
                        SaveData();
                    }

                    SendChatMsg(pl, string.Format(lang.GetMessage(nvpd.timeLocked ? "TimeLocked" : "TimeUnlocked", this, pl.UserIDString), nvpd.time));
                    break;
                case "unlimitednvg":
                case "unvg":
                    if (!permission.UserHasPermission(pl.UserIDString, PERM_UNLIMITEDNVG))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    List<Item> unvgInv = pl.inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item");
                    if (unvgInv.Count > 0)
                    {
                        foreach(Item i in unvgInv)
                        {
                            if (i.condition == 1 && i.amount == 0)
                            {
                                i.SwitchOnOff(false);
                                i.Remove();
                            }
                        }

                        pl.inventory.containerWear.capacity = 7;
                        SendChatMsg(pl, lang.GetMessage("RemoveUNVG", this, pl.UserIDString));
                    }
                    else
                    {
                        var item = ItemManager.CreateByName("nightvisiongoggles", 1, 0UL);
                        if (item != null)
                        {
                            item.OnVirginSpawn();
                            item.SwitchOnOff(true);
                            item.condition = 1;
                            item.amount = 0;
                            pl.inventory.containerWear.capacity = 8;
                            item.MoveToContainer(pl.inventory.containerWear, 7);
                            SendChatMsg(pl, lang.GetMessage("EquipUNVG", this, pl.UserIDString));
                        }
                    }
                    break;
            }
        }

        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            if (item == null || inventory == null) return null;
            if (item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0) return null;

            NextTick(() =>
            {
                if (inventory != null && inventory.containerMain != null)
                {
                    foreach (Item i in inventory.containerMain.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerBelt != null)
                {
                    foreach (Item i in inventory.containerBelt.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerWear != null)
                {
                    foreach (Item i in inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
            });
            return null;
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item != null && item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0)
            {
                item.Remove();
            }
        }

        private NVPlayerData GetNVPlayerData(BasePlayer pl)
        {
            _playerData[pl.userID] = _playerData.ContainsKey(pl.userID) ? _playerData[pl.userID] : new NVPlayerData();
			_playerData[pl.userID].timeLocked = !(!_playerData[pl.userID].timeLocked || !permission.UserHasPermission(pl.UserIDString, PERM_ALLOWED));
            return _playerData[pl.userID];
        }

        #region Plugin-API

        [HookMethod("LockPlayerTime")]
        void LockPlayerTime_PluginAPI(BasePlayer player, float time)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = true;
            data.time = time;
        }

        [HookMethod("UnlockPlayerTime")]
        void UnlockPlayerTime_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = false;
        }

        [HookMethod("IsPlayerTimeLocked")]
        bool IsPlayerTimeLocked_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            return data.timeLocked;
        }

        [HookMethod("BlockEnvUpdates")]
        void BlockEnvUpdates_PluginAPI(bool blockEnv)
        {
            API_blockEnvUpdates = blockEnv;
        }

        #endregion

        #region Config
        private DateTime _defaultDate = new DateTime(2024, 1, 25);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatPrefix"] = "<color=#00ff00>[Admin Night Vision]</color>",
                ["NoPerms"] = "You do not have permission to use this command!",
                ["TimeLocked"] = "Time locked to {0}",
                ["TimeUnlocked"] = "Time unlocked",
                ["HelpTitle"] = "<size=16><color=#00ff00>Admin Night Vision</color> Help</size>\n",
                ["Help1"] = "<color=#00ff00>/adminnightvision <0-24>(/nv)</color> - Toggle time lock night vision with optional time 0-24",
                ["Help2"] = "<color=#00ff00>/unlimitednvg (/unvg)</color> - Equip/remove unlimited night vision goggles",
                ["EquipUNVG"] = "Equipped unlimited night vision goggles",
                ["RemoveUNVG"] = "Removed unlimited night vision goggles"
            }, this);
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }

        private PluginConfig GetDefaultConfig()
        {
            PluginConfig config = new PluginConfig();
            config.date = _defaultDate.ToString("M/d/yyyy");
            config.time = 12;
            return config;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();

            if (_config.time < 0 || _config.time > 24)
                _config.time = 12;

            if (!DateTime.TryParse(_config.date, out _nvDate))
            {
                _nvDate = _defaultDate;
                _config.date = _defaultDate.ToString("M/d/yyyy");

                if (_config.time == 0)
                    _config.time = 12;
            }

            Config.WriteObject(_config, true);
        }

        private class PluginConfig
        {
            public string chatIconID = "0";
            public string date;
            public float time;

        }
        #endregion

        private class NVPlayerData
        {
            public bool timeLocked = false;
            public float time = 12f;
        }
    }
}