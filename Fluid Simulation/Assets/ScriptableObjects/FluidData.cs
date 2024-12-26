using UnityEngine;

public enum FluidType
{
    Disabled,
    Water,
    Steam,
    Honey
}

// Struct for passing to compute shader
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
}

[CreateAssetMenu(fileName = "New Fluid", menuName = "Fluids/New Fluid Type")]
public class FluidData : ScriptableObject
{
    [Header("Basic Properties")]
    [Tooltip("Type of fluid")]
    public FluidType fluidType;

    [Tooltip("Gravity force applied to the fluid")]
    public float gravity = 9.81f;

    [Tooltip("Damping applied when fluid collides with surfaces")]
    [Range(0f, 1f)]
    public float collisionDamping = 0.5f;

    [Header("Particle Properties")]
    [Tooltip("Radius within which particles interact")]
    public float smoothingRadius = 1f;

    [Tooltip("Target density for the fluid")]
    public float targetDensity = 1000f;

    [Header("Pressure Settings")]
    [Tooltip("Multiplier for pressure calculation")]
    public float pressureMultiplier = 1f;

    [Tooltip("Multiplier for near-field pressure calculation")]
    public float nearPressureMultiplier = 1f;

    [Header("Viscosity")]
    [Tooltip("Strength of the fluid's viscosity")]
    [Range(0f, 1f)]
    public float viscosityStrength = 0.5f;


    [Header("Shader Properties")]
    [Tooltip("What Type of Shader to use")]
    public Shader shader;
    public float scale;
	public Gradient colourMap;
	public int gradientResolution;
	public float velocityDisplayMax;

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
        FluidParam fluidParams = new FluidParam();
        fluidParams.gravity = this.gravity;
        fluidParams.collisionDamping = this.collisionDamping;
        fluidParams.smoothingRadius = this.smoothingRadius;
        fluidParams.targetDensity = this.targetDensity;
        fluidParams.pressureMultiplier = this.pressureMultiplier;
        fluidParams.nearPressureMultiplier = this.nearPressureMultiplier;
        fluidParams.viscosityStrength = this.viscosityStrength;
        return fluidParams;
    }
}