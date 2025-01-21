using System.Collections.Generic;
using UnityEngine;

public class MultiParticleDisplay2D : MonoBehaviour, IParticleDisplay
{
	public Mesh mesh;
	public Shader shader;
	Material material;
	ComputeBuffer argsBuffer;
	Bounds bounds;
	Texture2DArray gradientArray;
    [SerializeField] private int gradientResolution = 64;
	ComputeBuffer visualParamsBuffer;
    public Dictionary<FluidType, Texture2D> gradientTextures;

	public void Init(Simulation2DAoSCounting sim)
	{
        material = new Material(shader);
		material.SetBuffer("Particles", sim.particleBuffer);

		CreateAndSetupVisualParamsBuffer(sim.fluidDataArray);

		argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.particleBuffer.count);
		bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
	}

	public void Init(Simulation2DAoS_CPUCSort sim)
	{
        material = new Material(shader);
		material.SetBuffer("Particles", sim.particleBuffer);

        CreateAndSetupVisualParamsBuffer(sim.fluidDataArray);

		argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.particleBuffer.count);
		bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
	}


	void LateUpdate()
	{
		if (shader != null)
		{
			Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
		}
	}


	public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
	{
		if (texture == null)
		{
			texture = new Texture2D(width, 1);
		}
		else if (texture.width != width)
		{
			texture.Reinitialize(width, 1);
		}
		if (gradient == null)
		{
			gradient = new Gradient();
			gradient.SetKeys(
				new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
				new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
			);
		}
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = filterMode;

		Color[] cols = new Color[width];
		for (int i = 0; i < cols.Length; i++)
		{
			float t = i / (cols.Length - 1f);
			cols[i] = gradient.Evaluate(t);
		}
		texture.SetPixels(cols);
		texture.Apply();
	}

	void OnValidate()
	{
		//needsUpdate = true;
	}

	void OnDestroy()
	{
		ComputeHelper.Release(argsBuffer, visualParamsBuffer);
		foreach (var tex in gradientTextures.Values)
        {
            if (tex != null)
                Destroy(tex);
        }
	}

	void CreateAndSetupVisualParamsBuffer(FluidData[] fluidDataArray){
		// Create and set up visual parameters buffer
        var visualParams = new FluidData.VisualParamBuffer[fluidDataArray.Length];
        for (int i = 0; i < fluidDataArray.Length; i++)
        {
            visualParams[i] = fluidDataArray[i].GetVisualParams();
        }
        
        visualParamsBuffer = ComputeHelper.CreateStructuredBuffer<FluidData.VisualParamBuffer>(visualParams.Length);
        visualParamsBuffer.SetData(visualParams);
        material.SetBuffer("VisualParamsBuffer", visualParamsBuffer);

        // Set up gradient textures
        gradientTextures = new Dictionary<FluidType, Texture2D>();
        gradientArray = new Texture2DArray(gradientResolution, 1, fluidDataArray.Length, TextureFormat.RGBA32, false);
        
        for (int i = 0; i < fluidDataArray.Length; i++)
        {
            var fluidData = fluidDataArray[i];
            var gradientTex = new Texture2D(gradientResolution, 1, TextureFormat.RGBA32, false);
            TextureFromGradient(ref gradientTex, gradientResolution, fluidData.visualParams.colorGradient);
            gradientTextures[fluidData.fluidType] = gradientTex;
            
            // Copy to texture array
            Graphics.CopyTexture(gradientTex, 0, gradientArray, i);
        }
        
        material.SetTexture("_GradientArray", gradientArray);
	}
}
