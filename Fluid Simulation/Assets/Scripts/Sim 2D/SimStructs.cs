using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

//Defining Structs
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 40)]
public struct Particle // 40 bytes total
{
    public float2 density; //8 bytes, density and near density
    public Vector2 velocity; //8 bytes
    public Vector2 predictedPosition; // 8
    public Vector2 position; // 8
    public float temperature; // 4
    public FluidType type; // 4 (enum is int by default)
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct Circle //12 bytes total
{
    public Vector2 pos; //8 bytes
    public float radius; //4 bytes
}

[System.Serializable]
public struct SourceObjectInitializer //40 bytes total: This is for setting up source objects in the inspector
{
    public Transform transform; //24 bytes
    public Vector2 velo; //8 bytes
    [Range(0, 1)]
    // Each particle will pick a random source object to spawn from and then it will generate a number between 0 and 1 to compare to this spawnRate to see if it should spawn
    public float spawnRate; //4 bytes: This is the percent chance that the particle spawns, 1 means 100% given that the particle has picked this source
    public int fluidType; //4 bytes: Setting this to 0 will disable this source
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 28)]
public struct SourceObject //28 bytes total: This is for sending source object info to the GPU
{
    public Vector2 pos; //8 bytes
    public float radius; //4 bytes

    public Vector2 velo; //8 bytes
    public float spawnRate; //4 bytes
    public int fluidType; //4 bytes
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 24)]
public struct OrientedBox //24 bytes total
{
    public Vector2 pos; //8 bytes
    public Vector2 size;
    public Vector2 zLocal;
};