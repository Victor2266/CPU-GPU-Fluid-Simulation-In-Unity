using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;


public class CPUSimCalc {

    public uint3[] Entries;

    public uint[] Offsets;

    public float2[] Velocities;
    public float2[] Densities;
    public float2[] Positions;
    public float2[] PredictedPositions;
    public uint[] GridsToCalc = new uint[1];

    SpatialHashCPU spatialHashCPU = new();

    FluidMaths2DCPU fluidMaths2DCPU = new();

    public float smoothingRadius;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float gravity;
    public float deltaTime;
    public float collisionDamping;
    public float viscosityStrength;
    public float2 boundsSize;
    public float2 interactionInputPoint;
    public float interactionInputStrength;
    public float interactionInputRadius;

    public float2 obstacleSize;
    public float2 obstacleCentre;

    public uint numParticlesCPU;

    public void initialize(int numParticles, float smoothingRadius){
        this.fluidMaths2DCPU.setSmoothingRadius(smoothingRadius);
        this.smoothingRadius = smoothingRadius;
        this.numParticlesCPU = (uint) numParticles;
        this.Entries = new uint3[numParticles];
        this.Offsets = new uint[numParticles];
        this.Velocities = new float2[numParticles];
        this.Densities = new float2[numParticles];
        this.Positions = new float2[numParticles];
        this.PredictedPositions = new float2[numParticles];
    }
    float DensityKernel(float dst, float radius)
    {
    	return fluidMaths2DCPU.SpikyKernelPow2(dst, radius);
    }

    float NearDensityKernel(float dst, float radius)
    {
    	return fluidMaths2DCPU.SpikyKernelPow3(dst, radius);
    }

    float DensityDerivative(float dst, float radius)
    {
    	return fluidMaths2DCPU.DerivativeSpikyPow2(dst, radius);
    }

    float NearDensityDerivative(float dst, float radius)
    {
    	return fluidMaths2DCPU.DerivativeSpikyPow3(dst, radius);
    }

    float ViscosityKernel(float dst, float radius)
    {
    	return fluidMaths2DCPU.SmoothingKernelPoly6(dst, smoothingRadius);
    }

    float2 CalculateDensity(float2 pos){

        int2 originCell = spatialHashCPU.GetCell2D(pos, smoothingRadius);
	    float sqrRadius = smoothingRadius * smoothingRadius;
	    float density = 0;
	    float nearDensity = 0;

	    // Neighbour search
	    for (int i = 0; i < 9; i++)
	    {
	    	uint hash = spatialHashCPU.HashCell2D(originCell + spatialHashCPU.offsets2D[i]);
	    	uint key = spatialHashCPU.KeyFromHash(hash, numParticlesCPU);
	    	uint currIndex = Offsets[key];

	    	while (currIndex < numParticlesCPU)
	    	{
	    		uint3 indexData = Entries[currIndex];
	    		currIndex++;
	    		// Exit if no longer looking at correct bin
	    		if (indexData[2] != key) break;
	    		// Skip if hash does not match
	    		if (indexData[1] != hash) continue;

	    		uint neighbourIndex = indexData[0];
	    		float2 neighbourPos = PredictedPositions[neighbourIndex];
	    		float2 offsetToNeighbour = neighbourPos - pos;
	    		float sqrDstToNeighbour = fluidMaths2DCPU.Dot(offsetToNeighbour, offsetToNeighbour);

	    		// Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		// Calculate density and near density
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		density += DensityKernel(dst, smoothingRadius);
	    		nearDensity += NearDensityKernel(dst, smoothingRadius);
	    	}
	    }

	    return new float2(density, nearDensity);
    }

    public void CalculateDensityCPU(uint id){

        float2 pos = PredictedPositions[id];
        Densities[id] = CalculateDensity(pos);
    }
    float PressureFromDensity(float density)
    {
	    return (density - targetDensity) * pressureMultiplier;
    }

    float NearPressureFromDensity(float nearDensity)
    {
	    return nearPressureMultiplier * nearDensity;
    }

    public void HandleCollisionsCPU(uint particleIndex)
    {
    	float2 pos = Positions[particleIndex];
    	float2 vel = Velocities[particleIndex];

    	// Keep particle inside bounds
    	float2 halfSize = new float2(0,0);
        halfSize.x = (float)(boundsSize.x * 0.5);
        halfSize.y = (float)(boundsSize.y * 0.5);
    	float2 edgeDst = new float2(0,0);
        edgeDst.x = halfSize.x - Math.Abs(pos.x);
        edgeDst.y = halfSize.y - Math.Abs(pos.y);

    	if (edgeDst.x <= 0)
    	{
    		pos.x = halfSize.x * Math.Sign(pos.x);
    		vel.x *= -1 * collisionDamping;
    	}
    	if (edgeDst.y <= 0)
    	{
    		pos.y = halfSize.y * Math.Sign(pos.y);
    		vel.y *= -1 * collisionDamping;
    	}

    	// Collide particle against the test obstacle
    	float2 obstacleHalfSize = new float2(0,0);
        obstacleHalfSize.x = (float)(obstacleSize.x * 0.5);
        obstacleHalfSize.y = (float)(obstacleSize.y * 0.5);
    	float2 obstacleEdgeDst = new float2(0,0);
        obstacleEdgeDst.x = obstacleHalfSize.x - Math.Abs(pos.x - obstacleCentre.x);
        obstacleEdgeDst.y = obstacleHalfSize.y - Math.Abs(pos.y - obstacleCentre.y);

    	if (obstacleEdgeDst.x >= 0 && obstacleEdgeDst.y >= 0)
    	{
    		if (obstacleEdgeDst.x < obstacleEdgeDst.y) {
    			pos.x = obstacleHalfSize.x * Math.Sign(pos.x - obstacleCentre.x) + obstacleCentre.x;
    			vel.x *= -1 * collisionDamping;
    		}
    		else {
    			pos.y = obstacleHalfSize.y * Math.Sign(pos.y - obstacleCentre.y) + obstacleCentre.y;
    			vel.y *= -1 * collisionDamping;
    		}
    	}

    	// Update position and velocity
    	Positions[particleIndex] = pos;
    	Velocities[particleIndex] = vel;
    }


    void CalculatePressureForceCPU (uint id)
    {
    	float density = Densities[id][0];
    	float densityNear = Densities[id][1];
    	float pressure = PressureFromDensity(density);
    	float nearPressure = NearPressureFromDensity(densityNear);
    	float2 pressureForce = 0;
    
    	float2 pos = PredictedPositions[id];
    	int2 originCell = spatialHashCPU.GetCell2D(pos, smoothingRadius);
    	float sqrRadius = smoothingRadius * smoothingRadius;

    	// Neighbour search
    	for (int i = 0; i < 9; i ++)
    	{
    		uint hash = spatialHashCPU.HashCell2D(originCell + spatialHashCPU.offsets2D[i]);
    		uint key = spatialHashCPU.KeyFromHash(hash, numParticlesCPU);
    		uint currIndex = Offsets[key];

    		while (currIndex < numParticlesCPU)
    		{
    			uint3 indexData = Entries[currIndex];
    			currIndex ++;
    			// Exit if no longer looking at correct bin
    			if (indexData[2] != key) break;
    			// Skip if hash does not match
    			if (indexData[1] != hash) continue;

    			uint neighbourIndex = indexData[0];
    			// Skip if looking at self
    			if (neighbourIndex == id) continue;

    			float2 neighbourPos = PredictedPositions[neighbourIndex];
    			float2 offsetToNeighbour = neighbourPos - pos;
    			float sqrDstToNeighbour = fluidMaths2DCPU.Dot(offsetToNeighbour, offsetToNeighbour);

    			// Skip if not within radius
    			if (sqrDstToNeighbour > sqrRadius) continue;

    			// Calculate pressure force
    			float dst = (float)Math.Sqrt(sqrDstToNeighbour);
    			float2 dirToNeighbour = dst > 0 ? offsetToNeighbour / dst : new float2(0, 1);

    			float neighbourDensity = Densities[neighbourIndex][0];
    			float neighbourNearDensity = Densities[neighbourIndex][1];
    			float neighbourPressure = PressureFromDensity(neighbourDensity);
    			float neighbourNearPressure = NearPressureFromDensity(neighbourNearDensity);

    			float sharedPressure = (float)((pressure + neighbourPressure) * 0.5);
    			float sharedNearPressure = (float)((nearPressure + neighbourNearPressure) * 0.5);

    			pressureForce += dirToNeighbour * DensityDerivative(dst, smoothingRadius) * sharedPressure / neighbourDensity;
    			pressureForce += dirToNeighbour * NearDensityDerivative(dst, smoothingRadius) * sharedNearPressure / neighbourNearDensity;
    		}
    	}

    	float2 acceleration = pressureForce / density;
    	Velocities[id] += acceleration * deltaTime;//
    }
    void CalculateViscosity (uint id)
    {
    	float2 pos = PredictedPositions[id];
    	int2 originCell = spatialHashCPU.GetCell2D(pos, smoothingRadius);
    	float sqrRadius = smoothingRadius * smoothingRadius;

    	float2 viscosityForce = 0;
    	float2 velocity = Velocities[id];

    	for (int i = 0; i < 9; i ++)
    	{
    		uint hash = spatialHashCPU.HashCell2D(originCell + spatialHashCPU.offsets2D[i]);
    		uint key = spatialHashCPU.KeyFromHash(hash, numParticlesCPU);
    		uint currIndex = Offsets[key];

    		while (currIndex < numParticlesCPU)
    		{
    			uint3 indexData = Entries[currIndex];
    			currIndex ++;
    			// Exit if no longer looking at correct bin
    			if (indexData[2] != key) break;
    			// Skip if hash does not match
    			if (indexData[1] != hash) continue;

    			uint neighbourIndex = indexData[0];
    			// Skip if looking at self
    			if (neighbourIndex == id) continue;

    			float2 neighbourPos = PredictedPositions[neighbourIndex];
    			float2 offsetToNeighbour = neighbourPos - pos;
    			float sqrDstToNeighbour = fluidMaths2DCPU.Dot(offsetToNeighbour, offsetToNeighbour);

    			// Skip if not within radius
    			if (sqrDstToNeighbour > sqrRadius) continue;

    			float dst = (float)Math.Sqrt(sqrDstToNeighbour);
    			float2 neighbourVelocity = Velocities[neighbourIndex];
    			viscosityForce += (neighbourVelocity - velocity) * ViscosityKernel(dst, smoothingRadius);
    		}

    	}
    	Velocities[id] += viscosityForce * viscosityStrength * deltaTime;
    }
}