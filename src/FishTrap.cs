using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimpleFishingTrap
{
    /// <summary>
    /// The trap itself. Works like a bee hive:
    ///  - The fish count lives on the trap's ZDO (the networked save data), so every player sees the
    ///    same number and it's saved with the world.
    ///  - Only the ZDO owner (the game picks one player per area) advances the timer.
    ///  - Collecting asks the owner to empty the trap, and the owner sends the fish to whoever collected.
    /// </summary>
    public class FishTrap : MonoBehaviour, Hoverable, Interactable
    {
        private const string KeyCount = "sft_count";
        private const string KeyProgress = "sft_progress";
        private const string KeyLastTime = "sft_last";
        private const string RpcExtract = "SFT_Extract";
        private const string RpcGive = "SFT_Give";
        private const float TickSeconds = 2f;
        private const float MaxCatchUpWhenDisabled = 10f;

        private ZNetView _nview;
        private Piece _piece;
        private List<GameObject> _fishVisuals;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();

            // Placement ghosts have no ZDO — they must stay inert.
            if (_nview == null || _nview.GetZDO() == null) return;

            _nview.Register(RpcExtract, RPC_Extract);
            _nview.Register<int>(RpcGive, RPC_Give);

            // Optional: children of the model named "FishVisual..." appear as the trap fills up.
            _fishVisuals = GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("FishVisual", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.gameObject)
                .OrderBy(g => g.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            InvokeRepeating(nameof(Tick), UnityEngine.Random.Range(0.5f, TickSeconds), TickSeconds);
        }

        private bool Valid => _nview != null && _nview.IsValid();
        private int Count => Valid ? _nview.GetZDO().GetInt(KeyCount, 0) : 0;
        private float Progress => Valid ? _nview.GetZDO().GetFloat(KeyProgress, 0f) : 0f;

        // ------------------------------------------------------------ catching

        private void Tick()
        {
            if (!Valid) return;
            if (_nview.IsOwner()) Advance();
            UpdateFishVisuals();
        }

        private void Advance()
        {
            var zdo = _nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            long last = zdo.GetLong(KeyLastTime, 0L);
            zdo.Set(KeyLastTime, now);
            if (last == 0L) return; // just built

            float elapsed = (float)TimeSpan.FromTicks(Math.Max(0L, now - last)).TotalSeconds;
            if (!ConfigSync.CatchUpWhileAway) elapsed = Math.Min(elapsed, MaxCatchUpWhenDisabled);

            int max = ConfigSync.MaxFish;
            int count = zdo.GetInt(KeyCount, 0);
            if (count >= max)
            {
                // Full: no progress until emptied.
                if (zdo.GetFloat(KeyProgress, 0f) != 0f) zdo.Set(KeyProgress, 0f);
                return;
            }

            float secPer = ConfigSync.SecondsPerFish;
            float progress = zdo.GetFloat(KeyProgress, 0f) + elapsed;
            int caught = (int)(progress / secPer);
            if (caught > 0)
            {
                count = Math.Min(max, count + caught);
                progress -= caught * secPer;
                if (count >= max) progress = 0f;
                zdo.Set(KeyCount, count);
            }
            zdo.Set(KeyProgress, progress);
        }

        private void UpdateFishVisuals()
        {
            if (_fishVisuals == null || _fishVisuals.Count == 0) return;
            int max = ConfigSync.MaxFish;
            int show = Count <= 0 ? 0 : Mathf.CeilToInt(_fishVisuals.Count * Mathf.Clamp01(Count / (float)max));
            for (int i = 0; i < _fishVisuals.Count; i++)
                if (_fishVisuals[i] != null && _fishVisuals[i].activeSelf != (i < show))
                    _fishVisuals[i].SetActive(i < show);
        }

        // ------------------------------------------------------------ hover / interact

        public string GetHoverName() => _piece != null ? _piece.m_name : "Simple Fishing Trap";

        // Added to Hoverable in Valheim 1.0. No offset: hover text appears at the normal spot.
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (!Valid) return "";

            int count = Count;
            int max = ConfigSync.MaxFish;
            string text = $"{GetHoverName()}\nRaw Fish: {count}/{max}";

            if (count >= max)
            {
                text += "\n<color=orange>Full — empty it to keep catching</color>";
            }
            else
            {
                float remaining = Mathf.Max(0f, ConfigSync.SecondsPerFish - Progress);
                var ts = TimeSpan.FromSeconds(remaining);
                text += $"\n<color=#a0a0a0>Next fish in ~{(int)ts.TotalMinutes}:{ts.Seconds:00}</color>";
            }

            if (count > 0)
                text += "\n[<color=yellow><b>$KEY_Use</b></color>] Collect";

            return Loc.Localize(text);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !Valid) return false;
            if (!PrivateArea.CheckAccess(transform.position)) return true;

            if (Count <= 0)
            {
                user.Message(MessageHud.MessageType.Center, "The trap is empty");
                return true;
            }

            _nview.InvokeRPC(RpcExtract);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // Runs on the owner: empty the trap and send the fish to whoever asked.
        private void RPC_Extract(long sender)
        {
            if (!Valid || !_nview.IsOwner()) return;

            var zdo = _nview.GetZDO();
            int count = zdo.GetInt(KeyCount, 0);
            if (count <= 0) return;

            zdo.Set(KeyCount, 0);
            zdo.Set(KeyProgress, 0f);
            zdo.Set(KeyLastTime, ZNet.instance.GetTime().Ticks); // time spent full doesn't count
            UpdateFishVisuals();

            _nview.InvokeRPC(sender, RpcGive, count);
        }

        // Runs on the collecting player's machine.
        private void RPC_Give(long sender, int count)
        {
            var player = Player.m_localPlayer;
            if (player == null || count <= 0) return;

            var prefab = TrapPrefab.Find(ConfigSync.ItemPrefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Plugin.Log.LogError($"Item prefab '{ConfigSync.ItemPrefab}' not found — {count} fish lost.");
                return;
            }

            var inv = player.GetInventory();
            int stackMax = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int left = count;
            bool dropped = false;

            while (left > 0)
            {
                int n = Math.Min(left, stackMax);
                var item = drop.m_itemData.Clone();
                item.m_stack = n;
                item.m_dropPrefab = prefab;

                if (inv.CanAddItem(item))
                {
                    inv.AddItem(item);
                }
                else
                {
                    var pos = player.transform.position + player.transform.forward * 0.5f + Vector3.up;
                    ItemDrop.DropItem(item, n, pos, Quaternion.identity);
                    dropped = true;
                }
                left -= n;
            }

            string name = Loc.Localize(drop.m_itemData.m_shared.m_name);
            player.Message(MessageHud.MessageType.TopLeft, $"Collected {name} x{count}", 0, drop.m_itemData.GetIcon());
            if (dropped) player.Message(MessageHud.MessageType.Center, "Inventory full — some fish dropped at your feet");
        }
    }
}
