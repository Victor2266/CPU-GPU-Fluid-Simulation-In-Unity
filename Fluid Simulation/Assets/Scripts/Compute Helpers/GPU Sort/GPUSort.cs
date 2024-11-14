using UnityEngine;
using static UnityEngine.Mathf;

public class GPUSort
{
    const int sortKernel = 0;
    const int calculateOffsetsKernel = 1;

    readonly ComputeShader sortCompute;
    ComputeBuffer indexBuffer;

    ComputeBuffer mostParticlesBuffer = ComputeHelper.CreateStructuredBuffer<uint>(1); 
    public uint[] mostParticlesLocation = {0};
    public GPUSort()
    {
        sortCompute = ComputeHelper.LoadComputeShader("BitonicMergeSort");
    }

    public void SetBuffers(ComputeBuffer indexBuffer, ComputeBuffer offsetBuffer)
    {
        this.indexBuffer = indexBuffer;
        mostParticlesLocation[0] = 0;
        sortCompute.SetBuffer(sortKernel, "Entries", indexBuffer);
        ComputeHelper.SetBuffer(sortCompute, offsetBuffer, "Offsets", calculateOffsetsKernel);
        ComputeHelper.SetBuffer(sortCompute, indexBuffer, "Entries", calculateOffsetsKernel);
        ComputeHelper.SetBuffer(sortCompute, mostParticlesBuffer, "MostParticles", calculateOffsetsKernel);
        mostParticlesBuffer.SetData(mostParticlesLocation);
    }

    // Sorts given buffer of integer values using bitonic merge sort
    // Note: buffer size is not restricted to powers of 2 in this implementation
    public void Sort()
    {
        sortCompute.SetInt("numEntries", indexBuffer.count);

        // Launch each step of the sorting algorithm (once the previous step is complete)
        // Number of steps = [log2(n) * (log2(n) + 1)] / 2
        // where n = nearest power of 2 that is greater or equal to the number of inputs
        int numStages = (int)Log(NextPowerOfTwo(indexBuffer.count), 2);

        for (int stageIndex = 0; stageIndex < numStages; stageIndex++)
        {
            for (int stepIndex = 0; stepIndex < stageIndex + 1; stepIndex++)
            {
                // Calculate some pattern stuff
                int groupWidth = 1 << (stageIndex - stepIndex);
                int groupHeight = 2 * groupWidth - 1;
                sortCompute.SetInt("groupWidth", groupWidth);
                sortCompute.SetInt("groupHeight", groupHeight);
                sortCompute.SetInt("stepIndex", stepIndex);
                // Run the sorting step on the GPU
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBuffer.count) / 2);
            }
        }
    }


    public void SortAndCalculateOffsets()
    {
        Sort();
        mostParticlesLocation[0] = 0;
        mostParticlesBuffer.SetData(mostParticlesLocation);
        ComputeHelper.Dispatch(sortCompute, indexBuffer.count, kernelIndex: calculateOffsetsKernel);
        mostParticlesBuffer.GetData(mostParticlesLocation);
        Debug.Log("mostParticlesCellSize = " + mostParticlesLocation[0]);
    }

}