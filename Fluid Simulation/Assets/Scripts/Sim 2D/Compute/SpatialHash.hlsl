static const int2 offsets2D[9] =
{
	int2(-1, 1),
	int2(0, 1),
	int2(1, 1),
	int2(-1, 0),
	int2(0, 0),
	int2(1, 0),
	int2(-1, -1),
	int2(0, -1),
	int2(1, -1),
};

// Convert floating point position into an integer cell coordinate
int2 GetCell2D(float2 position, float radius)
{
	return (int2)floor(position / radius);
}

// Hash cell coordinate to a single unsigned integer
uint HashCell2D(int2 cell)
{
	return (cell.x << 16) | (cell.y + (2 << 15 - 1));
}
