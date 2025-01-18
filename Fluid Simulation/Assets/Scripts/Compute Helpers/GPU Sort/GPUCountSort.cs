using UnityEngine;
using Unity.Mathematics;

// Counting sort dispatcher
public class GPUCountSort
{
	readonly Scan scan = new();
	readonly ComputeShader cs;

	readonly ComputeBuffer sortedKBuffer;
	readonly ComputeBuffer sortedVBuffer;
	readonly ComputeBuffer cntBuffer;

	const int ClearCountsKernel = 0;
	const int CountKernel = 1;
	const int ScatterOutputsKernel = 2;
	const int CopyBackKernel = 3;

	// Sorts a buffer of keys based on a buffer of values (note that value buffer will also be sorted in the process).
	// keyArr stores keys sorted by population is descending order
	public GPUCountSort(ComputeBuffer keyBuffer, ComputeBuffer valueBuffer, uint maxValue, ComputeBuffer keyArr)
	{
		int count = keyBuffer.count;
		cs = ComputeHelper.LoadComputeShader("CountSort");

		sortedKBuffer = ComputeHelper.CreateStructuredBuffer<uint3>(count);
		sortedVBuffer = ComputeHelper.CreateStructuredBuffer<uint>(count);
		cntBuffer = ComputeHelper.CreateStructuredBuffer<uint>( (int) maxValue + 1 );

		// Input buffers
		ComputeHelper.SetBuffer(cs, keyBuffer, "InputKeys", CountKernel, ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, valueBuffer, "InputValues", ScatterOutputsKernel, CopyBackKernel);

		// Outputs + internal counts
		ComputeHelper.SetBuffer(cs, sortedKBuffer, "SortedKeys", ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, sortedVBuffer, "SortedValues", ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, cntBuffer, "Counts", ClearCountsKernel, CountKernel, ScatterOutputsKernel);

		ComputeHelper.SetBuffer(cs, keyArr, "KeyArr", ScatterOutputsKernel, CopyBackKernel);

		cs.SetInt("numInputs", count);
	}

	public void Run()
	{
		int count = sortedKBuffer.count;

		ComputeHelper.Dispatch(cs, count, kernelIndex: ClearCountsKernel);
		ComputeHelper.Dispatch(cs, count, kernelIndex: CountKernel);

		scan.Run(cntBuffer);
		ComputeHelper.Dispatch(cs, count, kernelIndex: ScatterOutputsKernel);
		ComputeHelper.Dispatch(cs, count, kernelIndex: CopyBackKernel);
	}

	public void Release()
	{
		ComputeHelper.Release(sortedKBuffer, sortedVBuffer, cntBuffer);
		scan.Release();
	}
}