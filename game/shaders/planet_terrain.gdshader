shader_type spatial;
// Planet terrain (K2-08): Voxel Tools Transvoxel mesh with one texture index per voxel (TEXTURES_SINGLE_S4), triplanar
// sampling of a Texture2DArray of placeholder textures (final art: K8-50). Vertex data layout: docs of Voxel Tools,
// "smooth_terrain": CUSTOM0 = transition smoothing, CUSTOM1.x = 4 packed texture indices, CUSTOM1.y = 4 packed weights.

uniform sampler2DArray u_textures : source_color, filter_linear_mipmap, repeat_enable;
uniform float u_texture_scale = 0.25;
// Share of the impostor in the cross-fade (K5-22 decision 13): the terrain discards the Bayer pixels the impostor draws.
uniform float u_blend = 0.0;

// Assigned by the voxel engine: neighbours of lower detail (layout 00000000 00000000 00000000 00xxyyzz).
uniform int u_transition_mask;

varying vec4 v_indices;
varying vec4 v_weights;
varying vec3 v_normal;
varying vec3 v_pos;

float get_transvoxel_secondary_factor(int idata) {
	int transition_mask = u_transition_mask & 0xff;
	int cell_border_mask = idata & 63;
	int vertex_border_mask = (idata >> 8) & 63;
	int m = transition_mask & cell_border_mask;
	float t = float(m != 0);
	t *= float((vertex_border_mask & ~transition_mask) == 0);
	return t;
}

vec3 get_transvoxel_position(vec3 vertex_pos, vec4 fdata) {
	int idata = floatBitsToInt(fdata.a);
	float secondary_factor = get_transvoxel_secondary_factor(idata);
	vec3 pos = mix(vertex_pos, fdata.xyz, secondary_factor);
	// Transition vertices of inactive transitions collapse into degenerate triangles.
	int itransition = (idata >> 16) & 0xff;
	float transition_cull = float(itransition == 0 || (itransition & u_transition_mask) != 0);
	return pos * transition_cull;
}

vec4 decode_8bit_vec4(float v) {
	uint i = floatBitsToUint(v);
	return vec4(
		float(i & uint(0xff)),
		float((i >> uint(8)) & uint(0xff)),
		float((i >> uint(16)) & uint(0xff)),
		float((i >> uint(24)) & uint(0xff)));
}

// 4 x 4 Bayer threshold at a pixel, the same as ImpostorBlend.ImpostorDraws (K5-22 decision 13).
float bayer4(vec2 frag) {
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	ivec2 p = ivec2(floor(frag)) & ivec2(3);
	return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

vec3 get_triplanar_blend(vec3 world_normal) {
	vec3 blending = abs(world_normal);
	blending = max(blending, vec3(0.00001));
	return blending / (blending.x + blending.y + blending.z);
}

vec3 sample_triplanar(vec3 pos, vec3 blend, float index) {
	vec3 xaxis = texture(u_textures, vec3(pos.yz, index)).rgb;
	vec3 yaxis = texture(u_textures, vec3(pos.xz, index)).rgb;
	vec3 zaxis = texture(u_textures, vec3(pos.xy, index)).rgb;
	return xaxis * blend.x + yaxis * blend.y + zaxis * blend.z;
}

void vertex() {
	VERTEX = get_transvoxel_position(VERTEX, CUSTOM0);
	v_indices = decode_8bit_vec4(CUSTOM1.x);
	v_weights = decode_8bit_vec4(CUSTOM1.y) / 255.0;
	// K5-22 decision 14: triplanar coordinates in model space, never world or view. A mesh block of Voxel Tools sits at its origin in
	// the terrain (identity basis, origin a multiple of 16 m), the terrain at the planet centre (planet frame), so the block-local vertex
	// differs from the planet-frame one by a multiple of 16 m = 4 texture periods at u_texture_scale 0.25: seamless, exact far from
	// the world origin (no float jitter about 110 km away).
	v_pos = VERTEX;
	v_normal = normalize(NORMAL);
}

void fragment() {
	if (bayer4(FRAGCOORD.xy) < u_blend) {
		discard;
	}
	vec3 blend = get_triplanar_blend(v_normal);
	vec3 p = v_pos * u_texture_scale;
	vec4 weights = v_weights;
	weights /= (weights.x + weights.y + weights.z + weights.w + 0.00001);
	vec3 col = sample_triplanar(p, blend, v_indices.x) * weights.x
		+ sample_triplanar(p, blend, v_indices.y) * weights.y
		+ sample_triplanar(p, blend, v_indices.z) * weights.z
		+ sample_triplanar(p, blend, v_indices.w) * weights.w;
	ALBEDO = col;
	ROUGHNESS = 0.95;
}
