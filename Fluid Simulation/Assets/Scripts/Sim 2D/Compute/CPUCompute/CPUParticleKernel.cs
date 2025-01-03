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

//Defining Structs
// [System.Serializable]
// [StructLayout(LayoutKind.Sequential, Size = 40)]
// public struct Particle // 40 bytes total
// {
//     public float2 density; //8 bytes, density and near density
//     public Vector2 velocity; //8 bytes
//     public Vector2 predictedPosition; // 8
//     public Vector2 position; // 8
//     public float temperature; // 4
//     public FluidType type; // 4 (enum is int by default)
// }

// [System.Serializable]
// [StructLayout(LayoutKind.Sequential, Size = 12)]
// public struct Circle //12 bytes total
// {
//     public Vector2 pos; //8 bytes
//     public float radius; //4 bytes
// }

// [System.Serializable]
// [StructLayout(LayoutKind.Sequential, Size = 24)]
// public struct OrientedBox //24 bytes total
// {
//     public Vector2 pos; //8 bytes
//     public Vector2 size;
//     public Vector2 zLocal;
// };


// This structure is used to hold the kernel variables. allows for the variables to be changed easily during runtime.
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 96)]
public struct kernelSettings
{
    public uint numCPUCells; //4
    public uint numParticles; //4
    public uint numBoxColliders; //4
    public uint numCircleColliders; //4
    public uint MAX_COLLIDERS; //4
    public float gravity; //4
    public float deltaTime; //4
    public float collisionDamping; //4
    public float smoothingRadius; //4
    public float targetDensity; //4
    public float pressureMultiplier; //4
    public float nearPressureMultiplier; //4
    public float viscosityStrength; //4
    public float2 boundsSize; //8
    public float2 interactionInputPoint; //8
    public float interactionInputStrength; //4
    public float interactionInputRadius; //4
    public float Poly6ScalingFactor; //4
    public float SpikyPow3ScalingFactor; //4
    public float SpikyPow2ScalingFactor; //4
    public float SpikyPow3DerivativeScalingFactor; //4
    public float SpikyPow2DerivativeScalingFactor; //4
}

//Density kernel calculates density for a given particle index and puts resulting particle with updated density into result buffer
public struct DensityCalcCPU : IJob
{
<<<<<<< Updated upstream
    public NativeArray<Particle> result;
=======
    public NativeSlice<Particle> result;
>>>>>>> Stashed changes
    [ReadOnly]
    public NativeArray<int> index;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public NativeArray<kernelSettings> kernelVariables;
    
    public void Execute()
    {
        Particle particle = particles[index[0]];
        kernelSettings settings = kernelVariables[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();
        int2 originCell = SHash.GetCell2D(particle.position, settings.smoothingRadius);
        float sqrRadius = settings.smoothingRadius * settings.smoothingRadius;
        float density = 0;
        float nearDensity = 0;

        //neighbour search
        for (int i = 0; i < 9; i++)
        {
            uint hash = SHash.HashCell2D(originCell + SHash.offsets2D[i]);
            int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
            int currIndex = (int) spatialOffsets[key];

            while (currIndex != settings.numParticles){
                uint3 indexData = spatialIndices[currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                int neighbourIndex = (int) indexData[0];
                float2 neighbourPos = particles[neighbourIndex].predictedPosition;
                float2 offsetToNeighbour;
                offsetToNeighbour.x = neighbourPos.x - particle.position.x;
                offsetToNeighbour.y = neighbourPos.y - particle.position.y;
                float sqrDstToNeighbour = FMath.Dot(offsetToNeighbour, offsetToNeighbour);
                // Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		// Calculate density and near density
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		density += FMath.SpikyKernelPow2(dst, settings.smoothingRadius);
	    		nearDensity += FMath.SpikyKernelPow3(dst, settings.smoothingRadius);
            }
        }

        particle.density = new float2(density, nearDensity);
        Debug.Log(particle.density);
        result[0] = particle; // may want to move this out of the job
    }

}

//currently not used
public struct HandleBoxCollision : IJob
{
    public NativeArray<int> index;
    public NativeArray<kernelSettings> kernelVariables;
    public NativeArray<Particle> particles;
<<<<<<< Updated upstream
    public NativeArray<Particle> result;
=======
    public NativeSlice<Particle> result;
>>>>>>> Stashed changes
    public NativeArray<OrientedBox> boxes;
    public void Execute()
    {
        OrientedBox box = boxes[index[1]];
        Particle particle = particles[index[0]];
        kernelSettings settings = kernelVariables[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        // Transform position to box's local space
        float2 localPos = particle.position - box.pos;

        // Create rotation matrix for local space transformation
        float2 right = box.zLocal;
        float2 up;
        up.y = box.zLocal.y * -1;
        up.x = box.zLocal.x;

        // Transform to box's local space
        float2 rotatedPos = new float2(
            FMath.Dot(localPos, right),
            FMath.Dot(localPos, up)
        );

        // Calculate distance to box edges in local space
        float2 boxHalfSize = box.size * 0.5F;
        float2 distanceFromCenter;
        distanceFromCenter.x = Math.Abs(rotatedPos.x);
        distanceFromCenter.y = Math.Abs(rotatedPos.y);
        float2 penetration = distanceFromCenter - boxHalfSize;

        // Only process collision if we're actually inside the box
        if (penetration.x < 0 && penetration.y < 0)
        {
            // Transform velocity to local space
            float2 localVel = new float2(
                FMath.Dot(particle.velocity, right),
                FMath.Dot(particle.velocity, up)
            );

            // Determine which axis has less penetration
            if (penetration.x > penetration.y)
            {
                // X axis collision
                float sign = rotatedPos.x > 0 ? 1 : -1;
                rotatedPos.x = boxHalfSize.x * sign;
                localVel.x *= -1 * settings.collisionDamping;
            }
            else
            {
                // Y axis collision
                float sign = rotatedPos.y > 0 ? 1 : -1;
                rotatedPos.y = boxHalfSize.y * sign;
                localVel.y *= -1 * settings.collisionDamping;
            }

            // Transform position back to world space
            particle.position = (right * rotatedPos.x) + (up * rotatedPos.y);
            particle.position += box.pos; 
            // Transform velocity back to world space
            particle.velocity = right * localVel.x + up * localVel.y;
            result[0] = particle;
        }
    }
}
//currently not used
public struct HandleCollisionCalc : IJob
{
    public NativeArray<int> index;
    public NativeArray<kernelSettings> _kernelVariables;
    public NativeArray<Particle> _particles;
    public NativeArray<OrientedBox> _boxes;

    public void Execute()
    {
        Particle particle = _particles[index[0]];
        kernelSettings settings = _kernelVariables[0];
        float2 pos = particle.position;
        float2 vel = particle.velocity;

        // Keep particle inside bounds
        float2 halfSize;
        halfSize.x = settings.boundsSize.x * 0.5F;
        halfSize.y = settings.boundsSize.y * 0.5F;
        float2 edgeDst;
        edgeDst.x = halfSize.x - Math.Abs(pos.x);
        edgeDst.y = halfSize.y - Math.Abs(pos.y);

        if (edgeDst.x <= 0)
        {
            pos.x = halfSize.x * Math.Sign(pos.x);
            vel.x *= -1 * settings.collisionDamping;
        }
        if (edgeDst.y <= 0)
        {
            pos.y = halfSize.y * Math.Sign(pos.y);
            vel.y *= -1 * settings.collisionDamping;
        }

        // Handle box collisions
        for (int i = 0; i < settings.numBoxColliders; i++)
        {
            NativeArray<int> tempIndex = new NativeArray<int>(1, Allocator.TempJob);
            tempIndex[0] = i;
            HandleBoxCollision collider = new HandleBoxCollision
            {
                index = tempIndex,
                kernelVariables = _kernelVariables,
                particles = _particles,
                boxes = _boxes

            };

            JobHandle collisionhandle = collider.Schedule();
            collisionhandle.Complete();
            tempIndex.Dispose();
        }
    }
}

//Pressure kernel calculates pressure using density data and puts resulting particle with updated pressure into the result buffer
public struct CPUPressureCalc : IJob 
{
<<<<<<< Updated upstream
    public NativeArray<Particle> result;
=======
    public NativeSlice<Particle> result;
>>>>>>> Stashed changes
    [ReadOnly]
    public NativeArray<int> index;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public NativeArray<kernelSettings> kernelVariables;

    public void Execute()
    {
        Particle particle = particles[index[0]];
        kernelSettings settings = kernelVariables[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();

        float density = particle.density[0];
	    float densityNear = particle.density[1];
	    float pressure = (density - settings.targetDensity) * settings.pressureMultiplier;
	    float nearPressure = settings.nearPressureMultiplier * densityNear;
	    float2 pressureForce = 0;
    
	    float2 pos = particle.predictedPosition;
	    int2 originCell = SHash.GetCell2D(pos, settings.smoothingRadius);
	    float sqrRadius = settings.smoothingRadius * settings.smoothingRadius;

	    // Neighbour search
	    for (int i = 0; i < 9; i ++)
	    {
	    	uint hash = SHash.HashCell2D(originCell + SHash.offsets2D[i]);
	    	int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
	    	int currIndex = (int) spatialOffsets[key];

	    	while (currIndex < settings.numParticles)
	    	{
	    		uint3 indexData = spatialIndices[currIndex];
	    		currIndex ++;
	    		// Exit if no longer looking at correct bin
	    		if (indexData[2] != key) break;
	    		// Skip if hash does not match
	    		if (indexData[1] != hash) continue;

	    		int neighbourIndex = (int) indexData[0];
	    		// Skip if looking at self
	    		if (neighbourIndex == index[0]) continue;

	    		float2 neighbourPos = particles[neighbourIndex].predictedPosition;
	    		float2 offsetToNeighbour = neighbourPos - pos;
	    		float sqrDstToNeighbour = FMath.Dot(offsetToNeighbour, offsetToNeighbour);

	    		// Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		// Calculate pressure force
	    		float dst = (float)Math.Sqrt(sqrDstToNeighbour);
	    		float2 dirToNeighbour = dst > 0 ? offsetToNeighbour / dst : new float2(0, 1);

	    		float neighbourDensity = particles[neighbourIndex].density[0];
	    		float neighbourNearDensity = particles[neighbourIndex].density[1];
	    		float neighbourPressure = (neighbourDensity - settings.targetDensity) * settings.pressureMultiplier;
	    		float neighbourNearPressure = settings.nearPressureMultiplier * neighbourNearDensity;

	    		float sharedPressure = (pressure + neighbourPressure) * 0.5F;
	    		float sharedNearPressure = (nearPressure + neighbourNearPressure) * 0.5F;

	    		pressureForce += dirToNeighbour * FMath.SpikyKernelPow2(dst, settings.smoothingRadius) * sharedPressure / neighbourDensity;
	    		pressureForce += dirToNeighbour * FMath.SpikyKernelPow3(dst, settings.smoothingRadius) * sharedNearPressure / neighbourNearDensity;
	    	}
	    }

	    float2 acceleration = pressureForce / density;
	    particle.velocity.x += acceleration.x * settings.deltaTime;
        particle.velocity.y += acceleration.y * settings.deltaTime;
        result[0] = particle;
    }
    
}
//see pressure and density
public struct CPUViscosityCalc : IJob
{
<<<<<<< Updated upstream
    public NativeArray<Particle> result;
=======
    public NativeSlice<Particle> result;
>>>>>>> Stashed changes
    [ReadOnly]
    public NativeArray<int> index;
    [ReadOnly]
    public NativeArray<Particle> particles;
    [ReadOnly]
    public NativeArray<uint3> spatialIndices;
    [ReadOnly]
    public NativeArray<uint> spatialOffsets;
    [ReadOnly]
    public NativeArray<kernelSettings> kernelVariables;
    public void Execute()
    {
        Particle particle = particles[index[0]];
        kernelSettings settings = kernelVariables[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();

        float2 pos = particle.predictedPosition;
	    int2 originCell = SHash.GetCell2D(pos, settings.smoothingRadius);
	    float sqrRadius = settings.smoothingRadius * settings.smoothingRadius;

	    float2 viscosityForce = 0;
	    float2 velocity = particle.velocity;

	    for (int i = 0; i < 9; i ++)
	    {
	    	uint hash = SHash.HashCell2D(originCell + SHash.offsets2D[i]);
	    	int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
	    	int currIndex = (int) spatialOffsets[key];

	    	while (currIndex < settings.numParticles)
	    	{
	    		uint3 indexData = spatialIndices[currIndex];
	    		currIndex ++;
	    		// Exit if no longer looking at correct bin
	    		if (indexData[2] != key) break;
	    		// Skip if hash does not match
	    		if (indexData[1] != hash) continue;

	    		int neighbourIndex = (int) indexData[0];
	    		// Skip if looking at self
	    		if (neighbourIndex == index[0]) continue;

	    		float2 neighbourPos = particle.predictedPosition;
	    		float2 offsetToNeighbour = neighbourPos - pos;
	    		float sqrDstToNeighbour = FMath.Dot(offsetToNeighbour, offsetToNeighbour);

	    		// Skip if not within radius
	    		if (sqrDstToNeighbour > sqrRadius) continue;

	    		float dst = (float) Math.Sqrt(sqrDstToNeighbour);
	    		float2 neighbourVelocity = particles[neighbourIndex].velocity;
	    		viscosityForce += (neighbourVelocity - velocity) * FMath.SmoothingKernelPoly6(dst, settings.smoothingRadius);
	    	}

	    }
	    particle.velocity.x += viscosityForce.x * settings.viscosityStrength * settings.deltaTime;
        particle.velocity.y += viscosityForce.y * settings.viscosityStrength * settings.deltaTime;
        result[0] = particle;
    }
}


//needs updating.
// Calls the density compute job on every particle in the given cell, currently need to figure out efficient way of storing the data
<<<<<<< Updated upstream
public struct CPUCellDensityExecuteManager : IJob 
{
    public NativeArray<int2> cell;
    public NativeArray<Particle> _particleBuffer;
    //public NativeArray<Particle> resultBuffer;
    public NativeArray<OrientedBox> _boxBuffer;
    public NativeArray<kernelSettings> _variablesBuffer;
    public NativeArray<uint3> _spatialIndicesBuffer;
=======
public struct CPUCellExecuteManager : IJob 
{
    public NativeArray<Particle> resultBuffer; // Result buffer is copy of particle buffer but will contain all changes
    [ReadOnly]
    public NativeArray<uint> hash;
    [ReadOnly]
    public NativeArray<Particle> _particleBuffer;
    [ReadOnly]
    public NativeArray<OrientedBox> _boxBuffer;
    [ReadOnly]
    public NativeArray<kernelSettings> _variablesBuffer;
    [ReadOnly]
    public NativeArray<uint3> _spatialIndicesBuffer;
    [ReadOnly]
>>>>>>> Stashed changes
    public NativeArray<uint> _spatialOffsetsBuffer;

    public void Execute()
    {
        //_particleBuffer.CopyTo(resultBuffer);
        kernelSettings settings = _variablesBuffer[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();
<<<<<<< Updated upstream
        uint hash = SHash.HashCell2D(cell[0]);
        int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
        int currIndex = (int) _spatialOffsetsBuffer[key];

        List<DensityCalcCPU> densityCalcs = new List<DensityCalcCPU>();
        List<JobHandle> jobHandles = new List<JobHandle>(); //---If someone can figure out how to implement this as native list and have it work please do
        List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
=======
        //uint hash = SHash.HashCell2D(cell[0]);
        int key = (int) SHash.KeyFromHash(hash[0], settings.numParticles);
        int currIndex = (int) _spatialOffsetsBuffer[key];

        List<DensityCalcCPU> densityCalcs = new List<DensityCalcCPU>();
        List<CPUPressureCalc> pressureCalcs = new List<CPUPressureCalc>();
        List<CPUViscosityCalc> viscosityCalcs = new List<CPUViscosityCalc>();
        NativeList<JobHandle> jobHandles = new NativeList<JobHandle>(Allocator.TempJob); //---If someone can figure out how to implement this as native list and have it work please do
        List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
        List<NativeSlice<Particle>> particleSlices = new List<NativeSlice<Particle>>();
>>>>>>> Stashed changes
        int currentParticle = 0;
        while (currIndex != settings.numParticles){
            uint3 indexData = _spatialIndicesBuffer[currIndex];
            currIndex++;
            // Exit if no longer looking at correct bin
            if (indexData[2] != key) break;
            // Skip if hash does not match
<<<<<<< Updated upstream
            if (indexData[1] != hash) continue;

            int neighbourIndex = (int) indexData[0];
            NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
            TempArray[0] = currentParticle;
            particleIndices.Add(TempArray);
=======
            if (indexData[1] != hash[0]) continue;

            NativeArray<int> temparray = new NativeArray<int>(1, Allocator.TempJob);
            temparray[0] = currIndex;
            particleIndices.Add(temparray);
            particleSlices.Add(new NativeSlice<Particle>(resultBuffer, currIndex, 1));
>>>>>>> Stashed changes
            densityCalcs.Add(new DensityCalcCPU{
                index = particleIndices[currentParticle],
                kernelVariables = _variablesBuffer,
                particles = _particleBuffer, //add result buffer
                spatialIndices = _spatialIndicesBuffer,
<<<<<<< Updated upstream
                spatialOffsets = _spatialOffsetsBuffer
            });

            jobHandles.Add(densityCalcs[currentParticle].Schedule());
            currentParticle++;
        }

        for (int i=0; i<currentParticle; i++){
            jobHandles[i].Complete();
            //resultBuffer[densityCalcs[i].index[0]] = densityCalcs[i].result[0];
        }
        densityCalcs = null;
        jobHandles = null;
=======
                spatialOffsets = _spatialOffsetsBuffer,
                result = particleSlices[currentParticle]
            });

            pressureCalcs.Add(new CPUPressureCalc{
                index = particleIndices[currentParticle],
                kernelVariables = _variablesBuffer,
                particles = _particleBuffer, //add result buffer
                spatialIndices = _spatialIndicesBuffer,
                spatialOffsets = _spatialOffsetsBuffer,
                result = particleSlices[currentParticle]
            });

            viscosityCalcs.Add(new CPUViscosityCalc{
                index = particleIndices[currentParticle],
                kernelVariables = _variablesBuffer,
                particles = _particleBuffer, //add result buffer
                spatialIndices = _spatialIndicesBuffer,
                spatialOffsets = _spatialOffsetsBuffer,
                result = particleSlices[currentParticle]
            });
            currentParticle++;
        }

        for(int j=0; j<3; j++){
            for(int i=0; i<currentParticle; i++){
                if(j == 0) {jobHandles.Add(densityCalcs[i].Schedule());}
                else if(j == 1) {jobHandles.Add(pressureCalcs[i].Schedule());}
                else if(i == 2) {jobHandles.Add(viscosityCalcs[i].Schedule());}
            }
            JobHandle.CompleteAll(jobHandles);
            resultBuffer.CopyTo(_particleBuffer);
            jobHandles.Clear();
        }
        
        

        densityCalcs = null;
        jobHandles.Dispose();
>>>>>>> Stashed changes
        for(int i=0; i<particleIndices.ToArray().Length; i++){
            particleIndices[i].Dispose();
        }
    }
}

//add result buffer
// same as CPUCellDensityExecute but for pressure
public struct CPUCellPressureExecuteManager : IJob 
{
    public NativeArray<int2> cell;
    public NativeArray<Particle> _particleBuffer;
    public NativeArray<OrientedBox> _boxBuffer;
    public NativeArray<kernelSettings> _variablesBuffer;
    public NativeArray<uint3> _spatialIndicesBuffer;
    public NativeArray<uint> _spatialOffsetsBuffer;

    public void Execute()
    {
        kernelSettings settings = _variablesBuffer[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();
        uint hash = SHash.HashCell2D(cell[0]);
        int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
        int currIndex = (int) _spatialOffsetsBuffer[key];

        List<CPUPressureCalc> pressureCalcs = new List<CPUPressureCalc>();
        List<JobHandle> jobHandles = new List<JobHandle>();//---If someone can figure out how to implement this as native list and have it work please do
        List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
        int currentParticle = 0;
        while (currIndex != settings.numParticles){
            uint3 indexData = _spatialIndicesBuffer[currIndex];
            currIndex++;
            // Exit if no longer looking at correct bin
            if (indexData[2] != key) break;
            // Skip if hash does not match
            if (indexData[1] != hash) continue;

            int neighbourIndex = (int) indexData[0];
            NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
            TempArray[0] = currentParticle;
            particleIndices.Add(TempArray);
            pressureCalcs.Add(new CPUPressureCalc{
                index = particleIndices[currentParticle],
                kernelVariables = _variablesBuffer,
                particles = _particleBuffer,
                spatialIndices = _spatialIndicesBuffer,
                spatialOffsets = _spatialOffsetsBuffer
            });

            jobHandles.Add(pressureCalcs[currentParticle].Schedule());
            currentParticle++;
        }

        for (int i=0; i<currentParticle; i++){
            jobHandles[i].Complete();
        }

        pressureCalcs = null;
        jobHandles = null;
        for(int i=0; i<particleIndices.ToArray().Length; i++){
            particleIndices[i].Dispose();
        }
    }
}

// add result buffer
// same as CPUCellDensityExecute but for viscosity
public struct CPUCellViscosityExecuteManager : IJob 
{
    public NativeArray<int2> cell;
    public NativeArray<Particle> _particleBuffer;
    public NativeArray<OrientedBox> _boxBuffer;
    public NativeArray<kernelSettings> _variablesBuffer;
    public NativeArray<uint3> _spatialIndicesBuffer;
    public NativeArray<uint> _spatialOffsetsBuffer;

    public void Execute()
    {
        kernelSettings settings = _variablesBuffer[0];
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();
        uint hash = SHash.HashCell2D(cell[0]);
        int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
        int currIndex = (int) _spatialOffsetsBuffer[key];

        List<CPUViscosityCalc> viscosityCalcs = new List<CPUViscosityCalc>();
        List<JobHandle> jobHandles = new List<JobHandle>();
        List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
        int currentParticle = 0;
        while (currIndex != settings.numParticles){
            uint3 indexData = _spatialIndicesBuffer[currIndex];
            currIndex++;
            // Exit if no longer looking at correct bin
            if (indexData[2] != key) break;
            // Skip if hash does not match
            if (indexData[1] != hash) continue;

            int neighbourIndex = (int) indexData[0];
            NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
            TempArray[0] = currentParticle;
            particleIndices.Add(TempArray);
            viscosityCalcs.Add(new CPUViscosityCalc{
                index = particleIndices[currentParticle],
                kernelVariables = _variablesBuffer,
                particles = _particleBuffer,
                spatialIndices = _spatialIndicesBuffer,
                spatialOffsets = _spatialOffsetsBuffer
            });

            jobHandles.Add(viscosityCalcs[currentParticle].Schedule());
            currentParticle++;
        }

        for (int i=0; i<currentParticle; i++){
            jobHandles[i].Complete();
        }

        viscosityCalcs = null;
        jobHandles = null;
        for(int i=0; i<particleIndices.ToArray().Length; i++){
            particleIndices[i].Dispose();
<<<<<<< Updated upstream
=======
            particleSl
>>>>>>> Stashed changes
        }
    }
}

//This class is created in Simulation2DAos.cs and called to run the CPU Kernel
public class CPUParticleKernel : MonoBehaviour
{
    public kernelSettings settings;
    public Particle[] particleData;
    public OrientedBox[] boxCollidersData;
    public Circle[] circleCollidersData;
    public uint3[] spatialIndicesCPU;
    public uint[] spatialOffsetsCPU;

    public int2[] CPUOriginCells;

    public NativeArray<Particle> particleBuffer;
    public NativeArray<OrientedBox> boxBuffer;
    public NativeArray<kernelSettings> variablesBuffer;
    public NativeArray<uint3> spatialIndicesBuffer;
    public NativeArray<uint> spatialOffsetsBuffer;
    public NativeArray<int2> CPUOriginCellBuffer;
    public NativeArray<int2>[] cellBuffers;

    public int InitializeCPUKernelSettings()
    {
        particleData = new Particle[settings.numParticles];
        boxCollidersData = new OrientedBox[settings.numBoxColliders];
        circleCollidersData = new Circle[settings.numCircleColliders];
        spatialIndicesCPU = new uint3[settings.numParticles];
        spatialOffsetsCPU = new uint[settings.numParticles];
        CPUOriginCells = new int2[settings.numCPUCells];
        return 0;
    }
    //setups all buffers for next compute cycle.
    private int setupCPUCompute()
    {
        particleBuffer = new NativeArray<Particle>((int) settings.numParticles, Allocator.TempJob);
        boxBuffer = new NativeArray<OrientedBox>((int) settings.numBoxColliders, Allocator.TempJob);
        variablesBuffer = new NativeArray<kernelSettings>(1, Allocator.TempJob);
        spatialIndicesBuffer = new NativeArray<uint3>((int) settings.numParticles, Allocator.TempJob);
        spatialOffsetsBuffer = new NativeArray<uint>((int) settings.numParticles, Allocator.TempJob);
        CPUOriginCellBuffer = new NativeArray<int2>((int) settings.numCPUCells, Allocator.TempJob);
        cellBuffers = new NativeArray<int2>[settings.numCPUCells];
        for (int i=0; i<settings.numCPUCells; i++){
            cellBuffers[i] = new NativeArray<int2>(1, Allocator.TempJob);
        }

        Debug.Log(particleBuffer.Length);
        particleBuffer.CopyFrom(particleData);
        boxBuffer.CopyFrom(boxCollidersData);
        variablesBuffer[0] = settings;
        spatialIndicesBuffer.CopyFrom(spatialIndicesCPU);
        spatialOffsetsBuffer.CopyFrom(spatialOffsetsCPU);
        CPUOriginCellBuffer.CopyFrom(CPUOriginCells);
        return 0;
    }

    //This compute version uses the Execute manager kernels. CURRENTLY DOES NOT WORK, need to reworks some buffers.
    public int ExecuteCPUCompute()
    {
        if (setupCPUCompute() == 1){
            return 1;
        }

        Debug.Log("pass");
        //This for loops calls the density manager kernel for each cell sent to the CPU fluid sim kernel.
        CPUCellDensityExecuteManager[] cellExecuteDensityManagers = new CPUCellDensityExecuteManager[settings.numCPUCells];
        JobHandle[] jobDensityHandleArray = new JobHandle[settings.numCPUCells]; //---If someone can figure out how to implement this as native list and have it work please do
        for(int i=0; i<settings.numCPUCells; i++)
        {
            cellBuffers[i][0] = CPUOriginCells[i];
            cellExecuteDensityManagers[i] = new CPUCellDensityExecuteManager{
                cell = cellBuffers[i],
                _particleBuffer = particleBuffer,
                _boxBuffer = boxBuffer,
                _variablesBuffer = variablesBuffer,
                _spatialIndicesBuffer = spatialIndicesBuffer,
                _spatialOffsetsBuffer = spatialOffsetsBuffer
            };
            jobDensityHandleArray[i] = cellExecuteDensityManagers[i].Schedule();
        }

        for(int i=0; i<settings.numCPUCells; i++)
        {
            jobDensityHandleArray[i].Complete();
        }
        
        //nulled for garbage collection
        cellExecuteDensityManagers = null;
        jobDensityHandleArray = null;

        //this loop is same as above but for pressure
        CPUCellPressureExecuteManager[] cellExecutePressureManagers = new CPUCellPressureExecuteManager[settings.numCPUCells];
        JobHandle[] jobPressureHandleArray = new JobHandle[settings.numCPUCells];
        for(int i=0; i<settings.numCPUCells; i++)
        {
            //cellBuffers[i][0] = CPUOriginCells[i];
            cellExecutePressureManagers[i] = new CPUCellPressureExecuteManager{
                cell = cellBuffers[i],
                _particleBuffer = particleBuffer,
                _boxBuffer = boxBuffer,
                _variablesBuffer = variablesBuffer,
                _spatialIndicesBuffer = spatialIndicesBuffer,
                _spatialOffsetsBuffer = spatialOffsetsBuffer
            };
            jobPressureHandleArray[i] = cellExecutePressureManagers[i].Schedule();
        }
        cellExecutePressureManagers = null;
        jobPressureHandleArray = null;

        //this loop is same as above but for viscosity
        CPUCellViscosityExecuteManager[] cellExecuteViscosityManagers = new CPUCellViscosityExecuteManager[settings.numCPUCells];
        JobHandle[] jobViscosityHandleArray = new JobHandle[settings.numCPUCells];
        for(int i=0; i<settings.numCPUCells; i++)
        {
            //cellBuffers[i][0] = CPUOriginCells[i];
            cellExecuteViscosityManagers[i] = new CPUCellViscosityExecuteManager{
                cell = cellBuffers[i],
                _particleBuffer = particleBuffer,
                _boxBuffer = boxBuffer,
                _variablesBuffer = variablesBuffer,
                _spatialIndicesBuffer = spatialIndicesBuffer,
                _spatialOffsetsBuffer = spatialOffsetsBuffer
            };
            jobViscosityHandleArray[i] = cellExecuteViscosityManagers[i].Schedule();
        }
        //write updated particle data to array (move out of buffer)
        particleBuffer.CopyTo(particleData);


        //clean up buffers for next cycle.
        //NOTE: Can probably make the buffers persistent and just overwrite them each cycle but is potentially dangerous to have persistent buffers
        cellExecuteViscosityManagers = null;
        jobViscosityHandleArray = null;
        particleBuffer.Dispose();
        boxBuffer.Dispose();
        variablesBuffer.Dispose();
        spatialIndicesBuffer.Dispose();
        spatialOffsetsBuffer.Dispose();
        CPUOriginCellBuffer.Dispose();
        for(int i=0; i<settings.numCPUCells; i++)
        {
            cellBuffers[i].Dispose();
        }
        cellBuffers = null;
        return 0;
    }

    //This compute version is a serialized version of the ExecuteCPUCompute function and the manager kernels. Performs the same function
    public int ExecuteCPUComputeB()
    {   
        //setup buffers
        if (setupCPUCompute() == 1){
            return 1;
        }
        //math objects for calculating hash, etc
        FluidMaths2DCPUAoS FMath = new FluidMaths2DCPUAoS();
        FMath.setSmoothingRadius(settings.smoothingRadius);
        SpatialHashCPU SHash = new SpatialHashCPU();

        //density loop but serialized.
        for(int i=0; i<settings.numCPUCells; i++){
            uint hash = SHash.HashCell2D(CPUOriginCells[i]);
            int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
            int currIndex = (int) spatialOffsetsCPU[key];

            List<DensityCalcCPU> densityCalcs = new List<DensityCalcCPU>();
            List<JobHandle> jobHandles = new List<JobHandle>(); //---If someone can figure out how to implement this as native list and have it work please do
            List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
            List<NativeArray<Particle>> resultBuffers = new List<NativeArray<Particle>>();
            int currentParticle = 0;
            while (currIndex != settings.numParticles)
            {
                uint3 indexData = spatialIndicesCPU[currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                int neighbourIndex = (int) indexData[0];
                NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
                NativeArray<Particle> result = new NativeArray<Particle>(1, Allocator.TempJob);
                resultBuffers.Add(result);
                TempArray[0] = currentParticle;
                particleIndices.Add(TempArray);
                densityCalcs.Add(new DensityCalcCPU{
                    index = particleIndices[currentParticle],
                    kernelVariables = variablesBuffer,
                    particles = particleBuffer, //add result buffer
                    spatialIndices = spatialIndicesBuffer,
                    spatialOffsets = spatialOffsetsBuffer,
                    result = resultBuffers[currentParticle]
                });

                jobHandles.Add(densityCalcs[currentParticle].Schedule());
                currentParticle++;
            }

            for (int j=0; j<currentParticle; j++){
            jobHandles[j].Complete();
            
            }
            for (int j=0; j<currentParticle; j++){
            particleBuffer[densityCalcs[j].index[0]] = densityCalcs[j].result[0];
            
            }
            
            //nulled for garbage collection
            densityCalcs = null;
            jobHandles = null;
            for(int j=0; j<currentParticle; j++){
                particleIndices[j].Dispose();
                resultBuffers[j].Dispose();
            }
        }

        //pressure loop serialized
        for(int i=0; i<settings.numCPUCells; i++){

            uint hash = SHash.HashCell2D(CPUOriginCells[i]);
            int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
            int currIndex = (int) spatialOffsetsCPU[key];

            List<CPUPressureCalc> pressureCalcs = new List<CPUPressureCalc>();
            List<JobHandle> jobHandles = new List<JobHandle>(); //---If someone can figure out how to implement this as native list and have it work please do
            List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
            List<NativeArray<Particle>> resultBuffers = new List<NativeArray<Particle>>();
            int currentParticle = 0;
            while (currIndex != settings.numParticles)
            {
                uint3 indexData = spatialIndicesCPU[currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                int neighbourIndex = (int) indexData[0];
                NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
                NativeArray<Particle> result = new NativeArray<Particle>(1, Allocator.TempJob);
                resultBuffers.Add(result);
                TempArray[0] = currentParticle;
                particleIndices.Add(TempArray);
                pressureCalcs.Add(new CPUPressureCalc{
                    index = particleIndices[currentParticle],
                    kernelVariables = variablesBuffer,
                    particles = particleBuffer, //add result buffer
                    spatialIndices = spatialIndicesBuffer,
                    spatialOffsets = spatialOffsetsBuffer,
                    result = resultBuffers[currentParticle]
                });

                jobHandles.Add(pressureCalcs[currentParticle].Schedule());
                currentParticle++;
            }

            for (int j=0; j<currentParticle; j++){
            jobHandles[j].Complete();
            
            }
            for (int j=0; j<currentParticle; j++){
            particleBuffer[pressureCalcs[j].index[0]] = pressureCalcs[j].result[0];
            
            }
            pressureCalcs = null;
            jobHandles = null;
            for(int j=0; j<currentParticle; j++){
                particleIndices[j].Dispose();
                resultBuffers[j].Dispose();
            }
        }

        //viscosity loop serialized
        for(int i=0; i<settings.numCPUCells; i++){
            uint hash = SHash.HashCell2D(CPUOriginCells[i]);
            int key = (int) SHash.KeyFromHash(hash, settings.numParticles);
            int currIndex = (int) spatialOffsetsCPU[key];
            List<CPUViscosityCalc> viscosityCalcs = new List<CPUViscosityCalc>();
            List<JobHandle> jobHandles = new List<JobHandle>();//---If someone can figure out how to implement this as native list and have it work please do
            List<NativeArray<int>> particleIndices = new List<NativeArray<int>>();
            List<NativeArray<Particle>> resultBuffers = new List<NativeArray<Particle>>();
            int currentParticle = 0;
            while (currIndex != settings.numParticles)
            {
                uint3 indexData = spatialIndicesCPU[currIndex];
                currIndex++;
                // Exit if no longer looking at correct bin
                if (indexData[2] != key) break;
                // Skip if hash does not match
                if (indexData[1] != hash) continue;

                int neighbourIndex = (int) indexData[0];
                NativeArray<int> TempArray = new NativeArray<int>(1, Allocator.TempJob);
                NativeArray<Particle> result = new NativeArray<Particle>(1, Allocator.TempJob);
                resultBuffers.Add(result);
                TempArray[0] = currentParticle;
                particleIndices.Add(TempArray);
                viscosityCalcs.Add(new CPUViscosityCalc{
                    index = particleIndices[currentParticle],
                    kernelVariables = variablesBuffer,
                    particles = particleBuffer, //add result buffer
                    spatialIndices = spatialIndicesBuffer,
                    spatialOffsets = spatialOffsetsBuffer,
                    result = resultBuffers[currentParticle]
                });

                jobHandles.Add(viscosityCalcs[currentParticle].Schedule());
                currentParticle++;
            }

            for (int j=0; j<currentParticle; j++){
            jobHandles[j].Complete();
            
            }
            for (int j=0; j<currentParticle; j++){
            particleBuffer[viscosityCalcs[j].index[0]] = viscosityCalcs[j].result[0];
            
            }
            viscosityCalcs = null;
            jobHandles = null;
            for(int j=0; j<currentParticle; j++){
                particleIndices[j].Dispose();
                resultBuffers[j].Dispose();
            }
        }


        particleBuffer.CopyTo(particleData);

        particleBuffer.Dispose();
        boxBuffer.Dispose();
        variablesBuffer.Dispose();
        spatialIndicesBuffer.Dispose();
        spatialOffsetsBuffer.Dispose();
        CPUOriginCellBuffer.Dispose();
        for(int i=0; i<settings.numCPUCells; i++)
        {
            cellBuffers[i].Dispose();
        }
        cellBuffers = null;
        return 0;
    }
}
