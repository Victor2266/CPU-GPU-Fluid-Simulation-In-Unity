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
    public Vector2 initVelo; //8 bytes
    public float initStrength; //4 bytes
    public int fluidType; //4 bytes
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 28)]
public struct SourceObject //28 bytes total: This is for sending source object info to the GPU
{
    public Vector2 pos; //8 bytes
    public float radius; //4 bytes

    public Vector2 initVelo; //8 bytes
    public float strength; //4 bytes
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