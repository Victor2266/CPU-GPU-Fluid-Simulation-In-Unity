using System.Runtime.InteropServices;
using UnityEngine;

public enum FluidType
{
    Disabled,
    Water,
    Steam,
    Honey,
    Lava
}

public enum VisualStyle
{
    VelocityBased,
    Temperature,
    Glowing,
    Fuzzy
}

// Struct for passing to compute shader.
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 36)]
public struct FluidParam
{
    public FluidType fluidType;
    public float gravity;
    public float collisionDamping;
    public float smoothingRadius;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;
    public float startTemperature;
    public float thermalDiffusivity;
};

// These are calculated once based on the smoothing radius of each fluid
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 20)]
public struct ScalingFactors
{
	public float Poly6;
	public float SpikyPow3;
	public float SpikyPow2;
	public float SpikyPow3Derivative;
	public float SpikyPow2Derivative;
};

[System.Serializable]
public struct VisualParameters
{
    public VisualStyle style;
    public Gradient colorGradient;
    public float visualScale;
    
    // General parameters
    public float baseOpacity;
    public float noiseScale;
    public float timeScale;
    
    // Glow parameters
    public float glowIntensity;
    public float glowFalloff;  // Added glow falloff parameter
    public float minPropertyValue; // For temperature mapping
    public float maxPropertyValue;
    
}

[CreateAssetMenu(fileName = "New Fluid", menuName = "Fluids/New Fluid Type")]
public class FluidData : ScriptableObject
{
    [Header("Basic Properties")]
    [Tooltip("Type of fluid")]
    public FluidType fluidType = FluidType.Water;

    [Tooltip("Gravity force applied to the fluid")]
    public float gravity = -9.81f;

    [Tooltip("Damping applied when fluid collides with surfaces")]
    [Range(0f, 1f)]
    public float collisionDamping = 0.95f;

    [Header("Particle Properties")]
    [Tooltip("Radius within which particles interact")]
    public float smoothingRadius = 0.35f;

    [Tooltip("Target density for the fluid")]
    public float targetDensity = 55f;

    [Header("Pressure Settings")]
    [Tooltip("Multiplier for pressure calculation")]
    public float pressureMultiplier = 500f;

    [Tooltip("Multiplier for near-field pressure calculation")]
    public float nearPressureMultiplier = 18f;

    [Header("Viscosity")]
    [Tooltip("Strength of the fluid's viscosity")]
    [Range(0f, 3f)]
    public float viscosityStrength = 0.06f;

    [Header("Thermal Properties")]
    [Tooltip("Starting temperature of the fluid")]
    public float startTemperature = 22f;
    public float thermalDiffusivity = 0.143f;

    [Header("Visual Properties")]
    [Tooltip("Setup the look of the fluid")]
    public VisualParameters visualParams;

    // Validation method to ensure values stay within reasonable bounds
    private void OnValidate()
    {
        smoothingRadius = Mathf.Max(0.001f, smoothingRadius);
        targetDensity = Mathf.Max(0.001f, targetDensity);
        pressureMultiplier = Mathf.Max(0f, pressureMultiplier);
        nearPressureMultiplier = Mathf.Max(0f, nearPressureMultiplier);
    }

    // Returns a compressed, compute-friendly copy of this instance as a FluidParam struct
    public FluidParam getFluidParams()
    {
        FluidParam fluidParams = new FluidParam
        {
            fluidType = fluidType,
            gravity = gravity,
            collisionDamping = collisionDamping,
            smoothingRadius = smoothingRadius,
            targetDensity = targetDensity,
            pressureMultiplier = pressureMultiplier,
            nearPressureMultiplier = nearPressureMultiplier,
            viscosityStrength = viscosityStrength,
            startTemperature = startTemperature,
            thermalDiffusivity = thermalDiffusivity
        };
        return fluidParams;
    }
    public ScalingFactors getScalingFactors()
    {
        ScalingFactors scalingFactors = new ScalingFactors
        {
            Poly6 = 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)),
            SpikyPow3 = 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)),
            SpikyPow2 = 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)),
            SpikyPow3Derivative = 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI),
            SpikyPow2Derivative = 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI)
        };
        return scalingFactors;
    }

    // Struct for passing visual data to compute shader
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VisualParamBuffer
    {
        public int visualStyle;
        public float visualScale;
        public float baseOpacity;
        public float noiseScale;
        public float timeScale;
        public float glowIntensity;
        public float glowFalloff; 
        public float minValue;
        public float maxValue;
    }

    public VisualParamBuffer GetVisualParams()
    {
        return new VisualParamBuffer
        {
            visualStyle = (int)visualParams.style,
            visualScale = visualParams.visualScale,
            baseOpacity = visualParams.baseOpacity,
            noiseScale = visualParams.noiseScale,
            timeScale = visualParams.timeScale,
            glowIntensity = visualParams.glowIntensity,
            glowFalloff = visualParams.glowFalloff,
            minValue = visualParams.minPropertyValue,
            maxValue = visualParams.maxPropertyValue
        };
    }

};
