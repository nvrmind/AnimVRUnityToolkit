
float4x4 _DeformationMatrix;
float4x4 _DeformationMatrixInv;
float _DeformationFactor;

float4 ApplyDeformation(float4 vertex) {

	vertex = mul(_DeformationMatrixInv, vertex);

	float f = _DeformationFactor;

	float x = vertex.x, y = vertex.y, z = vertex.z;
	float scale = (z * z * f - f + 1.0f);

	vertex.x = x * scale;
	vertex.y = y * scale;
	vertex.z = z * (1.0f + f);

	vertex = mul(_DeformationMatrix, vertex);
	return vertex;
}