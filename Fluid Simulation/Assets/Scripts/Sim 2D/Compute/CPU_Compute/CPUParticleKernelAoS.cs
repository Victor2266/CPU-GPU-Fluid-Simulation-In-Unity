using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine.UIElements;
using Unity.Jobs;
using Unity.Collections;
using UnityEditor;
using System.Threading;
using JetBrains.Annotations;
using Unity.VisualScripting;
using System.Collections.Generic;
using Unity.Burst;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using DG.Tweening.Plugins;
using UnityEngine.ParticleSystemJobs;

[BurstCompile]
public struct CPUDensityCalcAoS : IJobParallelFor
{
    [WriteOnly]
    public NativeArray<Particle> densityOut;
    [ReadOnly]
    public NativeArray<uint> keyarr;
    [ReadOnly]
    public uint numCPUKeys;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public uint numParticles;
    [ReadOnly]
    public NativeArray<FluidParam> fluidPs;
    [ReadOnly]
    public NativeArray<ScalingFactors> scalingFacts;
    
    [ReadOnly]
    public float maxSmoothingRadius;
    [ReadOnly]
    public NativeArray<int2> offsets2D;

    // Constants used for hashing
    const uint hashK1 = 15823;
    const uint hashK2 = 9737333;
    
    // Convert floating point position into an integer cell coordinate
    public int2 GetCell2D(float2 position, float radius)
    {
    	int2 temp = new int2(0,0);
        temp[0] = (int)Math.Floor(position[0] / radius);
        temp[1] = (int)Math.Floor(position[1] / radius);
        return temp;
    }
    
    // Hash cell coordinate to a single unsigned integer
    public uint HashCell2D(int2 cell)
    {
    	cell = (int2)(uint2)cell;
    	uint a = (uint)(cell.x * hashK1);
    	uint b = (uint)(cell.y * hashK2);
    	return (a + b);
    }
    
    public uint KeyFromHash(uint hash, uint tableSize)
    {
    	return hash % tableSize;
    }

    public float SpikyKernelPow3(float dst, float radius, float val)
    {
    	if (dst < radius)
    	{
    		float v = radius - dst;
    		return v * v * v * val;
    	}
    	return 0;
    }

    public float SpikyKernelPow2(float dst, float radius, float val)
    {
    	if (dst < radius)
    	{
    		float v = radius - dst;
    		return v * v * val;
    	}
    	return 0;
    }

    public float Dot(float2 A, float2 B){

		return (A.x * B.x) + (A.y * B.y);
	}

    float GetMaxSmoothingRadius(int typeA, int typeB)
    {
        return Math.Max(fluidPs[typeA - 1].smoothingRadius, fluidPs[typeB - 1].smoothingRadius);
    }
    public void Execute(int index)
    {
        
        
        Particle particleOut = particles[index];
        if(particleOut.type == FluidType.Disabled){
            return;
        }
        float2 pos = particles[index].predictedPosition;
        int2 originCell = GetCell2D(pos, maxSmoothingRadius);
        uint originHash = HashCell2D(originCell);
        uint originKey = KeyFromHash(originHash, numParticles);
        int j=0;
        for(j=0; j<numCPUKeys; j++){
            if(originKey == keyarr[j]){
                break;
            }
        }
        if(j == numCPUKeys){
            return;
        }
        FluidParam fparams = fluidPs[((int) particleOut.type) - 1];
        ScalingFactors sFactors = scalingFacts[((int) particleOut.type) - 1];

        
        float sqrRadius = fparams.smoothingRadius * fparams.smoothingRadius;
        float density = 0;
        float nearDensity = 0;

        //neighbour search
        for (int i = 0; i < 9; i++)
        {
            uint hash = HashCell2D(originCell + offsets2D[i]);
            uint key = KeyFromHash(hash, numParticles);
            uint currIndex =spatialOffsets[(int) key];

            while (currIndex != numParticles){
                uint3 indexData = spatialIndices[(int) currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                uint neighbourIndex = indexData[0];
                Particle neighbourParticle = particles[(int) neighbourIndex];
                if (neighbourParticle.type == FluidType.Disabled){
                    continue;
                }
                FluidParam neighbourData = fluidPs[((int) neighbourParticle.type) - 1];
                float2 neighbourPos = neighbourParticle.predictedPosition;
                float2 offsetToNeighbour;
                offsetToNeighbour.x = neighbourPos.x - pos.x;
                offsetToNeighbour.y = neighbourPos.y - pos.y;
                float sqrDstToNeighbour = Dot(offsetToNeighbour, offsetToNeighbour);
                float interactionRadius = GetMaxSmoothingRadius((int) particleOut.type, (int) neighbourParticle.type);
                float interactionSquare = interactionRadius * interactionRadius;
                // Skip if not within radius
	    		if (sqrDstToNeighbour > interactionSquare) continue;

	    		// Calculate density and near density
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		density += SpikyKernelPow2(dst, fparams.smoothingRadius, sFactors.SpikyPow2);
	    		nearDensity += SpikyKernelPow3(dst, fparams.smoothingRadius, sFactors.SpikyPow3);
            }
        }

        particleOut.density = new float2(density, nearDensity);
        densityOut[index] = particleOut;
    }

}

[BurstCompile]
public struct CPUPressureCalcAoS : IJobParallelFor
{

    [WriteOnly]
    public NativeArray<Particle> pressureOut;
    [ReadOnly]
    public NativeArray<uint> keyarr;
    [ReadOnly]
    public uint numCPUKeys;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public uint numParticles;
    [ReadOnly]
    public float deltaTime;
    [ReadOnly]
    public NativeArray<FluidParam> fluidPs;
    [ReadOnly]
    public NativeArray<ScalingFactors> scalingFacts;
    
    [ReadOnly]
    public float maxSmoothingRadius;
    [ReadOnly]
    public NativeArray<int2> offsets2D;
    
    // Constants used for hashing
    const uint hashK1 = 15823;
    const uint hashK2 = 9737333;
    
    // Convert floating point position into an integer cell coordinate
    public int2 GetCell2D(float2 position, float radius)
    {
    	int2 temp = new int2(0,0);
        temp[0] = (int)Math.Floor(position[0] / radius);
        temp[1] = (int)Math.Floor(position[1] / radius);
        return temp;
    }
    
    // Hash cell coordinate to a single unsigned integer
    public uint HashCell2D(int2 cell)
    {
    	cell = (int2)(uint2)cell;
    	uint a = (uint)(cell.x * hashK1);
    	uint b = (uint)(cell.y * hashK2);
    	return (a + b);
    }
    
    public uint KeyFromHash(uint hash, uint tableSize)
    {
    	return hash % tableSize;
    }

    public float DerivativeSpikyPow3(float dst, float radius, float val)
    {
    	if (dst <= radius)
    	{
    		float v = radius - dst;
    		return -v * v * val;
    	}
    	return 0;
    }

    public float DerivativeSpikyPow2(float dst, float radius, float val)
    {
    	if (dst <= radius)
    	{
    		float v = radius - dst;
    		return -v * val;
    	}
    	return 0;
    }

    float PressureFromDensity(float density, float targetDensity, float pressureMultiplier)
    {
	    return (density - targetDensity) * pressureMultiplier;
    }

    float NearPressureFromDensity(float nearDensity, float nearPressureMultiplier)
    {
	    return nearPressureMultiplier * nearDensity;
    }
    public float Dot(float2 A, float2 B){

		return (A.x * B.x) + (A.y * B.y);
	}
    public void Execute(int index)
    {
        Particle particleOut = particles[index];
        if(particleOut.type == FluidType.Disabled){
            return;
        }
        float2 pos = particles[index].predictedPosition;
        int2 originCell = GetCell2D(pos, maxSmoothingRadius);
        uint originHash = HashCell2D(originCell);
        uint originKey = KeyFromHash(originHash, numParticles);
        int j=0;
        for(j=0; j<numCPUKeys; j++){
            if(originKey == keyarr[j]){
                break;
            }
        }
        if(j == numCPUKeys){
            return;
        }
        FluidParam fParams = fluidPs[((int) particleOut.type) - 1];
        ScalingFactors sFactors = scalingFacts[((int) particleOut.type) - 1];
        float density = particles[index].density[0];
	    float densityNear = particles[index].density[1];
	    float pressure = PressureFromDensity(density, fParams.targetDensity, fParams.pressureMultiplier);
	    float nearPressure = NearPressureFromDensity(densityNear, fParams.nearPressureMultiplier);
	    float2 pressureForce = 0;
    
	    //float2 pos = particles[index].predictedPosition;
	    //int2 originCell = GetCell2D(pos, maxSmoothingRadius);
	    float sqrRadius = fParams.smoothingRadius * fParams.smoothingRadius;

	    // Neighbour search
	    for (int i = 0; i < 9; i ++)
	    {
	    	uint hash = HashCell2D(originCell + offsets2D[i]);
	    	uint key = KeyFromHash(hash, numParticles);
	    	uint currIndex = spatialOffsets[(int) key];

	    	while (currIndex < numParticles)
	    	{
	    		uint3 indexData = spatialIndices[(int) currIndex];
	    		currIndex++;
	    		// Exit if no longer looking at correct bin
	    		if (indexData[2] != key) break;
	    		// Skip if hash does not match
	    		if (indexData[1] != hash) continue;

	    		int neighbourIndex = (int) indexData[0];
	    		// Skip if looking at self
	    		if (neighbourIndex == index) continue;

                Particle neighbourParticle = particles[neighbourIndex];
                if(neighbourParticle.type == FluidType.Disabled){
                    continue;
                }
                FluidParam neighbourParams = fluidPs[((int)neighbourParticle.type) - 1];
	    		float2 neighbourPos = particles[neighbourIndex].predictedPosition;
	    		float2 offsetToNeighbour = neighbourPos - pos;
	    		float sqrDstToNeighbour = Dot(offsetToNeighbour, offsetToNeighbour);

	    		// Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		// Calculate pressure force
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		float2 dirToNeighbour = dst > 0 ? (offsetToNeighbour / dst) : new float2(0, 1);

	    		float neighbourDensity = particles[neighbourIndex].density[0];
	    		float neighbourNearDensity = particles[neighbourIndex].density[1];
	    		float neighbourPressure = PressureFromDensity(neighbourDensity, neighbourParams.targetDensity, neighbourParams.pressureMultiplier);
	    		float neighbourNearPressure = NearPressureFromDensity(neighbourNearDensity, neighbourParams.nearPressureMultiplier);

	    		float sharedPressure = (pressure + neighbourPressure) * 0.5F;
	    		float sharedNearPressure = (nearPressure + neighbourNearPressure) * 0.5F;

	    		pressureForce += dirToNeighbour * (DerivativeSpikyPow2(dst, fParams.smoothingRadius, sFactors.SpikyPow2Derivative) * sharedPressure / neighbourDensity);
	    		pressureForce += dirToNeighbour * (DerivativeSpikyPow3(dst, fParams.smoothingRadius, sFactors.SpikyPow3Derivative) * sharedNearPressure / neighbourNearDensity);
	    	}
	    }

	    float2 acceleration = pressureForce / density;
	    particleOut.velocity.x += acceleration.x * deltaTime;
        particleOut.velocity.y += acceleration.y * deltaTime;
        pressureOut[index] = particleOut;
    }

}
//see pressure and density
[BurstCompile]
public struct CPUViscosityCalcAoS : IJobParallelFor
{
    [WriteOnly]
    public NativeArray<Particle> viscosityOut;
    [ReadOnly]
    public NativeArray<uint> keyarr;
    [ReadOnly]
    public uint numCPUKeys;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public uint numParticles;
    [ReadOnly]
    public float deltaTime;
    [ReadOnly]
    public NativeArray<FluidParam> fluidPs;
    [ReadOnly]
    public NativeArray<ScalingFactors> scalingFacts;
    
    [ReadOnly]
    public float maxSmoothingRadius;
    [ReadOnly]
    public NativeArray<int2> offsets2D;
    
    // Constants used for hashing
    const uint hashK1 = 15823;
    const uint hashK2 = 9737333;
    
    // Convert floating point position into an integer cell coordinate
    public int2 GetCell2D(float2 position, float radius)
    {
    	int2 temp = new int2(0,0);
        temp[0] = (int)Math.Floor(position[0] / radius);
        temp[1] = (int)Math.Floor(position[1] / radius);
        return temp;
    }
    
    // Hash cell coordinate to a single unsigned integer
    public uint HashCell2D(int2 cell)
    {
    	cell = (int2)(uint2)cell;
    	uint a = (uint)(cell.x * hashK1);
    	uint b = (uint)(cell.y * hashK2);
    	return (a + b);
    }
    
    public uint KeyFromHash(uint hash, uint tableSize)
    {
    	return hash % tableSize;
    }

    public float SmoothingKernelPoly6(float dst, float radius, float val)
    {
    	if (dst < radius)
    	{
    		float v = radius * radius - dst * dst;
    		return v * v * v * val;
    	}
    	return 0;
    }

    public float Dot(float2 A, float2 B){

		return (A.x * B.x) + (A.y * B.y);
	}
    public void Execute(int index)
    {
        Particle particleOut = particles[index];
        if(particleOut.type == FluidType.Disabled){
            return;
        }
        float2 pos = particles[index].predictedPosition;
        int2 originCell = GetCell2D(pos, maxSmoothingRadius);
        uint originHash = HashCell2D(originCell);
        uint originKey = KeyFromHash(originHash, numParticles);
        int j=0;
        for(j=0; j<numCPUKeys; j++){
            if(originKey == keyarr[j]){
                break;
            }
        }
        if(j == numCPUKeys){
            return;
        }
        FluidParam fParams = fluidPs[((int) particleOut.type) - 1];
        ScalingFactors sFactors = scalingFacts[((int) particleOut.type) - 1];
        //float2 pos = particleOut.predictedPosition;
	    //int2 originCell = GetCell2D(pos, maxSmoothingRadius);
	    float sqrRadius = fParams.smoothingRadius * fParams.smoothingRadius;

	    float2 viscosityForce = 0;
	    float2 velocity = particleOut.velocity;

	    for (int i = 0; i < 9; i ++)
	    {
	    	uint hash = HashCell2D(originCell + offsets2D[i]);
	    	int key = (int) KeyFromHash(hash, numParticles);
	    	int currIndex = (int) spatialOffsets[key];

	    	while (currIndex < numParticles)
	    	{
	    		uint3 indexData = spatialIndices[currIndex];
	    		currIndex ++;
	    		// Exit if no longer looking at correct bin
	    		if (indexData[2] != key) break;
	    		// Skip if hash does not match
	    		if (indexData[1] != hash) continue;

	    		int neighbourIndex = (int) indexData[0];
	    		// Skip if looking at self
	    		if (neighbourIndex == index) continue;

                Particle neighbourParticle = particles[neighbourIndex];
                if(neighbourParticle.type == FluidType.Disabled){
                    continue;
                }
                FluidParam neighbourParams = fluidPs[((int) neighbourParticle.type) - 1];
	    		float2 neighbourPos = particles[neighbourIndex].predictedPosition;
	    		float2 offsetToNeighbour = neighbourPos - pos;
	    		float sqrDstToNeighbour = Dot(offsetToNeighbour, offsetToNeighbour);

	    		// Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		float dst = (float) Math.Sqrt(sqrDstToNeighbour);
	    		float2 neighbourVelocity = particles[neighbourIndex].velocity;
	    		viscosityForce += (neighbourVelocity - velocity) * SmoothingKernelPoly6(dst, fParams.smoothingRadius, sFactors.Poly6);
	    	}

	    }
	    particleOut.velocity.x += viscosityForce.x * fParams.viscosityStrength * deltaTime;
        particleOut.velocity.y += viscosityForce.y * fParams.viscosityStrength * deltaTime;
        viscosityOut[index] = particleOut;
    }
}

// public class CPUParticleKernel : MonoBehaviour
// {
//     public int numParticles;
//     public float maxSmoothingRadius;
//     public float deltaTime;
//     public int2[] offsets;
//     public FluidParam[] fluidParams;
//     public ScalingFactors[] scalingFactors;
//     public OrientedBox[] boxCollidersData;
//     public Circle[] circleCollidersData;
//     public OrientedBox[] drainData;
//     public Circle[] sourceData;
//     public uint3[] spatialIndices;
//     public uint[] spatialOffsets;
//     public uint[] CPUCellHashs;

//     public float2[] positions;
//     public float2[] predictedPositions;
//     public float2[] densities;
//     public float2[] velocities;
//     public NativeArray<FluidParam> fluidParamBuffer;
//     public NativeArray<ScalingFactors> scalingFactorsBuffer;
//     public NativeArray<OrientedBox> boxBuffer;
//     public NativeArray<Circle> circleBuffer;
//     public NativeArray<Circle> sourceBuffer;
//     public NativeArray<OrientedBox> drainBuffer;
//     public NativeArray<uint3> spatialIndicesBuffer;
//     public NativeArray<uint> spatialOffsetsBuffer;
//     public NativeArray<float2> positionBuffer;
//     public NativeArray<float2> predictedPositionBuffer;
//     public NativeArray<float2> densitiesBuffer;
//     public NativeArray<float2> velocitiesBuffer;
//     public NativeArray<int2> offsets2DBuffer;
    
// }

public class CPUParticleKernelAoS
{
    public int numParticles;
    public float maxSmoothingRadius;
    public float deltaTime;
    public int2[] offsets;
    public FluidParam[] fluidParams;
    public ScalingFactors[] scalingFactors;
    public OrientedBox[] boxCollidersData;
    public Circle[] circleCollidersData;
    public OrientedBox[] drainData;
    public Circle[] sourceData;
    public uint3[] spatialIndices;
    public uint[] spatialOffsets;
    public Particle[] particles;
    public uint[] keyarr;
    public NativeArray<FluidParam> fluidParamBuffer;
    public NativeArray<ScalingFactors> scalingFactorsBuffer;
    public NativeArray<OrientedBox> boxBuffer;
    public NativeArray<Circle> circleBuffer;
    public NativeArray<Circle> sourceBuffer;
    public NativeArray<OrientedBox> drainBuffer;
    public NativeArray<uint3> spatialIndicesBuffer;
    public NativeArray<uint> spatialOffsetsBuffer;
    public NativeArray<Particle> particleBuffer;
    public NativeArray<Particle> particleResultBuffer;
    public NativeArray<int2> offsets2DBuffer;
    public NativeArray<uint> keypopsbuffer;
    public NativeArray<uint> keyarrbuffer;

}