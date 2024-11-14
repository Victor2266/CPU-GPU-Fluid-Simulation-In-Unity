using Unity.Mathematics;
using System;
public class SpatialHashCPU{
    public int2[] offsets2D =
    {
    	new int2(-1, 1),
    	new int2(0, 1),
    	new int2(1, 1),
    	new int2(-1, 0),
    	new int2(0, 0),
    	new int2(1, 0),
    	new int2(-1, -1),
    	new int2(0, -1),
    	new int2(1, -1),
    };
    
    // Constants used for hashing
    const uint hashK1 = 15823;
    const uint hashK2 = 9737333;
    
    // Convert floating point position into an integer cell coordinate
    public int2 GetCell2D(float2 position, float radius)
    {
    	int2 temp = new int2(0,0);
        temp[0] = (int)Math.Floor(position[0] / radius);
        temp[1] = (int)Math.Floor(position[1] / radius);
        return temp;
    }
    
    // Hash cell coordinate to a single unsigned integer
    public uint HashCell2D(int2 cell)
    {
    	cell = (int2)(uint2)cell;
    	uint a = (uint)(cell.x * hashK1);
    	uint b = (uint)(cell.y * hashK2);
    	return (a + b);
    }
    
    public uint KeyFromHash(uint hash, uint tableSize)
    {
    	return hash % tableSize;
    }
}

