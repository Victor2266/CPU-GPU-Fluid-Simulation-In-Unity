using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

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

	public GPUCountSort(ComputeBuffer keyBuffer, ComputeBuffer valueBuffer, uint maxValue)
	{
		int count = keyBuffer.count;
		cs = ComputeHelper.LoadComputeShader("CountSort");

		sortedKBuffer = ComputeHelper.CreateStructuredBuffer<uint3>(count);
		sortedVBuffer = ComputeHelper.CreateStructuredBuffer<uint>(count);
		cntBuffer = ComputeHelper.CreateStructuredBuffer<uint>( (int) maxValue + 1 );

		ComputeHelper.SetBuffer(cs, keyBuffer, "InputKeys", CountKernel, ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, valueBuffer, "InputValues", ScatterOutputsKernel, CopyBackKernel);

		ComputeHelper.SetBuffer(cs, sortedKBuffer, "SortedKeys", ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, sortedVBuffer, "SortedValues", ScatterOutputsKernel, CopyBackKernel);
		ComputeHelper.SetBuffer(cs, cntBuffer, "Counts", ClearCountsKernel, CountKernel, ScatterOutputsKernel);
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