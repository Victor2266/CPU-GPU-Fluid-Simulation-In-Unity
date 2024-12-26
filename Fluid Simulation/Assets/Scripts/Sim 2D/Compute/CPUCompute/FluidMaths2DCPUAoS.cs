using Unity.Mathematics;
using System;
using UnityEngine;
using Unity.VisualScripting;

public class FluidMaths2DCPUAoS{

    public float smoothingRadius;
    public float Poly6ScalingFactor;
    public float SpikyPow3ScalingFactor;
    public float SpikyPow2ScalingFactor;
    public float SpikyPow3DerivativeScalingFactor;
    public float SpikyPow2DerivativeScalingFactor;

    public void setSmoothingRadius(float rad){
        smoothingRadius = rad;
        Poly6ScalingFactor = 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8));
        SpikyPow3ScalingFactor = 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5));
        SpikyPow2ScalingFactor = 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4));
        SpikyPow3DerivativeScalingFactor = 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI);
        SpikyPow2DerivativeScalingFactor = 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI);
    }
    public float SmoothingKernelPoly6(float dst, float radius)
    {
    	if (dst < radius)
    	{
    		float v = radius * radius - dst * dst;
    		return v * v * v * Poly6ScalingFactor;
    	}
    	return 0;
    }

    public float SpikyKernelPow3(float dst, float radius)
    {
    	if (dst < radius)
    	{
    		float v = radius - dst;
    		return v * v * v * SpikyPow3ScalingFactor;
    	}
    	return 0;
    }

    public float SpikyKernelPow2(float dst, float radius)
    {
    	if (dst < radius)
    	{
    		float v = radius - dst;
    		return v * v * SpikyPow2ScalingFactor;
    	}
    	return 0;
    }

    public float DerivativeSpikyPow3(float dst, float radius)
    {
    	if (dst <= radius)
    	{
    		float v = radius - dst;
    		return -v * v * SpikyPow3DerivativeScalingFactor;
    	}
    	return 0;
    }

    public float DerivativeSpikyPow2(float dst, float radius)
    {
    	if (dst <= radius)
    	{
    		float v = radius - dst;
    		return -v * SpikyPow2DerivativeScalingFactor;
    	}
    	return 0;
    }

	public float Dot(float2 A, float2 B){

		return (A.x * B.x) + (A.y * B.y);
	}
}