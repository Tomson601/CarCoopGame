using System.Collections.Generic;
using UnityEngine;

// Struktura pliku zapisu (JSON).
[System.Serializable]
public class SaveFile
{
    public int version = 1;
    public int seed;
    public Vector3 spawnPoint;
    public List<int> lootedPois = new List<int>();
    public List<ObjectSave> objects = new List<ObjectSave>();
}

// Jeden zapisany obiekt. Pola -1 oznaczają "ten obiekt tego nie ma".
[System.Serializable]
public class ObjectSave
{
    public string saveId;
    public string prefabId;
    public Vector3 position;
    public Quaternion rotation;

    public bool hasLiquid;
    public FuelMix liquid;

    public float engineHealth = -1f;
    public float engineTemp = -1f;
    public float oilTemp = -1f;
    public float batteryCharge = -1f;
    public float tireHealth = -1f;

    public bool hasCarState;
    public int gear;
    public bool handbrake = true;

    // Dla obiektów ze slotami (auto, felga): saveId części w każdym slocie, "" = pusty
    public List<string> mounted = new List<string>();
}