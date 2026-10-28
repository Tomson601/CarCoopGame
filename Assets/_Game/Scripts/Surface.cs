using UnityEngine;

public enum SurfaceType
{
    Asphalt,
    Dirt,
    Grass
}

// Dodaj do obiektów terenu (droga, trawa). Teren bez tego komponentu = ziemia.
public class Surface : MonoBehaviour
{
    public SurfaceType type = SurfaceType.Asphalt;
}