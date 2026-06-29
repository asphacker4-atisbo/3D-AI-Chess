using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Static utility that manages up to <see cref="SlotCount"/> encrypted save slots on disk.
/// Slot numbers are 1-based (1 … <see cref="SlotCount"/>).
/// Each slot is stored as an AES-encrypted JSON file alongside the other game saves.
///
/// Auto-save flow:
///   Call <see cref="GetOrCreateSessionSlot"/> once per game session to claim a slot,
///   then pass its return value to <see cref="SaveToSlot"/> on every move.
///   Call <see cref="ClearSessionSlot"/> when starting a brand-new game so the next
///   session gets a fresh slot assignment.
///   Call <see cref="SetSessionSlot"/> when resuming a saved game so subsequent
///   auto-saves overwrite the same slot that was loaded.
/// </summary>
public static class SaveSlotManager
{
    public const int SlotCount = 5;

    // ── session slot tracking ─────────────────────────────────────────────────

    private static int _sessionSlot = -1;

    /// <summary>
    /// The slot currently assigned to this game session, or -1 if none has been
    /// claimed yet (e.g. the game ended before the first auto-save).
    /// </summary>
    public static int CurrentSessionSlot => _sessionSlot;

    /// <summary>
    /// Returns the slot assigned to the current game session, creating one if needed.
    /// Prefers the first empty slot; falls back to the slot with the oldest save when
    /// all five are occupied.
    /// </summary>
    public static int GetOrCreateSessionSlot()
    {
        if (_sessionSlot >= 1 && _sessionSlot <= SlotCount)
            return _sessionSlot;

        _sessionSlot = FindNextAutoSlot();
        Debug.Log($"[SaveSlot] Auto-selected slot {_sessionSlot} for this session.");
        return _sessionSlot;
    }

    /// <summary>
    /// Pins the session to <paramref name="slot"/> so subsequent auto-saves
    /// overwrite the slot that was just loaded.
    /// </summary>
    public static void SetSessionSlot(int slot)
    {
        Validate(slot);
        _sessionSlot = slot;
    }

    /// <summary>
    /// Clears the session slot so the next call to <see cref="GetOrCreateSessionSlot"/>
    /// picks a fresh slot. Call this when the player starts a brand-new game.
    /// </summary>
    public static void ClearSessionSlot() => _sessionSlot = -1;

    // ── disk I/O ──────────────────────────────────────────────────────────────

    private static string GetPath(int slot) =>
        Path.Combine(Application.persistentDataPath, $"slot_{slot}.json");

    /// <summary>
    /// Writes <paramref name="data"/> to the given slot, stamping
    /// <see cref="SaveSettings.saveDateTime"/>, <see cref="SaveSettings.moveCount"/>,
    /// and <see cref="SaveSettings.saveTimestamp"/> with current values before serialising.
    /// </summary>
    public static void SaveToSlot(int slot, SaveSettings data)
    {
        Validate(slot);
        data.saveDateTime  = DateTime.Now.ToString("MMM dd, yyyy · HH:mm");
        data.moveCount     = data.moveHistory?.Count ?? 0;
        data.saveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string json = JsonUtility.ToJson(data, true);
        EncryptionTool.SaveEncrypted(json, GetPath(slot));
        Debug.Log($"[SaveSlot] Game saved to slot {slot}.");
    }

    /// <returns>
    /// The fully deserialised <see cref="SaveSettings"/> for the slot,
    /// or <c>null</c> when the slot is empty.
    /// </returns>
    public static SaveSettings LoadFromSlot(int slot)
    {
        Validate(slot);
        string path = GetPath(slot);
        if (!File.Exists(path)) return null;
        string json = EncryptionTool.LoadDecrypted(path);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonUtility.FromJson<SaveSettings>(json);
    }

    /// <returns><c>true</c> when the slot file exists on disk.</returns>
    public static bool SlotExists(int slot)
    {
        Validate(slot);
        return File.Exists(GetPath(slot));
    }

    /// <summary>Permanently deletes the save file for the given slot.</summary>
    public static void DeleteSlot(int slot)
    {
        Validate(slot);
        string path = GetPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveSlot] Slot {slot} deleted.");
        }

        // If this was the active session slot, release it
        if (_sessionSlot == slot)
            _sessionSlot = -1;
    }

    // ── internal helpers ──────────────────────────────────────────────────────

    private static int FindNextAutoSlot()
    {
        // Prefer an empty slot
        for (int i = 1; i <= SlotCount; i++)
            if (!SlotExists(i)) return i;

        // All slots occupied — evict the oldest one
        int  oldest   = 1;
        long oldestTs = long.MaxValue;
        for (int i = 1; i <= SlotCount; i++)
        {
            SaveSettings d = LoadFromSlot(i);
            if (d != null && d.saveTimestamp < oldestTs)
            {
                oldestTs = d.saveTimestamp;
                oldest   = i;
            }
        }
        return oldest;
    }

    private static void Validate(int slot)
    {
        if (slot < 1 || slot > SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot),
                $"Slot must be between 1 and {SlotCount}.");
    }
}
