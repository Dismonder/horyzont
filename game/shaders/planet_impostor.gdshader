shader_type spatial;
// Planet impostor (K5-22 decision 12): vertex colours from the material palette, Lambert lighting from the sun (SunLight, a
// DirectionalLight3D). Positions and normals are those of the model (planet frame); the shader reads no world or view coordinates.
render_mode diffuse_lambert, specular_disabled, cull_back;

// Share of the impostor in the cross-fade with the voxel terrain (decision 13): 1 = impostor only.
instance uniform float u_blend = 1.0;

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
}
