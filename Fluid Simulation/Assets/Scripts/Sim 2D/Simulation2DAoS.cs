using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine.UIElements;
using Unity.VisualScripting.FullSerializer;

//Defining Structs
[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 40)]
public struct Particle // 40 bytes total
{
    public float2 density; //8 bytes, density and near density
    public Vector2 velocity; //8 bytes
    public Vector2 predictedPosition; // 8
    public Vector2 position; // 8
    public float temperature; // 4
    public FluidType type; // 4 (enum is int by default)
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct Circle //12 bytes total
{
    public Vector2 pos; //8 bytes
    public float radius; //4 bytes
}

[System.Serializable]
[StructLayout(LayoutKind.Sequential, Size = 24)]
public struct OrientedBox //24 bytes total
{
    public Vector2 pos; //8 bytes
    public Vector2 size;
    public Vector2 zLocal;
};


public class Simulation2DAoS : MonoBehaviour
{
    public event System.Action SimulationStepCompleted;

    [Header("Simulation Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep;
    public int iterationsPerFrame;

    public Vector2 boundsSize;
    public Vector2 obstacleSize;
    public Vector2 obstacleCentre;

    [Header("Fluid Settings")]
    public float gravity;
    [Range(0, 1)] public float collisionDamping = 0.95f;
    public float smoothingRadius = 2;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;

    [Header("Interaction Settings")]
    public float interactionRadius;
    public float interactionStrength;

    // Brush Settings + Enum type
    public enum BrushType
    {
        DRAW,
        GRAVITY
    }
    [Header("Brush Type")]

    [SerializeField] private BrushType brushState = BrushType.GRAVITY;

    [Header("Current Fluid Type")]
    [SerializeField] private FluidData currentFluid;

    [Header("References")]
    public ComputeShader compute;
    public ParticleSpawner spawner;
    public ParticleDisplay2D display;

    [Header("Obstacle Colliders")]
    public Transform[] boxColliders;
    public Transform[] circleColliders;

    private ComputeBuffer boxCollidersBuffer;
    private ComputeBuffer circleCollidersBuffer;
    private OrientedBox[] boxColliderData;
    private Circle[] circleColliderData;
    private const int MAX_COLLIDERS = 64; // Set a reasonable maximum number of colliders

    [Header("Particle Data")]
    // Buffers
    /*public ComputeBuffer positionBuffer { get; private set; }   //These are replaced by struct buffers
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer; */
    public Particle[] particleData;
    public ComputeBuffer particleBuffer { get; private set; }
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;
    GPUSort gpuSort;

    // Kernel IDs
    const int externalForcesKernel = 0;
    const int spatialHashKernel = 1;
    const int densityKernel = 2;
    const int pressureKernel = 3;
    const int viscosityKernel = 4;
    const int updatePositionKernel = 5;

    //CPUKERNEL
    public CPUParticleKernel CPUKernel;
    // State
    bool isPaused;
    ParticleSpawner.ParticleSpawnData spawnData;
    bool pauseNextFrame;

    public int numParticles { get; private set; }

    void Start()
    {
        //initialized CPUKernel
        CPUKernel = new CPUParticleKernel();
        Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");

        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;

        spawnData = spawner.GetSpawnData();
        numParticles = spawnData.positions.Length;

        // Create buffers
        /*positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);            //These are replaced by struct buffers
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);*/
        particleData = new Particle[numParticles];
        particleBuffer = ComputeHelper.CreateStructuredBuffer<Particle>(numParticles);
        
        boxColliderData = new OrientedBox[MAX_COLLIDERS];
        circleColliderData = new Circle[MAX_COLLIDERS];

        boxCollidersBuffer = ComputeHelper.CreateStructuredBuffer<OrientedBox>(MAX_COLLIDERS);
        circleCollidersBuffer = ComputeHelper.CreateStructuredBuffer<Circle>(MAX_COLLIDERS);
        
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);

        
        // Set buffer data
        SetInitialBufferData(spawnData);

        // Init compute
        ComputeHelper.SetBuffer(compute, particleBuffer, "Particles", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel);
        
        //ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel);
        //ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        //ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
        //ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, boxCollidersBuffer, "BoxColliders", externalForcesKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, circleCollidersBuffer, "CircleColliders", externalForcesKernel, updatePositionKernel);

        compute.SetInt("numBoxColliders", boxColliders.Length);
        compute.SetInt("numCircleColliders", circleColliders.Length);
        compute.SetInt("numParticles", numParticles);

        gpuSort = new();
        gpuSort.SetBuffers(spatialIndices, spatialOffsets);


        // Init display
        display.InitAoS(this);
    }

    void FixedUpdate()
    {
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        // Run simulation if not in fixed timestep mode
        // (skip running for first few frames as deltaTime can be disproportionaly large)
        if (!fixedTimeStep && Time.frameCount > 10)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }

        UpdateColliderData();
        HandleInput();
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!isPaused)
        {
            float timeStep = frameTime / iterationsPerFrame * timeScale;

            UpdateSettings(timeStep);

            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }
    }

    void RunSimulationStep()
    {
        
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);

        //CPUKERNEL
        //simulation is setup to run all particles on CPU for testing purposes. can be commented out for normal use
        gpuSort.SortAndCalculateOffsets();
        particleBuffer.GetData(CPUKernel.particleData);
        Debug.Log("Pass");
        Debug.Log(CPUKernel.particleData.Length);
        boxCollidersBuffer.GetData(CPUKernel.boxCollidersData);
        circleCollidersBuffer.GetData(CPUKernel.circleCollidersData);
        spatialIndices.GetData(CPUKernel.spatialIndicesCPU);
        spatialOffsets.GetData(CPUKernel.spatialOffsetsCPU);
        SpatialHashCPU sHash = new SpatialHashCPU();
        for(int i=0; i< numParticles; i++){
            CPUKernel.CPUOriginCells[i] = sHash.GetCell2D(CPUKernel.particleData[i].position, smoothingRadius);
        }
        int result = CPUKernel.ExecuteCPUComputeB();

        particleBuffer.SetData(CPUKernel.particleData);
        
        for(int i=0; i<numParticles; i++){
            particleData[i] = CPUKernel.particleData[i];
        }

        //uncomment some of these to get normal operation

        //ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
        //compute the pressure and viscosity on CPU
        //ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
        //ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);

    }

    void UpdateColliderData()
    {
        // Update box colliders
        for (int i = 0; i < boxColliders.Length && i < MAX_COLLIDERS; i++)
        {
            Transform collider = boxColliders[i];
            boxColliderData[i] = new OrientedBox
            {
                pos = collider.position,
                size = collider.localScale,
                zLocal = (Vector2)(collider.right) // Use right vector for orientation
            };
        }

        // Update circle colliders
        for (int i = 0; i < circleColliders.Length && i < MAX_COLLIDERS; i++)
        {
            Transform collider = circleColliders[i];
            circleColliderData[i] = new Circle
            {
                pos = collider.position,
                radius = collider.localScale.x * 0.5f // Assuming uniform scale
            };
        }

        // Update buffers
        boxCollidersBuffer.SetData(boxColliderData);
        circleCollidersBuffer.SetData(circleColliderData);
    }

    void UpdateSettings(float deltaTime)
    {
        
        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetVector("boundsSize", boundsSize);
        compute.SetInt("numBoxColliders", boxColliders.Length);
        compute.SetInt("numCircleColliders", circleColliders.Length);

        compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
        compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
        compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

        // Mouse interaction settings:
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        bool isPullInteraction = Input.GetMouseButton(0);
        bool isPushInteraction = Input.GetMouseButton(1);
        float currInteractStrength = 0;
        if (isPushInteraction || isPullInteraction)
        {
            if(brushState == BrushType.GRAVITY)
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
        }

        compute.SetVector("interactionInputPoint", mousePos);
        compute.SetFloat("interactionInputStrength", currInteractStrength);
        compute.SetFloat("interactionInputRadius", interactionRadius);

        //CPUKERNEL
        //creates settings struct to be sent to kernel, this is updated every update cycle same as GPU.
        kernelSettings newSettings = new kernelSettings();
        newSettings.deltaTime = Time.fixedDeltaTime;
        newSettings.gravity = gravity;
        newSettings.collisionDamping = collisionDamping;
        newSettings.smoothingRadius = smoothingRadius;
        newSettings.targetDensity = targetDensity;
        newSettings.pressureMultiplier = pressureMultiplier;
        newSettings.nearPressureMultiplier = nearPressureMultiplier;
        newSettings.viscosityStrength = viscosityStrength;
        newSettings.boundsSize = boundsSize;
        newSettings.numBoxColliders = (uint) boxColliders.Length;
        newSettings.numCircleColliders = (uint) circleColliders.Length;
        newSettings.interactionInputPoint = 0;
        newSettings.interactionInputStrength = 0;
        newSettings.interactionInputRadius = interactionRadius;
        newSettings.numCPUCells = (uint) numParticles;
        newSettings.numParticles = (uint) numParticles;
        newSettings.MAX_COLLIDERS = MAX_COLLIDERS;
        CPUKernel.settings = newSettings;
        CPUKernel.InitializeCPUKernelSettings();

    }

    void SetInitialBufferData(ParticleSpawner.ParticleSpawnData spawnData)
    {
        //float2[] allPoints = new float2[spawnData.positions.Length];
        Particle[] allPoints = new Particle[spawnData.positions.Length];

        //System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        // FIXME defaulting some values
        for (int i = 0; i < spawnData.positions.Length; i++)
        {
            Particle p = new Particle {
                position = spawnData.positions[i], 
                predictedPosition = spawnData.positions[i], 
                velocity = spawnData.velocities[i],
                density = new float2(0, 0),
                temperature = 0,
                type = FluidType.Water // Or whatever default type you want};
            };
            allPoints[i] = p;
        }

        //positionBuffer.SetData(allPoints);
        //predictedPositionBuffer.SetData(allPoints);
        //velocityBuffer.SetData(spawnData.velocities);
        particleBuffer.SetData(allPoints);
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            pauseNextFrame = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
            // Reset positions, the run single frame to get density etc (for debug purposes) and then reset positions again
            SetInitialBufferData(spawnData);
            RunSimulationStep();
            SetInitialBufferData(spawnData);
        }
    }


    void OnDestroy()
    {
        ComputeHelper.Release(
            //positionBuffer, 
            //predictedPositionBuffer, 
            //velocityBuffer, 
            //densityBuffer, 
            particleBuffer,
            spatialIndices, 
            spatialOffsets,
            boxCollidersBuffer,
            circleCollidersBuffer
        );
    }


    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0, 1, 0, 0.4f);
        Gizmos.DrawWireCube(Vector2.zero, boundsSize);
        
        // Draw all box colliders
        if (boxColliders != null)
        {
            foreach (Transform boxCollider in boxColliders)
            {
                if (boxCollider != null)
                {
                    Gizmos.DrawWireCube(boxCollider.position, boxCollider.localScale);
                }
            }
        }

        // Draw all circle colliders
        if (circleColliders != null)
        {
            foreach (Transform circleCollider in circleColliders)
            {
                if (circleCollider != null)
                {
                    Gizmos.DrawWireSphere(circleCollider.position, circleCollider.localScale.x * 0.5f);
                }
            }
        }

        if (Application.isPlaying)
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            bool isInteracting = isPullInteraction || isPushInteraction;
            if (isInteracting)
            {
                Gizmos.color = isPullInteraction ? Color.green : Color.red;
                Gizmos.DrawWireSphere(mousePos, interactionRadius);
            }
        }

    }

    public void SetFluidProperties(FluidData fluidData)
    {
        if (fluidData == null)
        {
            Debug.LogError("Attempted to set null FluidData");
            return;
        }

        currentFluid = fluidData;

        // Update simulation parameters with the new fluid's properties
        gravity = fluidData.gravity;
        collisionDamping = fluidData.collisionDamping;
        smoothingRadius = fluidData.smoothingRadius;
        targetDensity = fluidData.targetDensity;
        pressureMultiplier = fluidData.pressureMultiplier;
        nearPressureMultiplier = fluidData.nearPressureMultiplier;
        viscosityStrength = fluidData.viscosityStrength;
    }

    public void SetBrushType(int brushTypeIndex)
    {
        brushState = (BrushType)brushTypeIndex;
    }
}
