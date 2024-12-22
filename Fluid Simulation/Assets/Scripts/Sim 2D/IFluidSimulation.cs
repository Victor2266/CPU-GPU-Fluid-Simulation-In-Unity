using UnityEngine;

public interface IFluidSimulation
{
    /// <summary>
    /// Sets the fluid properties for the simulation using the provided FluidData.
    /// </summary>
    /// <param name="fluidData">The fluid data containing properties to be applied to the simulation.</param>
    void SetFluidProperties(FluidData fluidData);

    /// <summary>
    /// Sets the brush type for particle interaction.
    /// </summary>
    /// <param name="brushTypeIndex">The index corresponding to the desired brush type.</param>
    void SetBrushType(int brushTypeIndex);

    /// <summary>
    /// Toggles the pause state of the simulation.
    /// </summary>
    void togglePause();

    /// <summary>
    /// Gets the current pause state of the simulation.
    /// </summary>
    /// <returns>True if the simulation is paused, false otherwise.</returns>
    bool getPaused();

    /// <summary>
    /// Steps the simulation forward by one frame.
    /// </summary>
    void stepSimulation();

    /// <summary>
    /// Resets the simulation to its initial state.
    /// </summary>
    void resetSimulation();

    // Methods for fluid detection
    bool IsPositionBufferValid(); // Check if position buffer exists and is valid
    Vector2[] GetParticlePositions(); // Get current particle positions
    int GetParticleCount(); // Get total number of particles
}