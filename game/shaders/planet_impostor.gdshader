shader_type spatial;
// Planet impostor (K5-22 decision 12): vertex colours from the material palette, Lambert lighting from the sun (SunLight, a
// DirectionalLight3D). Positions and normals are those of the model (planet frame); the shader reads no world or view coordinates.
render_mode diffuse_lambert, specular_disabled, cull_back;

// Share of the impostor in the cross-fade with the voxel terrain (decision 13): 1 = impostor only.
uniform float u_blend = 1.0;

// 4 x 4 Bayer threshold at a pixel, the same as ImpostorBlend.ImpostorDraws (K5-22 decision 13).
float bayer4(vec2 frag) {
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	ivec2 p = ivec2(floor(frag)) & ivec2(3);
	return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

void fragment() {
	// The impostor draws the Bayer pixels under its share; the terrain draws the others (complementary, no sorting, no z-fight).
	if (bayer4(FRAGCOORD.xy) >= u_blend) {
		discard;
	}
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
}
