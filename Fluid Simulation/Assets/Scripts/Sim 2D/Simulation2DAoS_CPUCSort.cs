using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using UnityEngine.UIElements;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.SceneManagement;


public class Simulation2DAoS_CPUCSort : MonoBehaviour, IFluidSimulation
{
    public event System.Action SimulationStepCompleted;

    [Header("Simulation Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep; // Enable for consistent simulation steps across different framerates, (limits smoothness to 120fps)
    public bool enableHotkeys = false;
    public int iterationsPerFrame;

    public Vector2 boundsSize;
    public Vector2 obstacleSize;
    public Vector2 obstacleCentre;

    public enum EdgeType
    {
        Solid,
        Void,
        Loop
    }
    [SerializeField] private EdgeType edgeType = EdgeType.Solid;
    public uint spawnRate = 100; // How many particles that can spawn per frame

    [Header("Selected Fluid Type")] // This is used for the draw brush
    [SerializeField] private int selectedFluid;

    // Brush Settings + Enum type
    public enum BrushType
    {
        DRAW,
        GRAVITY,
        NOTHING
    }

    [Header("Brush Type")]

    [SerializeField] private BrushType brushState = BrushType.GRAVITY;

    [Header("Interaction Settings")]
    public float interactionRadius;
    public float interactionStrength;
    public float minRadius = 0.25f;
    public float maxRadius = 24f;
    public float smoothingTime = 0.04f;
    public bool enableScrolling = false;
    private float targetInteractionRadius;
    private float smoothVelocity;



    // Fluid data array and buffer (to serialize then pass to GPU)
    [Header("Fluid Data Types")]
    // For the spatial subdivision to work we use the largest smoothing radius for the grid
    // By manually selecting the fluid types you can finetune the grid size
    [SerializeField] private bool manuallySelectFluidTypes;
    private float maxSmoothingRadius = 0f;
    [SerializeField] public FluidData[] fluidDataArray;
    private FluidParam[] fluidParamArr; // Compute-friendly data type
    public ComputeBuffer fluidDataBuffer { get; private set; }

    private ScalingFactors[] scalingFactorsArr;
    private ComputeBuffer ScalingFactorsBuffer;

    [Header("References")]
    public ComputeShader compute;
    public ParticleSpawner spawner;
    public IParticleDisplay display;

    [Header("Obstacle Colliders")]
    public Transform[] boxColliders;
    public Transform[] circleColliders;
    private ComputeBuffer boxCollidersBuffer;
    private ComputeBuffer circleCollidersBuffer;

    [Header("Source and Drain Objects")]
    public SourceObjectInitializer[] sourceObjects;
    public Transform[] drainObjects;
    private ComputeBuffer sourceObjectBuffer;
    private ComputeBuffer drainObjectBuffer;

    // Counter Variables
    private ComputeBuffer atomicCounterBuffer;
    private uint uintCounter;

    // Private Variables 
    private OrientedBox[] boxColliderData;
    private Circle[] circleColliderData;

    private SourceObject[] sourceObjectData;
    private OrientedBox[] drainObjectData;

    [Header("Particle Data")]
    // Buffers
    private Particle[] particleData;
    public ComputeBuffer particleBuffer { get; private set; }
    public ComputeBuffer sortedParticleBuffer { get; private set; }
    public ComputeBuffer sortedIndices { get; private set; }
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;
    GPUCountSort gpuSort; // Class adjust for sorting algorithm
    SpatialOffsetCalculator spatialOffsetsCalc;

    // Kernel IDs
    const int SpawnParticlesKernel = 0;
    const int externalForcesKernel = 1;
    const int spatialHashKernel = 2;
    const int reorderKernel = 3;
    const int reorderCopybackKernel = 4;
    const int densityKernel = 5;
    const int pressureKernel = 6;
    const int viscosityKernel = 7;
    const int updatePositionKernel = 8;
    const int mergeCPUParticlesKernel = 9; // For CPU-GPU

    // State
    bool isPaused;
    ParticleSpawner.ParticleSpawnData spawnData;
    bool pauseNextFrame;

    public int numParticles { get; private set; }

    private float accumulatedTime = 0f;
    private const float MAX_DELTA_TIME = 1f / 30f; // Maximum allowed delta time
    private const float FIXED_TIME_STEP = 1f / 120f; // Your desired fixed time step

    [Header("CPU Computing")]
    //CPU Compute

    public bool isCPUComputingEnabled = false;

    public bool toggleCPUComputing = false;

    CPUParticleKernelAoS CPUKernelAOS;

    ComputeBuffer cpuparticlebuffer;
    ComputeBuffer keyarrbuffer;
    public uint ThreadBatchSize = 50;
    public uint numCPUKeys = 10;

    public static bool reloadRequest = false;
    void Start()
    {
        // Debug.Log(System.Runtime.InteropServices.Marshal.SizeOf(typeof(SourceObjectInitializer))); //This prints the size of the typeof(struct)
        //Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");
        Debug.Log("This level is using the CPU CSort");
        CPUKernelAOS = new CPUParticleKernelAoS();
        targetInteractionRadius = interactionRadius;
        spawnData = spawner.GetSpawnData();
        numParticles = spawnData.positions.Length;

        if (!manuallySelectFluidTypes)
        {
            // Get the number of fluid types (excluding Disabled)
            int numFluidTypes = Enum.GetValues(typeof(FluidType)).Length - 1;
            // Initialize arrays
            fluidDataArray = new FluidData[numFluidTypes];
            fluidParamArr = new FluidParam[numFluidTypes];
            scalingFactorsArr = new ScalingFactors[numFluidTypes];

            // Load each fluid type in order
            for (int i = 1; i < numFluidTypes + 1; i++)
            {
                string fluidName = Enum.GetName(typeof(FluidType), i);
                FluidData fluidData = Resources.Load<FluidData>($"Fluids/{fluidName}");
                fluidData.fluidType = (FluidType)i;

                if (fluidData == null)
                {
                    Debug.LogError($"Failed to load fluid data for {fluidName}. Ensure the scriptable object exists at Resources/Fluids/{fluidName}");
                    continue;
                }

                // Assign to array at index-1 (since we skip Disabled which is 0)
                fluidDataArray[i - 1] = fluidData;
                fluidParamArr[i - 1] = fluidData.getFluidParams();
                scalingFactorsArr[i - 1] = fluidData.getScalingFactors();
            }
        }
        else
        {
            fluidParamArr = new FluidParam[fluidDataArray.Length];
            scalingFactorsArr = new ScalingFactors[fluidDataArray.Length];
            for (int i = 0; i < fluidDataArray.Length; i++)
            {
                fluidParamArr[i] = fluidDataArray[i].getFluidParams();
                fluidParamArr[i].fluidType = (FluidType)i + 1;
                scalingFactorsArr[i] = fluidDataArray[i].getScalingFactors();
            }
        }

        maxSmoothingRadius = 0f;
        for (int i = 0; i < fluidDataArray.Length; i++)
        {
            if (fluidDataArray[i].smoothingRadius > maxSmoothingRadius)
            {
                maxSmoothingRadius = fluidDataArray[i].smoothingRadius;
            }
        }

        // Create buffers
        // init buffer
        fluidDataBuffer = ComputeHelper.CreateStructuredBuffer<FluidParam>(fluidDataArray.Length);
        ScalingFactorsBuffer = ComputeHelper.CreateStructuredBuffer<ScalingFactors>(fluidDataArray.Length); //why does it say this leaks?

        particleData = new Particle[numParticles];
        particleBuffer = ComputeHelper.CreateStructuredBuffer<Particle>(numParticles);
        sortedParticleBuffer = ComputeHelper.CreateStructuredBuffer<Particle>(numParticles);

        boxColliderData = new OrientedBox[boxColliders.Length];
        circleColliderData = new Circle[circleColliders.Length];
        sourceObjectData = new SourceObject[sourceObjects.Length];
        drainObjectData = new OrientedBox[drainObjects.Length];

        boxCollidersBuffer = ComputeHelper.CreateStructuredBuffer<OrientedBox>(Mathf.Max(boxColliders.Length, 1));
        circleCollidersBuffer = ComputeHelper.CreateStructuredBuffer<Circle>(Mathf.Max(circleColliders.Length, 1));
        sourceObjectBuffer = ComputeHelper.CreateStructuredBuffer<SourceObject>(Mathf.Max(sourceObjects.Length, 1));
        drainObjectBuffer = ComputeHelper.CreateStructuredBuffer<OrientedBox>(Mathf.Max(drainObjects.Length, 1));

        atomicCounterBuffer = ComputeHelper.CreateStructuredBuffer<uint>(2);


        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
        sortedIndices = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);

        // CPU Particle Buffer
        cpuparticlebuffer = ComputeHelper.CreateStructuredBuffer<Particle>(numParticles);
        keyarrbuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);

        // Set buffer data
        fluidDataBuffer.SetData(fluidParamArr);
        ScalingFactorsBuffer.SetData(scalingFactorsArr);
        SetInitialBufferData(spawnData);
        uint[] atomicCounter = { 0, uintCounter++ };
        atomicCounterBuffer.SetData(atomicCounter);

        // Init compute
        ComputeHelper.SetBuffer(compute, fluidDataBuffer, "FluidDataSet", SpawnParticlesKernel, externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, ScalingFactorsBuffer, "ScalingFactorsBuffer", densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, particleBuffer, "Particles", SpawnParticlesKernel, externalForcesKernel, reorderKernel, reorderCopybackKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, mergeCPUParticlesKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, sortedIndices, "SortedIndices", spatialHashKernel, reorderKernel, reorderCopybackKernel);
        ComputeHelper.SetBuffer(compute, sortedParticleBuffer, "SortedParticles", reorderKernel, reorderCopybackKernel);
        ComputeHelper.SetBuffer(compute, boxCollidersBuffer, "BoxColliders", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, circleCollidersBuffer, "CircleColliders", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, sourceObjectBuffer, "SourceObjs", SpawnParticlesKernel);
        ComputeHelper.SetBuffer(compute, drainObjectBuffer, "DrainObjs", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, atomicCounterBuffer, "atomicCounter", SpawnParticlesKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, cpuparticlebuffer, "CPUParticles", mergeCPUParticlesKernel);
        ComputeHelper.SetBuffer(compute, keyarrbuffer, "keyarr", densityKernel, pressureKernel, viscosityKernel, mergeCPUParticlesKernel);

        compute.SetInt("numBoxColliders", boxColliders.Length);
        compute.SetInt("numCircleColliders", circleColliders.Length);
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("maxSmoothingRadius", maxSmoothingRadius);
        compute.SetInt("spawnRate", (int) spawnRate);
        compute.SetInt("numCPUKeys", (int) numCPUKeys);

        // GPU Sort Init
        gpuSort = new GPUCountSort(spatialIndices, sortedIndices, (uint) (spatialIndices.count - 1), keyarrbuffer );
        spatialOffsetsCalc = new SpatialOffsetCalculator(spatialIndices, spatialOffsets);

        // Init display
        display = GetComponent<IParticleDisplay>();
        display.Init(this);  
    }

    void Update()
    {
        checkforReloadRequest();
        // Run simulation in fixed timestep mode
        // It will make number of simulation steps more consistent accross different frame rates
        // (it will be perfectly consistent down to 30fps)
        // ONLY ACTIVATE IF CONSISTENCY BETWEEN FRAMERATES IS IMPORTANT, non-fixed can be smoother looking above 120fps.
        // (skip running for first few frames as deltaTime can be disproportionaly large)
        if (fixedTimeStep && Time.frameCount > 10)
        {
            // Accumulate time, but cap it to prevent spiral of death
            accumulatedTime += Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
            
            // Run as many fixed updates as necessary to catch up
            // When the FPS is low then it will run more times to catch up
            // When the FPS is high then it will run less times
            while (accumulatedTime >= FIXED_TIME_STEP)
            {
                RunSimulationFrame(FIXED_TIME_STEP); // This way the simulation steps are consistent
                accumulatedTime -= FIXED_TIME_STEP;
            }
        } 
        // In variable timestep mode, the delta time can vary, which slightly effects physics consistency across framerates
        // The number of simulation steps varies depending on the framerate 
        // Tabbing out has been fixed so it won't cause issues
        // This seems to give smoother results than fixed timestep above 120fps.
        else if (!fixedTimeStep && Time.frameCount > 10)  
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }

        UpdateColliderData();

        if (enableScrolling)
            HandleScrollInput();

        if (enableHotkeys)
            HandleHotkeysInput();

        checkforReloadRequest();
    }

    void RunSimulationFrame(float frameTime)
    {
        // Cap the maximum deltaTime to prevent instability when tabbing out
        float cappedFrameTime = frameTime > 1f/30f ? 1f/30f : frameTime; // Cap at 30fps equivalent

        if (!isPaused)
        {
            float timeStep = cappedFrameTime / iterationsPerFrame * timeScale;

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
        // Init + ext forces
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: SpawnParticlesKernel);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);

        // Sort & offsets; copyback for memory coherency
        gpuSort.Run();
        spatialOffsetsCalc.Run(false);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderCopybackKernel);

        // CPU-GPU flag
        if (isCPUComputingEnabled)
        {
            if (toggleCPUComputing)
            {
                runCPUComputeTest();
            }
            else
            {
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
            }
        }
        else
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
        }

        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
    }

    void UpdateColliderData()
    {
        // Update box colliders
        for (int i = 0; i < boxColliders.Length; i++)
        {
            Transform collider = boxColliders[i];
            // Modify properties directly
            boxColliderData[i].pos = collider.position;
            boxColliderData[i].size = collider.localScale;
            boxColliderData[i].zLocal = (Vector2)(collider.right); // Use right vector for orientation
        }

        // Update circle colliders
        for (int i = 0; i < circleColliders.Length; i++)
        {
            Transform collider = circleColliders[i];
            // Modify properties directly
            circleColliderData[i].pos = collider.position;
            circleColliderData[i].radius = collider.localScale.x * 0.5f; // Assuming uniform scale
        }

        // Update source objects
        for (int i = 0; i < sourceObjects.Length; i++)
        {
            Transform source = sourceObjects[i].transform;
            // Modify properties directly
            sourceObjectData[i].pos = source.position;
            sourceObjectData[i].radius = source.localScale.x * 0.5f; // Assuming uniform scale
            sourceObjectData[i].velo = sourceObjects[i].velo;
            sourceObjectData[i].spawnRate = sourceObjects[i].spawnRate;
            sourceObjectData[i].fluidType = sourceObjects[i].fluidType;
        }

        // Update drain objects
        for (int i = 0; i < drainObjects.Length; i++)
        {
            Transform drain = drainObjects[i];
            // Modify properties directly
            drainObjectData[i].pos = drain.position;
            drainObjectData[i].size = drain.localScale;
            drainObjectData[i].zLocal = (Vector2)(drain.right); // Use right vector for orientation  
        }

        // Update buffers
        boxCollidersBuffer.SetData(boxColliderData);
        circleCollidersBuffer.SetData(circleColliderData);
        sourceObjectBuffer.SetData(sourceObjectData);
        drainObjectBuffer.SetData(drainObjectData);
    }

    void UpdateSettings(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);
        //compute.SetFloat("gravity", gravity);
        //compute.SetFloat("collisionDamping", collisionDamping);
        //compute.SetFloat("smoothingRadius", smoothingRadius);
        //compute.SetFloat("targetDensity", targetDensity);
        //compute.SetFloat("pressureMultiplier", pressureMultiplier);
        //compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        //compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetVector("boundsSize", boundsSize);
        compute.SetInt("numBoxColliders", boxColliders.Length);
        compute.SetInt("numCircleColliders", circleColliders.Length);
        compute.SetInt("numSourceObjs", sourceObjects.Length);
        compute.SetInt("numDrainObjs", drainObjects.Length);

        compute.SetInt("selectedFluidType", selectedFluid);

        compute.SetInt("edgeType", (int) edgeType);

        compute.SetInt("spawnRate", (int) spawnRate);

        //These are now computed once at the start
        /*
        compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(currentFluid.smoothingRadius, 8)));
        compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(currentFluid.smoothingRadius, 5)));
        compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(currentFluid.smoothingRadius, 4)));
        compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(currentFluid.smoothingRadius, 5) * Mathf.PI));
        compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(currentFluid.smoothingRadius, 4) * Mathf.PI));
        */

        if (sourceObjects.Length > 0)
        {
            uint[] atomicCounter = { 0, uintCounter++ };
            atomicCounterBuffer.SetData(atomicCounter);
        }

        // Mouse interaction settings:
        HandleMouseInput();

        // CPU compute keys
        if (isCPUComputingEnabled)
        {
            CPUKernelAOS.deltaTime = deltaTime;
            compute.SetInt("numCPUKeys", (int)numCPUKeys);
        }
        else
        {
            compute.SetInt("numCPUKeys", 0);
        }
    }
    void HandleScrollInput()
    {
        ApplySmoothing();

        float scrollDelta = Input.mouseScrollDelta.y;
        
        if (scrollDelta != 0)
        {
            // Apply scroll input to target radius with exponential scaling
            float scaleFactor = scrollDelta > 0 ? 1.1f : 0.9f;
            targetInteractionRadius *= Mathf.Pow(scaleFactor, Mathf.Abs(scrollDelta));
            targetInteractionRadius = Mathf.Clamp(targetInteractionRadius, minRadius, maxRadius);
        }
    }
    void ApplySmoothing()
    {
        // Smoothly interpolate to the target radius
        interactionRadius = Mathf.SmoothDamp(interactionRadius, 
            targetInteractionRadius, 
            ref smoothVelocity, 
            smoothingTime);
    }

    void HandleMouseInput()
    {
        // Mouse interaction settings:
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        bool isPullInteraction = Input.GetMouseButton(0);
        bool isPushInteraction = Input.GetMouseButton(1);
        float currInteractStrength = 0;

        if (brushState == BrushType.GRAVITY)
        {
            if (isPushInteraction || isPullInteraction)
            {
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
            }
        }
        else if (brushState == BrushType.DRAW)
        {
            if (isPullInteraction)
            {
                currInteractStrength = 1f;
                if (sourceObjects.Length == 0){
                    uint[] atomicCounter = { 0, uintCounter++ };
                    atomicCounterBuffer.SetData(atomicCounter);
                }
                
            }
            else if (isPushInteraction)
            {
                currInteractStrength = -1f;
            }
        }

        compute.SetInt("brushType", (int) brushState);
        compute.SetVector("interactionInputPoint", mousePos);
        compute.SetFloat("interactionInputStrength", currInteractStrength);
        compute.SetFloat("interactionInputRadius", interactionRadius);
    }

    void SetInitialBufferData(ParticleSpawner.ParticleSpawnData spawnData)
    {
        Particle[] allPoints = new Particle[spawnData.positions.Length];

        // FIXME defaulting some values
        for (int i = 0; i < spawnData.positions.Length; i++)
        {
            Particle p = new Particle {
                position = spawnData.positions[i],
                predictedPosition = spawnData.positions[i],
                velocity = spawnData.velocities[i],
                density = new float2(0, 0),
                temperature = 22.0f,
                type = FluidType.Water // Or whatever default type you want};
            };
            allPoints[i] = p;
        }

        particleBuffer.SetData(allPoints);
    }

    void HandleHotkeysInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            togglePause();
            GameObject sidebar = GameObject.FindGameObjectWithTag("Sidebar");
            if (sidebar != null)
            {
                SideBarWrapper sideBarWrapper = sidebar.GetComponent<SideBarWrapper>();
                if (sideBarWrapper != null)
                {
                    sideBarWrapper.UpdatePauseIcon();
                }
            }
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            stepSimulation();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            resetSimulation();
        }
    }


    void OnDestroy()
    {
        ComputeHelper.Release(
            fluidDataBuffer,
            ScalingFactorsBuffer,
            particleBuffer,
            sortedParticleBuffer,
            spatialIndices, 
            spatialOffsets,
            sortedIndices,
            boxCollidersBuffer,
            circleCollidersBuffer,
            sourceObjectBuffer,
            drainObjectBuffer,
            atomicCounterBuffer,
            cpuparticlebuffer,
            keyarrbuffer
        );

        gpuSort.Release();
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

    // These are the Interface Functions for outside game scripts:
    //
    //

    public void setEdgeType(int edgeTypeIndex){
        edgeType = (EdgeType)edgeTypeIndex;
    }

    public void setSelectedFluid(int fluidTypeIndex){
        selectedFluid = fluidTypeIndex;
    }

    public void SetBrushType(int brushTypeIndex)
    {
        brushState = (BrushType)brushTypeIndex;
    }

    public void togglePause()
    {
        isPaused = !isPaused;
    }
    public bool getPaused()
    {
        return isPaused;
    }
    public void stepSimulation()
    {
        isPaused = false;
        pauseNextFrame = true;
    }
    public void resetSimulation()
    {
        isPaused = true;
        // Reset positions, the run single frame to get density etc (for debug purposes) and then reset positions again
        SetInitialBufferData(spawnData);
        RunSimulationStep();
        SetInitialBufferData(spawnData);
    }

    // These functions are for the fluid detector
    public bool IsPositionBufferValid()
    {
        return particleBuffer != null;
    }
    public Vector2[] GetParticlePositions()
    {
        Vector2[] positions = new Vector2[numParticles];
        particleBuffer.GetData(particleData);

        for (int i = 0; i < numParticles; i++)
        {
            positions[i] = particleData[i].position;
        }
        return positions;
    }
    public int GetParticleCount()
    {
        return numParticles;
    }
    public float GetInteractionRadius()
    {
        return interactionRadius;
    }

    void initializeCPUKernelSettingsAoS()
    {
        CPUKernelAOS.numParticles = numParticles;
        CPUKernelAOS.offsets = new int2[9];

        CPUKernelAOS.offsets[0] = new int2(-1, 1);
        CPUKernelAOS.offsets[1] = new int2(0, 1);
        CPUKernelAOS.offsets[2] = new int2(1, 1);
        CPUKernelAOS.offsets[3] = new int2(-1, 0);
        CPUKernelAOS.offsets[4] = new int2(0, 0);
        CPUKernelAOS.offsets[5] = new int2(1, 0);
        CPUKernelAOS.offsets[6] = new int2(-1, -1);
        CPUKernelAOS.offsets[7] = new int2(0, -1);
        CPUKernelAOS.offsets[8] = new int2(1, -1);

        CPUKernelAOS.fluidParams = new FluidParam[Enum.GetValues(typeof(FluidType)).Length - 1];
        CPUKernelAOS.scalingFactors = new ScalingFactors[Enum.GetValues(typeof(FluidType)).Length - 1];
        CPUKernelAOS.maxSmoothingRadius = maxSmoothingRadius;
        CPUKernelAOS.boxCollidersData = new OrientedBox[boxColliders.Length];
        CPUKernelAOS.circleCollidersData = new Circle[circleColliders.Length];
        CPUKernelAOS.drainData = new OrientedBox[drainObjects.Length];
        CPUKernelAOS.sourceData = new Circle[sourceObjects.Length];
        CPUKernelAOS.spatialIndices = new uint3[numParticles];
        CPUKernelAOS.spatialOffsets = new uint[numParticles];
        CPUKernelAOS.particles = new Particle[numParticles];
        fluidParamArr.CopyTo(CPUKernelAOS.fluidParams, 0);
        scalingFactorsArr.CopyTo(CPUKernelAOS.scalingFactors, 0);
        CPUKernelAOS.keyarr = new uint[numParticles];
    }

    public void runCPUComputeTest()
    {
        //initialize local arrays
        initializeCPUKernelSettingsAoS();

        //Initialize all CPU buffers
        CPUKernelAOS.fluidParamBuffer = new NativeArray<FluidParam>(CPUKernelAOS.fluidParams.Length, Allocator.TempJob);
        CPUKernelAOS.scalingFactorsBuffer = new NativeArray<ScalingFactors>(CPUKernelAOS.scalingFactors.Length, Allocator.TempJob);
        CPUKernelAOS.spatialIndicesBuffer = new NativeArray<uint3>(numParticles, Allocator.TempJob);
        CPUKernelAOS.spatialOffsetsBuffer = new NativeArray<uint>(numParticles, Allocator.TempJob);
        CPUKernelAOS.particleBuffer = new NativeArray<Particle>(numParticles, Allocator.TempJob);
        CPUKernelAOS.offsets2DBuffer = new NativeArray<int2>(CPUKernelAOS.offsets.Length, Allocator.TempJob);
        CPUKernelAOS.particleResultBuffer = new NativeArray<Particle>(numParticles, Allocator.TempJob);
        CPUKernelAOS.keyarrbuffer = new NativeArray<uint>(numParticles, Allocator.TempJob);
        //copy data to CPU Buffers (need to figure out how to copy direct from compute buffer to nativearray)
        keyarrbuffer.GetData(CPUKernelAOS.keyarr);
        CPUKernelAOS.keyarrbuffer.CopyFrom(CPUKernelAOS.keyarr);
        CPUKernelAOS.offsets2DBuffer.CopyFrom(CPUKernelAOS.offsets);
        CPUKernelAOS.fluidParamBuffer.CopyFrom(fluidParamArr);
        CPUKernelAOS.scalingFactorsBuffer.CopyFrom(scalingFactorsArr);
        spatialIndices.GetData(CPUKernelAOS.spatialIndices);
        CPUKernelAOS.spatialIndicesBuffer.CopyFrom(CPUKernelAOS.spatialIndices);
        spatialOffsets.GetData(CPUKernelAOS.spatialOffsets);
        CPUKernelAOS.spatialOffsetsBuffer.CopyFrom(CPUKernelAOS.spatialOffsets);
        particleBuffer.GetData(CPUKernelAOS.particles);
        CPUKernelAOS.particleBuffer.CopyFrom(CPUKernelAOS.particles);
        CPUKernelAOS.particleResultBuffer.CopyFrom(CPUKernelAOS.particles);

        //create each job type and assign the buffers to the jobs
        CPUDensityCalcAoS densitycalc = new CPUDensityCalcAoS
        {
            numParticles = (uint)numParticles,
            maxSmoothingRadius = maxSmoothingRadius,
            densityOut = CPUKernelAOS.particleResultBuffer,
            particles = CPUKernelAOS.particleBuffer,
            spatialIndices = CPUKernelAOS.spatialIndicesBuffer,
            spatialOffsets = CPUKernelAOS.spatialOffsetsBuffer,
            fluidPs = CPUKernelAOS.fluidParamBuffer,
            scalingFacts = CPUKernelAOS.scalingFactorsBuffer,
            offsets2D = CPUKernelAOS.offsets2DBuffer,
            numCPUKeys = numCPUKeys,
            keyarr = CPUKernelAOS.keyarrbuffer,
        };

        CPUPressureCalcAoS pressureCalc = new CPUPressureCalcAoS
        {
            numParticles = (uint)numParticles,
            maxSmoothingRadius = maxSmoothingRadius,
            pressureOut = CPUKernelAOS.particleBuffer,
            particles = CPUKernelAOS.particleResultBuffer,
            spatialIndices = CPUKernelAOS.spatialIndicesBuffer,
            spatialOffsets = CPUKernelAOS.spatialOffsetsBuffer,
            fluidPs = CPUKernelAOS.fluidParamBuffer,
            scalingFacts = CPUKernelAOS.scalingFactorsBuffer,
            offsets2D = CPUKernelAOS.offsets2DBuffer,
            deltaTime = CPUKernelAOS.deltaTime,
            numCPUKeys = numCPUKeys,
            keyarr = CPUKernelAOS.keyarrbuffer,
        };

        CPUViscosityCalcAoS viscosityCalc = new CPUViscosityCalcAoS
        {
            numParticles = (uint)numParticles,
            maxSmoothingRadius = maxSmoothingRadius,
            viscosityOut = CPUKernelAOS.particleResultBuffer,
            particles = CPUKernelAOS.particleBuffer,
            spatialIndices = CPUKernelAOS.spatialIndicesBuffer,
            spatialOffsets = CPUKernelAOS.spatialOffsetsBuffer,
            fluidPs = CPUKernelAOS.fluidParamBuffer,
            scalingFacts = CPUKernelAOS.scalingFactorsBuffer,
            offsets2D = CPUKernelAOS.offsets2DBuffer,
            deltaTime = CPUKernelAOS.deltaTime,
            numCPUKeys = numCPUKeys,
            keyarr = CPUKernelAOS.keyarrbuffer,
        };

        //Create the threads to calculate either density, pressure, or velocity

        JobHandle density = densitycalc.Schedule(numParticles, (int)ThreadBatchSize);
        density.Complete();
        ComputeHelper.Dispatch(compute, numParticles + 1, kernelIndex: densityKernel);

        //data transfers required to merge CPU and GPU data
        CPUKernelAOS.particleResultBuffer.CopyTo(CPUKernelAOS.particles);
        cpuparticlebuffer.SetData(CPUKernelAOS.particles);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: mergeCPUParticlesKernel);
        particleBuffer.GetData(CPUKernelAOS.particles);
        CPUKernelAOS.particleResultBuffer.CopyFrom(CPUKernelAOS.particles);
        CPUKernelAOS.particleBuffer.CopyFrom(CPUKernelAOS.particles);

        JobHandle pressure = pressureCalc.Schedule(numParticles, (int)ThreadBatchSize);
        pressure.Complete();
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
        CPUKernelAOS.particleResultBuffer.CopyTo(CPUKernelAOS.particles);
        cpuparticlebuffer.SetData(CPUKernelAOS.particles);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: mergeCPUParticlesKernel);
        particleBuffer.GetData(CPUKernelAOS.particles);
        CPUKernelAOS.particleResultBuffer.CopyFrom(CPUKernelAOS.particles);
        CPUKernelAOS.particleBuffer.CopyFrom(CPUKernelAOS.particles);

        JobHandle viscosity = viscosityCalc.Schedule(numParticles, (int)ThreadBatchSize);
        viscosity.Complete();
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);

        CPUKernelAOS.particleResultBuffer.CopyTo(CPUKernelAOS.particles);
        cpuparticlebuffer.SetData(CPUKernelAOS.particles);
        ComputeHelper.Dispatch(compute, numParticles, kernelIndex: mergeCPUParticlesKernel);
        particleBuffer.GetData(CPUKernelAOS.particles);
        CPUKernelAOS.particleResultBuffer.CopyFrom(CPUKernelAOS.particles);
        CPUKernelAOS.particleBuffer.CopyFrom(CPUKernelAOS.particles);

        //Debug.Log("CPUComputeCompleted");
        CPUKernelAOS.particleResultBuffer.CopyTo(CPUKernelAOS.particles);
        particleBuffer.SetData(CPUKernelAOS.particles);

        CPUKernelAOS.fluidParamBuffer.Dispose();
        CPUKernelAOS.scalingFactorsBuffer.Dispose();
        CPUKernelAOS.spatialIndicesBuffer.Dispose();
        CPUKernelAOS.spatialOffsetsBuffer.Dispose();
        CPUKernelAOS.particleBuffer.Dispose();
        CPUKernelAOS.offsets2DBuffer.Dispose();
        CPUKernelAOS.particleResultBuffer.Dispose();
        CPUKernelAOS.keyarrbuffer.Dispose();
    }

    void checkforReloadRequest(){
        if(reloadRequest){
            Destroy(this);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
    public void requestReload(){
        reloadRequest = true;
    }

    public bool getReloadRequest(){
        return reloadRequest;
    }
}
