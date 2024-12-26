using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine.UIElements;
using Unity.Jobs;
using Unity.Collections;

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
[StructLayout(LayoutKind.Sequential, Size = 24)]
public struct OrientedBox //24 bytes total
{
    public Vector2 pos; //8 bytes
    public Vector2 size;
    public Vector2 zLocal;
};

public float DensityKernel(float dst, float radius)
{
	return fluidMaths2DCPUAoS.SpikyKernelPow2(dst, radius);
}
public float NearDensityKernel(float dst, float radius)
{
	return fluidMaths2DCPUAoS.SpikyKernelPow3(dst, radius);
}
public float DensityDerivative(float dst, float radius)
{
	return fluidMaths2DCPUAoS.DerivativeSpikyPow2(dst, radius);
}
public float NearDensityDerivative(float dst, float radius)
{
	return fluidMaths2DCPUAoS.DerivativeSpikyPow3(dst, radius);
}
public float ViscosityKernel(float dst, float radius)
{
	return fluidMaths2DCPUAoS.SmoothingKernelPoly6(dst, smoothingRadius);
}

public struct DensityCalcCPU : IJob
{
    public NativeArray<uint> index;

    public void Execute()
    {
        int2 originCell = SpatialHashCPU.GetCell2D(CPUParticleKernel.particleData[index].position, CPUParticleKernel.smoothingRadius);
        float sqrRadius = CPUParticleKernel.smoothingRadius * CPUParticleKernel.smoothingRadius;
        float density = 0;
        float nearDensity = 0;

        //neighbour search
        for (int i = 0; i < 9; i++)
        {
            uint hash = SpatialHashCPU.HashCell2D(originCell + SpatialHashCPU.offsets2D[i]);
            uint key = SpatialHashCPU.KeyFromHash(hash, CPUParticleKernel.numParticles);
            uint currIndex = CPUParticleKernel.spatialOffsetsCPU[key];

            while (currIndex != CPUParticleKernel.numParticles){
                uint3 indexData = CPUParticleKernel.spatialIndicesCPU[currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                uint neighbourIndex = indexData[0];
                float2 neighbourPos = CPUParticleKernel.particleData[neighbourIndex].predictedPosition;
                float2 offsetToNeighbour = neighbourPos - CPUParticleKernel.particleData[index].position;
                float sqrDstToNeighbour = FluidMaths2DCPUAoS.Dot(offsetToNeighbour, offsetToNeighbour);
                // Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		// Calculate density and near density
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		density += DensityKernel(dst, smoothingRadius);
	    		nearDensity += NearDensityKernel(dst, smoothingRadius);
            }
        }

        CPUParticleKernel.particleData[index].density = new float2(density, nearDensity); // may want to move this out of the job
    }

}

public float PressureFromDensity(float density)
{
    return (density - targetDensity) * pressureMultiplier;
}
public float NearPressureFromDensity(float nearDensity)
{
    return nearPressureMultiplier * nearDensity;
}

public struct PressureCalcCPU : IJob
{
    public NativeArray<uint> index;
    float density = CPUParticleKernel.particleData[index].density[0];
    float density = CPUParticleKernel.particleData[index].density[1];
    float pressure = PressureFromDensity(density);
    floar nearPressure = NearPressureFromDensity(nearDensity);
    float2 pressureForce = 0;

    float2 pos = CPUParticleKernel.particleData[index].predictedPosition;
    int2 originCell = SpatialHashCPU.GetCell2D(CPUParticleKernel.particleData[index].position, CPUParticleKernel.smoothingRadius);
    float sqrRadius = CPUParticleKernel.smoothingRadius * CPUParticleKernel.smoothingRadius;
    //neighbour search
    for (int i = 0; i < 9; i++)
    {
        uint hash = SpatialHashCPU.HashCell2D(originCell + SpatialHashCPU.offsets2D[i]);
        uint key = SpatialHashCPU.KeyFromHash(hash, CPUParticleKernel.numParticles);
        uint currIndex = CPUParticleKernel.spatialOffsetsCPU[key];
        while (currIndex != CPUParticleKernel.numParticles){
            uint3 indexData = CPUParticleKernel.spatialIndicesCPU[currIndex];
            currIndex++;
            // Exit if no longer looking at correct bin
            if (indexData[2] != key) break;
            // Skip if hash does not match
            if (indexData[1] != hash) continue;
            uint neighbourIndex = indexData[0];
            float2 neighbourPos = CPUParticleKernel.particleData[neighbourIndex].predictedPosition;
            float2 offsetToNeighbour = neighbourPos - CPUParticleKernel.particleData[index].position;
            float sqrDstToNeighbour = FluidMaths2DCPUAoS.Dot(offsetToNeighbour, offsetToNeighbour);
            // Skip if not within radius
    		if (sqrDstToNeighbour > sqrRadius) continue;
            // Calculate pressure force
			float dst = (float)Math.Sqrt(sqrDstToNeighbour);
			float2 dirToNeighbour = dst > 0 ? offsetToNeighbour / dst : new float2(0, 1);
            float neighbourDensity = CPUParticleKernel.particleData[neighbourIndex].density[0];
            float neighbourNearDensity = CPUParticleKernel.particleData[neighbourIndex].density[1];
            float neighbourPressure = PressureFromDensity(neighbourDensity);
            float neighbourNearPressure = PressureFromDensity(neighbourNearDensity);
            float sharedPressure = (pressure + neighbourPressure) / 0.5;
            float sharedNearPressure = (nearPressure + neighbourNearPressure) / 0.5;
            pressureForce += dirToNeighbour * DensityDerivative(DensityCalcCPU, smoothingRadius) * sharedPressure / neighbourDensity;
            pressureForce += dirToNeighbour * NearDensityDerivative(DensityCalcCPU, smoothingRadius) * sharedNearPressure / neighbourNearDensity;
        }
    }
    
    float 2 acceleration = pressureForce / density;
    CPUParticleKernel.particleData[index].velocity += acceleration * CPUParticleKernel.deltaTime;
}

public struct CPUParticleKernel : MonoBehaviour
{
    public uint numCPUCells;
    public uint numParticles;
    public uint numBoxColliders;
    public uint numCircleColliders;
    public uint[] CPUParticleIndices;

    public uint MAX_COLLIDERS;
    public Particle[] particleData;
    public OrientedBox[] boxCollidersBuffer;
    public Circle[] circleCollidersBuffer;
    public uint3[] spatialIndicesCPU;
    public uint[] spatialOffsetsCPU;

    public float gravity;
    public float deltaTime;
    public float collisionDamping;
    public float smoothingRadius;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;
    public float2 boundsSize;
    public float2 interactionInputPoint;
    public float interactionInputStrength;
    public float interactionInputRadius;

}
